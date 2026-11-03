using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public partial class PurchaseRepository : IPurchaseRepository, IPurchaseChangeRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public PurchaseRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private const string HeaderSql = """
        SELECT p.purchase_id AS PurchaseId, p.purchase_number AS PurchaseNumber,
            p.supplier_id AS SupplierId, COALESCE(p.supplier_name_snapshot,s.supplier_name) AS SupplierName,
            CASE WHEN p.supplier_snapshot_at IS NULL THEN s.supplier_code ELSE p.supplier_code_snapshot END AS SupplierCode,
            CASE WHEN p.supplier_snapshot_at IS NULL THEN s.gst_number ELSE p.supplier_gst_snapshot END AS SupplierGstNumber,
            CASE WHEN p.supplier_snapshot_at IS NULL THEN s.address ELSE p.supplier_address_snapshot END AS SupplierAddress,
            (p.supplier_snapshot_at IS NOT NULL) AS HasSupplierSnapshot,p.payment_terms_days AS PaymentTermsDays,
            p.supplier_invoice_number AS SupplierInvoiceNumber,
            p.purchase_date AS PurchaseDate,p.supplier_invoice_date AS SupplierInvoiceDate,p.received_date AS ReceivedDate,
            p.freight_amount AS FreightAmount,p.other_landed_cost AS OtherLandedCost,p.invoice_attachment_name AS InvoiceAttachmentName,
            COALESCE((SELECT sum(amount) FROM supplier_payments sp WHERE sp.purchase_id=p.purchase_id),0) AS PaidAmount,
            COALESCE((SELECT sum(return_total) FROM purchase_returns pr WHERE pr.purchase_id=p.purchase_id),0) AS ReturnedAmount,
            COALESCE((SELECT sum(CASE WHEN h.request_payload->>'TransactionType'='ADJUSTMENT_IN' THEN (h.request_payload->>'Quantity')::bigint ELSE -(h.request_payload->>'Quantity')::bigint END) FROM purchase_changes h WHERE h.purchase_id=p.purchase_id AND h.change_type='STOCK'),0)::bigint AS StockCorrectionQuantity,
            p.subtotal AS Subtotal, p.tax_amount AS TaxAmount, p.discount_amount AS DiscountAmount,
            p.total_amount AS TotalAmount, p.status AS Status, p.notes AS Notes,
            p.created_by AS CreatedBy, p.created_at AS CreatedAt,p.edit_version AS EditVersion,p.internal_reference AS InternalReference
        FROM purchases p LEFT JOIN suppliers s ON s.supplier_id = p.supplier_id
        """;
    private const string ItemsSql = """
        SELECT i.purchase_item_id AS PurchaseItemId, i.product_id AS ProductId, i.variant_id AS VariantId, i.discount_amount AS DiscountAmount,
            i.received_quantity AS ReceivedQuantity,i.damaged_quantity AS DamagedQuantity,i.pending_received_quantity AS PendingReceivedQuantity,
            i.pending_damaged_quantity AS PendingDamagedQuantity,
            COALESCE(to_jsonb(i)->>'product_name_snapshot',p.product_name) || ' / ' || COALESCE(i.variant_name_snapshot,v.variant_name) AS ProductName, i.quantity AS Quantity, i.purchase_price AS PurchasePrice,
            COALESCE(to_jsonb(i)->>'variant_attributes_snapshot','{}') AS AttributesJson,to_jsonb(i)->>'sku_snapshot' AS Sku,
            i.tax_percentage AS TaxPercentage, i.line_total AS LineTotal,
            COALESCE(i.is_serial_tracked, p.is_serial_tracked) AS IsSerialTracked,
            i.serial_numbers AS SerialNumbers,i.damaged_serial_numbers AS DamagedSerialNumbers,
            i.pending_serial_numbers AS PendingSerialNumbers,i.pending_damaged_serial_numbers AS PendingDamagedSerialNumbers,
            i.unit_of_measure AS UnitOfMeasure,i.pack_quantity AS PackQuantity,i.mrp AS Mrp,i.supplier_product_code AS SupplierProductCode,
            i.batch_number AS BatchNumber,i.manufacturing_date AS ManufacturingDate,i.expiry_date AS ExpiryDate
        FROM purchase_items i LEFT JOIN products p ON p.product_id = i.product_id LEFT JOIN product_variants v ON v.variant_id=i.variant_id
        WHERE i.purchase_id = @Id ORDER BY i.purchase_item_id
        """;
    public async Task<IEnumerable<PurchaseResponse>> GetAllAsync()
    {
        using var connection = factory.CreateConnection();
        return await connection.QueryAsync<PurchaseResponse>(HeaderSql + " ORDER BY p.purchase_id DESC;");
    }

    public async Task<PurchaseResponse?> GetByIdAsync(long id)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var result = await ReadAsync(connection, transaction, id);
        await transaction.CommitAsync();
        return result;
    }

    public async Task<PurchaseAttachment?> GetAttachmentAsync(long id)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PurchaseAttachment>("SELECT invoice_attachment_name AS FileName,invoice_attachment_content_type AS ContentType,invoice_attachment_data AS Data FROM purchases WHERE purchase_id=@id AND invoice_attachment_data IS NOT NULL",
new
{
    id
});
    }

    private static async Task<PurchaseResponse?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long id)
    {
        var header = await connection.QuerySingleOrDefaultAsync<PurchaseResponse>(HeaderSql + " WHERE p.purchase_id = @Id;", new
        {
            Id = id
        }, transaction);
        if (header is not null)
        {
            header.Items = (await connection.QueryAsync<PurchaseItemResponse>(ItemsSql, new
            {
                Id = id
            }, transaction)).ToList();
        }

        return header;
    }

    public async Task<long> CreatePurchaseAsync(CreatePurchaseRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        PurchaseRules.ValidateAndCalculate(request);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        if (request.CompleteImmediately && request.RequestId is { } requestId && requestId != Guid.Empty)
        {
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",
                new { key = "StockFlow.DirectReceipt:" + requestId }, transaction);
            var prior = await connection.QuerySingleOrDefaultAsync<DirectReceiptRetry>("""
                SELECT purchase_id AS PurchaseId,
                    created_by=@userId AND request_payload=CAST(@payload AS jsonb) AS Matches
                FROM purchase_receipts WHERE request_id=@requestId
                """, new { requestId, userId, payload = JsonSerializer.Serialize(request) }, transaction);
            if (prior is not null)
            {
                if (!prior.Matches) throw new ValidationException("This request ID belongs to a different receipt.");
                await transaction.CommitAsync();
                return prior.PurchaseId;
            }
        }
        var id = await CreateInTransactionAsync(connection, transaction, request, userId, factory.RequiresExplicitRetailVariant);
        await transaction.CommitAsync();
        return id;
    }

    internal static async Task<long> CreateInTransactionAsync(NpgsqlConnection connection,
NpgsqlTransaction transaction,
CreatePurchaseRequest request,
int userId, bool requireExplicitVariant = false)
    {
        var totals = PurchaseRules.ValidateAndCalculate(request);
        var retryPayload = request.RequestId.HasValue ? JsonSerializer.Serialize(request) : null;
        var products = await ValidateReferencesAsync(connection, transaction, request, request.CompleteImmediately, requireExplicitVariant);
        // PostgreSQL's existing BIGSERIAL sequence is concurrency-safe; gaps after rollback are expected.
        var id = await connection.ExecuteScalarAsync<long>("SELECT nextval(pg_get_serial_sequence('purchases', 'purchase_id'));", transaction: transaction);
        var number = $"PUR-{DateTime.UtcNow.Year}-{id:D6}";
        await connection.ExecuteAsync("""
            INSERT INTO purchases (purchase_id, supplier_id, purchase_number, supplier_invoice_number,supplier_invoice_date,
                purchase_date, received_date, subtotal, tax_amount, discount_amount,freight_amount,other_landed_cost,total_amount, status, notes, internal_reference, created_by,payment_terms_days,
                invoice_attachment_name,invoice_attachment_content_type,invoice_attachment_data)
            VALUES (@Id, @SupplierId, @Number, @SupplierInvoiceNumber,@SupplierInvoiceDate, @PurchaseDate, @ReceivedDate,
                @Subtotal, @Tax, @DiscountAmount,@FreightAmount,@OtherLandedCost, @Total, 'DRAFT', @Notes, @InternalReference, @UserId,@PaymentTermsDays,
                @InvoiceAttachmentName,@InvoiceAttachmentContentType,@InvoiceAttachmentData);
            """,
new
{
    Id = id,
    request.SupplierId,
    Number = number,
    request.SupplierInvoiceNumber,
    request.SupplierInvoiceDate,
    request.PurchaseDate,
    request.ReceivedDate,
    totals.Subtotal,
    totals.Tax,
    request.DiscountAmount,
    request.FreightAmount,
    request.OtherLandedCost,
    totals.Total,
    request.Notes,
    request.InternalReference,
    request.PaymentTermsDays,
    request.InvoiceAttachmentName,
    request.InvoiceAttachmentContentType,
    request.InvoiceAttachmentData,
    UserId = userId
},
transaction);
        await SaveItemsAsync(connection, transaction, id, request, products);
        if (request.CompleteImmediately)
        {
            await PostStockAsync(connection, transaction, id, userId, request, retryPayload);
        }

        return id;
    }

    public async Task<bool> UpdateDraftAsync(long id, CreatePurchaseRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        var totals = PurchaseRules.ValidateAndCalculate(request);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        var status = await LockPurchaseAsync(connection, transaction, id);
        if (status is null)
        {
            return false;
        }

        if (status != "DRAFT")
        {
            throw new ValidationException("Only draft purchases can be edited.");
        }

        var existing = (await ReadAsync(connection, transaction, id))!;
        Version(existing.EditVersion, request.ExpectedVersion);
        if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM purchase_receipts WHERE purchase_id=@id) OR EXISTS(SELECT 1 FROM supplier_payments WHERE purchase_id=@id) OR EXISTS(SELECT 1 FROM purchase_returns WHERE purchase_id=@id)", new { id }, transaction))
            throw new ValidationException("This purchase has receipts or payments. Use the restricted edit actions on Purchase Details.");
        var before = await ChangeSnapshot(connection, transaction, id);
        var oldFile = await OldFile(connection, transaction, id);
        var products = await ValidateReferencesAsync(connection, transaction, request, request.CompleteImmediately, factory.RequiresExplicitRetailVariant);
        await connection.ExecuteAsync("""
            UPDATE purchases SET supplier_id = @SupplierId, supplier_invoice_number = @SupplierInvoiceNumber,
                supplier_invoice_date=@SupplierInvoiceDate,purchase_date = @PurchaseDate, received_date = @ReceivedDate, subtotal = @Subtotal,
                tax_amount = @Tax, discount_amount = @DiscountAmount,freight_amount=@FreightAmount,other_landed_cost=@OtherLandedCost,total_amount = @Total, notes = @Notes,internal_reference=@InternalReference,payment_terms_days=@PaymentTermsDays,
                invoice_attachment_name=COALESCE(@InvoiceAttachmentName,invoice_attachment_name),invoice_attachment_content_type=COALESCE(@InvoiceAttachmentContentType,invoice_attachment_content_type),invoice_attachment_data=COALESCE(@InvoiceAttachmentData,invoice_attachment_data)
            WHERE purchase_id = @Id;
            DELETE FROM purchase_items WHERE purchase_id = @Id;
            """,
new
{
    Id = id,
    request.SupplierId,
    request.SupplierInvoiceNumber,
    request.SupplierInvoiceDate,
    request.PurchaseDate,
    request.ReceivedDate,
    totals.Subtotal,
    totals.Tax,
    request.DiscountAmount,
    request.FreightAmount,
    request.OtherLandedCost,
    totals.Total,
    request.Notes,
    request.InternalReference,
    request.PaymentTermsDays,
    request.InvoiceAttachmentName,
    request.InvoiceAttachmentContentType,
    request.InvoiceAttachmentData
},
transaction);
        await SaveItemsAsync(connection, transaction, id, request, products);
        await WriteChange(connection, transaction, id, request.RequestId ?? Guid.NewGuid(), "DRAFT_EDIT",
            string.IsNullOrWhiteSpace(request.ChangeReason) ? "Draft purchase updated" : request.ChangeReason.Trim(),
            before, await ChangeSnapshot(connection, transaction, id), JsonSerializer.Serialize(request), userId, totals.Total-existing.TotalAmount, oldFile: oldFile);
        if (request.CompleteImmediately)
        {
            await PostStockAsync(connection, transaction, id, userId);
        }

        await transaction.CommitAsync();
        return true;
    }

    public async Task<bool> CompleteAsync(long id, int userId)
    {
        factory.RequireLocation();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        var status = await LockPurchaseAsync(connection, transaction, id);
        if (status is null)
        {
            return false;
        }

        if (status == "COMPLETED")
        {
            await transaction.CommitAsync();
            return true;
        }

        if (status != "DRAFT")
        {
            throw new ValidationException("Only draft purchases can be completed.");
        }

        var purchase = (await ReadAsync(connection, transaction, id))!;
        var request = new CreatePurchaseRequest
        {
            SupplierId = purchase.SupplierId,
            PurchaseDate = purchase.PurchaseDate,
            SupplierInvoiceDate = purchase.SupplierInvoiceDate,
            ReceivedDate = purchase.ReceivedDate,
            DiscountAmount = purchase.DiscountAmount,
            FreightAmount = purchase.FreightAmount,
            OtherLandedCost = purchase.OtherLandedCost,
            CompleteImmediately = true,
            Items = purchase.Items.Select(i => new CreatePurchaseItemRequest { ProductId = i.ProductId, VariantId = i.VariantId, DiscountAmount = i.DiscountAmount, Quantity = i.Quantity, ReceivedQuantity = i.PendingReceivedQuantity, DamagedQuantity = i.PendingDamagedQuantity, PurchasePrice = i.PurchasePrice, TaxPercentage = i.TaxPercentage, SerialNumbers = i.PendingSerialNumbers.ToList(), DamagedSerialNumbers = i.PendingDamagedSerialNumbers.ToList(), UnitOfMeasure = i.UnitOfMeasure, PackQuantity = i.PackQuantity, Mrp = i.Mrp, SupplierProductCode = i.SupplierProductCode, BatchNumber = i.BatchNumber, ManufacturingDate = i.ManufacturingDate, ExpiryDate = i.ExpiryDate })
.ToList()
        };
        PurchaseRules.ValidateAndCalculate(request);
        var products = await ValidateReferencesAsync(connection, transaction, request, true, factory.RequiresExplicitRetailVariant);
        if (purchase.Items.Any(i => i.IsSerialTracked != products[i.VariantId!.Value]))
        {
            throw new ValidationException("A product's serial-tracking setting changed. Edit and save the draft before completing it.");
        }

        await PostStockAsync(connection, transaction, id, userId);
        await transaction.CommitAsync();
        return true;
    }

    private static Task<string?> LockPurchaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long id)
    {
        return connection.QuerySingleOrDefaultAsync<string>("SELECT status FROM purchases WHERE purchase_id = @Id FOR UPDATE;", new
        {
            Id = id
        }, transaction);
    }

    private static async Task<Dictionary<long, bool>> ValidateReferencesAsync(NpgsqlConnection connection,
NpgsqlTransaction transaction,
CreatePurchaseRequest request,
bool completing, bool requireExplicitVariant)
    {
        var supplier = await connection.QuerySingleOrDefaultAsync<int?>("SELECT supplier_id FROM suppliers WHERE supplier_id = @SupplierId AND is_active FOR SHARE;",
new
{
    request.SupplierId
},
transaction);
        if (supplier is null)
        {
            throw new ValidationException("Select an active supplier.");
        }

        var products = new Dictionary<long, bool>();
        foreach (var id in request.Items.Select(i => i.ProductId).Distinct().Order())
        {
            var tracked = await connection.QuerySingleOrDefaultAsync<bool?>("SELECT is_serial_tracked FROM products WHERE product_id = @Id AND is_active FOR UPDATE;",
new
{
    Id = id
},
transaction);
            if (tracked is null)
            {
                throw new ValidationException($"Product {id} is missing or inactive.");
            }
        }

        foreach (var item in request.Items.OrderBy(i => i.VariantId))
        {
            var variant = await VariantRepository.ResolveAsync(connection, transaction, item.ProductId, item.VariantId, requireExplicitVariant: requireExplicitVariant);
            item.VariantId = variant.VariantId;
            products[variant.VariantId] = variant.IsSerialTracked;
            PurchaseRules.ValidateSerials(item, variant.IsSerialTracked, completing);
        }

        return products;
    }

    private static async Task SaveItemsAsync(NpgsqlConnection connection,
NpgsqlTransaction transaction,
long id,
CreatePurchaseRequest request,
Dictionary<long, bool> products)
    {
        foreach (var item in request.Items)
        {
            var net = PurchaseRules.Money(item.Quantity * item.PurchasePrice) - item.DiscountAmount;
            var total = net + PurchaseRules.Money(net * item.TaxPercentage / 100m);
            var pendingReceived = item.ReceivedQuantity ?? (request.CompleteImmediately ? item.Quantity : 0);
            await connection.ExecuteAsync("""
                INSERT INTO purchase_items (purchase_id, product_id, variant_id, variant_name_snapshot, discount_amount, quantity, purchase_price, tax_percentage,
                    line_total, serial_numbers,damaged_serial_numbers,pending_received_quantity,pending_damaged_quantity,pending_serial_numbers,pending_damaged_serial_numbers,
                    unit_of_measure,pack_quantity,mrp,supplier_product_code,batch_number,manufacturing_date,expiry_date,is_serial_tracked)
                VALUES (@Id, @ProductId, @VariantId, (SELECT variant_name FROM product_variants WHERE variant_id=@VariantId), @DiscountAmount, @Quantity, @PurchasePrice, @TaxPercentage, @Total, @Serials,@DamagedSerials,@PendingReceived,@DamagedQuantity,@Serials,@DamagedSerials,
                    @UnitOfMeasure,@PackQuantity,@Mrp,@SupplierProductCode,@BatchNumber,@ManufacturingDate,@ExpiryDate,@Tracked);
                """,
new
{
    Id = id,
    item.ProductId,
    item.VariantId,
    item.DiscountAmount,
    item.Quantity,
    item.PurchasePrice,
    item.TaxPercentage,
    Total = total,
    PendingReceived = pendingReceived,
    item.DamagedQuantity,
    Serials = item.SerialNumbers.ToArray(),
    DamagedSerials = item.DamagedSerialNumbers.ToArray(),
    item.UnitOfMeasure,
    item.PackQuantity,
    item.Mrp,
    item.SupplierProductCode,
    item.BatchNumber,
    item.ManufacturingDate,
    item.ExpiryDate,
    Tracked = products[item.VariantId!.Value]
},
transaction);
        }
    }

    private sealed class DirectReceiptRetry
    {
        public long PurchaseId { get; set; }
        public bool Matches { get; set; }
    }

    private static async Task PostStockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long id, int userId, CreatePurchaseRequest? request = null, string? retryPayload = null)
    {
        var receiptId = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO purchase_receipts(purchase_id,received_date,supplier_invoice_number,supplier_invoice_date,notes,created_by,request_id,request_payload)
            SELECT purchase_id,COALESCE(received_date,purchase_date),supplier_invoice_number,supplier_invoice_date,notes,@userId,@requestId,CAST(@payload AS jsonb) FROM purchases WHERE purchase_id=@id RETURNING receipt_id;
            """,
new
{
    id,
    userId,
    requestId = request?.RequestId is { } retryId && retryId != Guid.Empty ? (Guid?)retryId : null,
    payload = retryPayload ?? (request is null ? null : JsonSerializer.Serialize(request))
},
transaction);
        var items = (await connection.QueryAsync<PurchaseItemResponse>(ItemsSql, new
        {
            Id = id
        }, transaction)).ToList();
        if (items.Sum(x => x.PendingReceivedQuantity + x.PendingDamagedQuantity) <= 0)
        {
            throw new ValidationException("Enter received or damaged quantities before posting this receipt.");
        }

        foreach (var item in items)
        {
            var received = item.PendingReceivedQuantity;
            var damaged = item.PendingDamagedQuantity;
            if (received + damaged <= 0)
            {
                continue;
            }

            await connection.ExecuteAsync("""
                INSERT INTO purchase_receipt_items(receipt_id,purchase_item_id,quantity,damaged_quantity,serial_numbers,damaged_serial_numbers,purchase_price,discount_amount,tax_percentage,unit_of_measure,pack_quantity,mrp,supplier_product_code,batch_number,manufacturing_date,expiry_date)
                VALUES(@receiptId,@PurchaseItemId,@received,@damaged,@PendingSerialNumbers,@PendingDamagedSerialNumbers,@PurchasePrice,@DiscountAmount,@TaxPercentage,@UnitOfMeasure,@PackQuantity,@Mrp,@SupplierProductCode,@BatchNumber,@ManufacturingDate,@ExpiryDate);
                UPDATE purchase_items SET received_quantity=received_quantity+@received,damaged_quantity=damaged_quantity+@damaged,
                    serial_numbers=CASE WHEN received_quantity=0 THEN @PendingSerialNumbers ELSE serial_numbers||@PendingSerialNumbers END,
                    damaged_serial_numbers=damaged_serial_numbers||@PendingDamagedSerialNumbers,
                    pending_received_quantity=0,pending_damaged_quantity=0,pending_serial_numbers='{}',pending_damaged_serial_numbers='{}'
                WHERE purchase_item_id=@PurchaseItemId;
                """,
new
{
    receiptId,
    item.PurchaseItemId,
    received,
    damaged,
    item.PendingSerialNumbers,
    item.PendingDamagedSerialNumbers,
    item.PurchasePrice,
    item.DiscountAmount,
    item.TaxPercentage,
    item.UnitOfMeasure,
    item.PackQuantity,
    item.Mrp,
    item.SupplierProductCode,
    item.BatchNumber,
    item.ManufacturingDate,
    item.ExpiryDate
},
transaction);
            if (item.IsSerialTracked)
            {
                foreach (var serial in item.PendingSerialNumbers)
                {
                    if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE LOWER(BTRIM(serial_number)) = LOWER(@Serial));",
new
{
    Serial = serial
},
transaction))
                    {
                        throw new ValidationException($"Serial number {serial} already exists.");
                    }

                    var unitId = await connection.ExecuteScalarAsync<long>("""
                        INSERT INTO product_units (product_id, variant_id, purchase_id, purchase_item_id, receipt_id, serial_number, unit_status)
                        VALUES (@ProductId, @VariantId, @Id, @PurchaseItemId, @receiptId, @Serial, 'IN_STOCK') RETURNING product_unit_id;
                        """,
new
{
    item.ProductId,
    item.VariantId,
    Id = id,
    item.PurchaseItemId,
    receiptId,
    Serial = serial
},
transaction);
                    await AddMovementAsync(connection, transaction, id, receiptId, item.ProductId, item.VariantId!.Value, unitId, 1, userId);
                }

                foreach (var serial in item.PendingDamagedSerialNumbers)
                {
                    if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE LOWER(BTRIM(serial_number)) = LOWER(@Serial));",
new
{
    Serial = serial
},
transaction))
                    {
                        throw new ValidationException($"Serial number {serial} already exists.");
                    }

                    var unitId = await connection.ExecuteScalarAsync<long>("INSERT INTO product_units(product_id,variant_id,purchase_id,purchase_item_id,receipt_id,serial_number,unit_status) VALUES(@ProductId,@VariantId,@id,@PurchaseItemId,@receiptId,@serial,'DAMAGED') RETURNING product_unit_id",
new
{
    item.ProductId,
    item.VariantId,
    id,
    item.PurchaseItemId,
    receiptId,
    serial
},
transaction);
                    await AddMovementAsync(connection, transaction, id, receiptId, item.ProductId, item.VariantId!.Value, unitId, 1, userId);
                    await AddDamageMovementAsync(connection, transaction, id, receiptId, item.ProductId, item.VariantId.Value, unitId, 1, userId);
                }
            }
            else
            {
                if (received > 0)
                {
                    await AddMovementAsync(connection, transaction, id, receiptId, item.ProductId, item.VariantId!.Value, null, received, userId);
                }

                if (damaged > 0)
                {
                    await AddMovementAsync(connection, transaction, id, receiptId, item.ProductId, item.VariantId!.Value, null, damaged, userId);
                    await AddDamageMovementAsync(connection, transaction, id, receiptId, item.ProductId, item.VariantId.Value, null, damaged, userId);
                }
            }
        }

        await connection.ExecuteAsync("""
            UPDATE purchases SET status = CASE WHEN EXISTS(SELECT 1 FROM purchase_items WHERE purchase_id=@Id AND received_quantity+damaged_quantity<quantity) THEN 'PARTIALLY_RECEIVED' ELSE 'COMPLETED' END,
                received_date = COALESCE(received_date, purchase_date)
            WHERE purchase_id = @Id;
            INSERT INTO audit_logs (user_id, module_name, action_name, table_name, record_id, reason)
            VALUES (@UserId, 'Purchase', 'RECEIVE', 'purchases', @Id, 'Purchase receipt posted and purchase status recalculated');
            """,
new
{
    Id = id,
    UserId = userId
},
transaction);
    }

    private static Task<int> AddMovementAsync(NpgsqlConnection connection,
NpgsqlTransaction transaction,
long id,
long receiptId,
int productId,
long variantId,
long? unitId,
int quantity,
int userId)
    {
        return connection.ExecuteAsync("""
            INSERT INTO inventory_transactions (product_id, variant_id, product_unit_id, receipt_id, transaction_type, quantity,
                reference_type, reference_id, remarks, created_by)
            VALUES (@ProductId, @VariantId, @UnitId, @receiptId, 'PURCHASE', @Quantity, 'PURCHASE', @Id, 'Stock received through purchase', @UserId);
            """,
new
{
    ProductId = productId,
    VariantId = variantId,
    UnitId = unitId,
    receiptId,
    Quantity = quantity,
    Id = id,
    UserId = userId
},
transaction);
    }

    private static Task<int> AddDamageMovementAsync(NpgsqlConnection connection,
NpgsqlTransaction transaction,
long id,
long receiptId,
int productId,
long variantId,
long? unitId,
int quantity,
int userId)
    {
        return connection.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,receipt_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@productId,@variantId,@unitId,@receiptId,'DAMAGED',@quantity,'PURCHASE',@id,'Damaged during receiving',@userId)",
new
{
    productId,
    variantId,
    unitId,
    receiptId,
    quantity,
    id,
    userId
},
transaction);
    }
}
