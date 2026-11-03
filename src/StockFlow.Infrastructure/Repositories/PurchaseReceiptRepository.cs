using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class PurchaseReceiptRepository : IPurchaseReceiptRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public PurchaseReceiptRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<IEnumerable<PurchaseReceiptResponse>> GetAsync(long purchaseId)
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<PurchaseReceiptResponse>("""
            SELECT r.receipt_id AS ReceiptId,r.receipt_number AS ReceiptNumber,r.status AS Status,r.received_date AS ReceivedDate,r.supplier_invoice_number AS SupplierInvoiceNumber,r.supplier_invoice_date AS SupplierInvoiceDate,r.notes AS Notes,
                (SELECT SUM(quantity)::int FROM purchase_receipt_items WHERE receipt_id=r.receipt_id) AS Quantity,
                (SELECT SUM(damaged_quantity)::int FROM purchase_receipt_items WHERE receipt_id=r.receipt_id) AS DamagedQuantity
            FROM purchase_receipts r WHERE r.purchase_id=@purchaseId ORDER BY r.receipt_id
            """,
new
{
    purchaseId
});
    }

    public async Task<long> ReceiveAsync(long purchaseId, ReceivePurchaseRequest r, int userId)
    {
        factory.RequireLocation(r.LocationId);
        Validator.ValidateObject(r, new ValidationContext(r), true);
        if (r.RequestId == Guid.Empty || r.ReceivedDate == default || r.Items is null || r.Items.Count is < 1 or > 500 || r.Items.Any(i => i is null || i.PurchaseItemId <= 0 || i.Quantity is < 0 or > 10000 || i.DamagedQuantity is < 0 or > 10000 || i.Quantity + i.DamagedQuantity is < 1 or > 10000 || i.SerialNumbers is null || i.DamagedSerialNumbers is null))
        {
            throw new ValidationException("Enter a receipt date and valid received/damaged quantities for 1 to 500 lines.");
        }

        if (r.Items.Select(i => i.PurchaseItemId).Distinct().Count() != r.Items.Count)
        {
            throw new ValidationException("A purchase line can appear once per receipt.");
        }

        r.SupplierInvoiceDate = r.SupplierInvoiceDate is { } invoiceDate ? DateTime.SpecifyKind(invoiceDate.Date, DateTimeKind.Unspecified) : null;
        var payload = JsonSerializer.Serialize(new
        {
            purchaseId,
            request = r
        });
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", new
        {
            key = "StockFlow.Receipt:" + r.RequestId
        }, t);
        var prior = await c.QuerySingleOrDefaultAsync<ReceiptRetry>("SELECT receipt_id AS Id,created_by=@userId AND request_payload=CAST(@payload AS jsonb) AS Matches FROM purchase_receipts WHERE request_id=@RequestId",
new
{
    r.RequestId,
    userId,
    payload
},
t);
        if (prior is not null)
        {
            if (!prior.Matches)
            {
                throw new ValidationException("This request ID belongs to a different receipt.");
            }

            await t.CommitAsync();
            return prior.Id;
        }

        var header = await c.QuerySingleOrDefaultAsync<PurchaseResponse>("SELECT status AS Status,purchase_date AS PurchaseDate FROM purchases WHERE purchase_id=@purchaseId FOR UPDATE",
new
{
    purchaseId
},
t) ?? throw new ValidationException("Purchase not found.");
        if (header.Status is not ("DRAFT" or "PARTIALLY_RECEIVED"))
        {
            throw new ValidationException("Only open purchases can receive stock.");
        }

        if (r.ReceivedDate.Date < header.PurchaseDate.Date)
        {
            throw new ValidationException("Receipt date cannot precede the order date.");
        }

        var lines = (await c.QueryAsync<PurchaseItemResponse>("SELECT purchase_item_id AS PurchaseItemId,product_id AS ProductId,variant_id AS VariantId,quantity AS Quantity,received_quantity AS ReceivedQuantity,damaged_quantity AS DamagedQuantity,purchase_price AS PurchasePrice,discount_amount AS DiscountAmount,tax_percentage AS TaxPercentage,is_serial_tracked AS IsSerialTracked FROM purchase_items WHERE purchase_id=@purchaseId ORDER BY product_id,variant_id",
new
{
    purchaseId
},
t)).ToDictionary(i => i.PurchaseItemId);
        foreach (var productId in lines.Values.Select(i => i.ProductId).Distinct().Order())
        {
            await c.ExecuteAsync("SELECT product_id FROM products WHERE product_id=@productId FOR UPDATE", new
            {
                productId
            }, t);
        }

        var receiptId = await c.ExecuteScalarAsync<long>("INSERT INTO purchase_receipts(purchase_id,received_date,supplier_invoice_number,supplier_invoice_date,notes,request_id,request_payload,created_by) VALUES(@purchaseId,@date,@SupplierInvoiceNumber,@SupplierInvoiceDate,@Notes,@RequestId,CAST(@payload AS jsonb),@userId) RETURNING receipt_id",
new
{
    purchaseId,
    date = DateTime.SpecifyKind(r.ReceivedDate.Date, DateTimeKind.Unspecified),
    r.SupplierInvoiceNumber,
    r.SupplierInvoiceDate,
    r.Notes,
    r.RequestId,
    payload,
    userId
},
t);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var received in r.Items)
        {
            var resolved = received.Quantity + received.DamagedQuantity;
            if (!lines.TryGetValue(received.PurchaseItemId, out var line) || resolved > line.Quantity - line.ReceivedQuantity - line.DamagedQuantity)
            {
                throw new ValidationException("Receipt exceeds the outstanding order quantity.");
            }

            var variant = await VariantRepository.ResolveAsync(c, t, line.ProductId, line.VariantId, requireExplicitVariant: factory.RequiresExplicitRetailVariant);
            var serials = received.SerialNumbers.Select(s => s?.Trim() ?? "").ToArray();
            var damagedSerials = received.DamagedSerialNumbers.Select(s => s?.Trim() ?? "").ToArray();
            if (received.ExpiryDate < received.ManufacturingDate)
            {
                throw new ValidationException("Expiry date cannot precede manufacturing date.");
            }

            if (serials.Concat(damagedSerials).Any(s => s.Length is < 1 or > 150 || !seen.Add(s)) || (variant.IsSerialTracked ? serials.Length != received.Quantity || damagedSerials.Length != received.DamagedQuantity : serials.Length != 0 || damagedSerials.Length != 0))
            {
                throw new ValidationException("Capture one distinct valid serial per received or damaged serialized unit; quantity stock must not include serials.");
            }

            // Proportional cumulative rounding guarantees split receipts sum to the order's line discount.
            var previous = line.ReceivedQuantity + line.DamagedQuantity;
            var discount = PurchaseRules.Money(line.DiscountAmount * (previous + resolved) / line.Quantity) - PurchaseRules.Money(line.DiscountAmount * previous / line.Quantity);
            await c.ExecuteAsync("INSERT INTO purchase_receipt_items(receipt_id,purchase_item_id,quantity,damaged_quantity,serial_numbers,damaged_serial_numbers,purchase_price,discount_amount,tax_percentage,unit_of_measure,pack_quantity,mrp,supplier_product_code,batch_number,manufacturing_date,expiry_date) VALUES(@receiptId,@PurchaseItemId,@Quantity,@DamagedQuantity,@serials,@damagedSerials,@PurchasePrice,@discount,@TaxPercentage,@UnitOfMeasure,@PackQuantity,@Mrp,@SupplierProductCode,@BatchNumber,@ManufacturingDate,@ExpiryDate)",
new
{
    receiptId,
    received.PurchaseItemId,
    received.Quantity,
    received.DamagedQuantity,
    serials,
    damagedSerials,
    line.PurchasePrice,
    discount,
    line.TaxPercentage,
    received.UnitOfMeasure,
    received.PackQuantity,
    received.Mrp,
    received.SupplierProductCode,
    received.BatchNumber,
    received.ManufacturingDate,
    received.ExpiryDate
},
t);
            var units = new List<long?>();
            if (variant.IsSerialTracked)
            {
                foreach (var serial in serials)
                {
                    units.Add(await c.ExecuteScalarAsync<long>("INSERT INTO product_units(product_id,variant_id,purchase_id,purchase_item_id,receipt_id,serial_number,unit_status) VALUES(@ProductId,@VariantId,@purchaseId,@PurchaseItemId,@receiptId,@serial,'IN_STOCK') RETURNING product_unit_id",
new
{
    line.ProductId,
    line.VariantId,
    purchaseId,
    line.PurchaseItemId,
    receiptId,
    serial
},
t));
                }
            }
            else if (received.Quantity > 0)
            {
                units.Add(null);
            }

            foreach (var unit in units)
            {
                await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,receipt_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@ProductId,@VariantId,@unit,@receiptId,'PURCHASE',@quantity,'PURCHASE',@purchaseId,@remarks,@userId)",
new
{
    line.ProductId,
    line.VariantId,
    unit,
    receiptId,
    quantity = variant.IsSerialTracked ? 1 : received.Quantity,
    purchaseId,
    remarks = "Receipt #" + receiptId,
    userId
},
t);
            }

            if (variant.IsSerialTracked)
            {
                foreach (var serial in damagedSerials)
                {
                    var unit = await c.ExecuteScalarAsync<long>("INSERT INTO product_units(product_id,variant_id,purchase_id,purchase_item_id,receipt_id,serial_number,unit_status) VALUES(@ProductId,@VariantId,@purchaseId,@PurchaseItemId,@receiptId,@serial,'DAMAGED') RETURNING product_unit_id",
new
{
    line.ProductId,
    line.VariantId,
    purchaseId,
    received.PurchaseItemId,
    receiptId,
    serial
},
t);
                    await AddDamagedAsync(c, t, line.ProductId, line.VariantId!.Value, unit, receiptId, purchaseId, 1, userId);
                }
            }
            else if (received.DamagedQuantity > 0)
            {
                await AddDamagedAsync(c, t, line.ProductId, line.VariantId!.Value, null, receiptId, purchaseId, received.DamagedQuantity, userId);
            }

            await c.ExecuteAsync("UPDATE purchase_items SET received_quantity=received_quantity+@Quantity,damaged_quantity=damaged_quantity+@DamagedQuantity,serial_numbers=CASE WHEN received_quantity=0 THEN @serials ELSE serial_numbers||@serials END,damaged_serial_numbers=damaged_serial_numbers||@damagedSerials WHERE purchase_item_id=@PurchaseItemId",
new
{
    received.Quantity,
    received.DamagedQuantity,
    serials,
    damagedSerials,
    received.PurchaseItemId
},
t);
        }

        await c.ExecuteAsync("""
            UPDATE purchases SET status=CASE WHEN EXISTS(SELECT 1 FROM purchase_items WHERE purchase_id=@purchaseId AND received_quantity+damaged_quantity<quantity) THEN 'PARTIALLY_RECEIVED' ELSE 'COMPLETED' END,received_date=@date WHERE purchase_id=@purchaseId;
            INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,'Purchase','RECEIVE','purchase_receipts',@receiptId,CAST(@payload AS jsonb));
            """,
new
{
    purchaseId,
    date = DateTime.SpecifyKind(r.ReceivedDate.Date, DateTimeKind.Unspecified),
    userId,
    receiptId,
    payload
},
t);
        await t.CommitAsync();
        return receiptId;
    }

    private static async Task AddDamagedAsync(Npgsql.NpgsqlConnection c,
Npgsql.NpgsqlTransaction t,
int productId,
long variantId,
long? unit,
long receiptId,
long purchaseId,
int quantity,
int userId)
    {
        await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,receipt_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@productId,@variantId,@unit,@receiptId,'PURCHASE',@quantity,'PURCHASE',@purchaseId,'Damaged receipt',@userId),(@productId,@variantId,@unit,@receiptId,'DAMAGED',@quantity,'PURCHASE',@purchaseId,'Damaged during receiving',@userId)",
new
{
    productId,
    variantId,
    unit,
    receiptId,
    quantity,
    purchaseId,
    userId
},
t);
    }

    private sealed class ReceiptRetry
    {
        public long Id
        {
            get; set;
        }
        public bool Matches
        {
            get; set;
        }
    }
}
