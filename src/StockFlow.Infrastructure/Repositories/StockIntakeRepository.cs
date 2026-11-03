using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class StockIntakeRepository : IStockIntakeRepository
{
    public async Task GenerateItemLabelsAsync(long intakeId, int userId)
    {
        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await StockItemLabels.Generate(c, factory.Scope!.LocationId, null, intakeId, userId);
    }
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public StockIntakeRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<IReadOnlyList<StockLabelResponse>?> LabelsAsync(long intakeId, string? printAction = null, int userId = 0)
    {
        factory.RequireLocation();
        if (printAction is not null and not "PRINT" and not "REPRINT")
        {
            throw new ValidationException("Invalid label action.");
        }

        await using var c = factory.CreateConnection();
        var locationId = factory.Scope!.LocationId;
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM stock_intakes WHERE intake_id=@intakeId AND location_id=@locationId)",
new
{
    intakeId,
    locationId
}))
        {
            return null;
        }

        if (await c.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM stock_intake_items i WHERE i.intake_id=@intakeId AND i.location_id=@locationId
                AND cardinality(i.serial_numbers)>(SELECT count(*) FROM product_units u WHERE u.variant_id=i.variant_id AND u.serial_number=ANY(i.serial_numbers)))
            """,
new
{
    intakeId,
    locationId
}))
        {
            throw new ValidationException("Some individual units are no longer available in this location. Their labels cannot be prepared from this receipt.");
        }

        var rows = (await c.QueryAsync<StockLabelResponse>("""
            SELECT i.variant_id VariantId,u.product_unit_id UnitId,
                i.product_name_snapshot || CASE WHEN i.variant_name_snapshot='DEFAULT' THEN '' ELSE ' / ' || i.variant_name_snapshot END ProductName,
                v.sku Sku,(SELECT string_agg(a.key || ': ' || a.value,' / ' ORDER BY a.key) FROM jsonb_each_text(v.attributes) a WHERE lower(a.key) IN ('size','color','colour')) VariantDetails,
                u.serial_number SerialNumber,CASE WHEN cardinality(i.serial_numbers)>0 THEN 1 ELSE i.quantity END Quantity,
                CASE WHEN cardinality(i.serial_numbers)>0 THEN COALESCE(u.manufacturer_barcode,u.barcode,u.serial_number)
                     ELSE (SELECT barcode FROM variant_barcodes WHERE variant_id=i.variant_id AND is_active AND barcode_source='MANUFACTURER' ORDER BY is_primary DESC,variant_barcode_id LIMIT 1) END ExistingBarcode,
                CASE WHEN cardinality(i.serial_numbers)>0 THEN u.internal_barcode
                     ELSE (SELECT barcode FROM variant_barcodes WHERE variant_id=i.variant_id AND is_active AND barcode_source='INTERNAL' ORDER BY variant_barcode_id LIMIT 1) END InternalBarcode,
                v.selling_price SellingPrice
            FROM stock_intake_items i JOIN product_variants v ON v.variant_id=i.variant_id
            LEFT JOIN product_units u ON u.variant_id=i.variant_id AND u.serial_number=ANY(i.serial_numbers)
            WHERE i.intake_id=@intakeId AND i.location_id=@locationId
            ORDER BY i.intake_item_id,u.product_unit_id
            """,
new
{
    intakeId,
    locationId
})).ToList();
        if (await StockItemLabels.Available(c))
        {
            var individual = await StockItemLabels.Read(c, locationId, null, intakeId);
            // Keep supplied manufacturer codes immediately available. Generated labels take
            // precedence only after the user explicitly prepares unique item labels.
            rows = rows.Where(row => row.UnitId.HasValue || !individual.Any(label => label.VariantId == row.VariantId))
                .Concat(individual).ToList();
        }
        else
        {
            foreach (var row in rows.Where(row => !row.UnitId.HasValue))
                row.InternalBarcode = null;
        }
        if (printAction is not null)
        {
            await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(@userId,'Barcodes',@printAction,'stock_intakes',@intakeId)",
new
{
    userId,
    printAction,
    intakeId
});
        }

        return rows;
    }

    public async Task<StockIntakeResult> PostAsync(StockIntakeRequest request, bool opening, int userId)
    {
        factory.RequireLocation(request.LocationId);
        request.LocationId ??= factory.Scope?.LocationId;
        var purchase = StockIntakeRules.Validate(request, opening);
        var kind = opening ? "OPENING_STOCK" : "RECEIPT";
        // Capture before resolving default variants so an identical retry has the same payload.
        var payload = JsonSerializer.Serialize(new
        {
            kind,
            request
        });
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        // A transaction saves the receipt and its stock changes together, or rolls back both.
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))", new
        {
            key = "StockFlow.Intake:" + request.RequestId
        }, t);
        var prior = await c.QuerySingleOrDefaultAsync<Retry>("SELECT intake_id IntakeId,purchase_id PurchaseId,kind Kind,created_by=@userId AND request_payload=CAST(@payload AS jsonb) Matches FROM stock_intakes WHERE request_id=@RequestId",
new
{
    request.RequestId,
    userId,
    payload
},
t);
        if (prior is not null)
        {
            if (!prior.Matches)
            {
                throw new ValidationException("This retry identifier belongs to a different stock receipt.");
            }

            await t.CommitAsync();
            return prior;
        }

        var id = await c.ExecuteScalarAsync<long>("INSERT INTO stock_intakes(kind,received_date,request_id,request_payload,created_by) VALUES(@kind,@ReceivedDate,@RequestId,CAST(@payload AS jsonb),@userId) RETURNING intake_id",
new
{
    kind,
    request.ReceivedDate,
    request.RequestId,
    payload,
    userId
},
t);
        long? purchaseId = null;
        if (!opening)
        {
            purchaseId = await PurchaseRepository.CreateInTransactionAsync(c, t, purchase, userId, factory.RequiresExplicitRetailVariant);
        }
        else
        {
            foreach (var productId in request.Items.Select(i => i.ProductId).Distinct().Order())
            {
                if (await c.ExecuteScalarAsync<int?>("SELECT product_id FROM products WHERE product_id=@productId AND is_active FOR UPDATE", new { productId }, t) is null)
                {
                    throw new ValidationException("Choose active products for opening stock.");
                }
            }

            foreach (var item in request.Items.OrderBy(i => i.VariantId))
            {
                var variant = await VariantRepository.ResolveAsync(c, t, item.ProductId, item.VariantId, requireExplicitVariant: factory.RequiresExplicitRetailVariant);
                PurchaseRules.ValidateSerials(item, variant.IsSerialTracked, true);
                var units = new List<long?>();
                if (variant.IsSerialTracked)
                {
                    foreach (var serial in item.SerialNumbers)
                    {
                        units.Add(await c.ExecuteScalarAsync<long>("INSERT INTO product_units(product_id,variant_id,serial_number,unit_status) VALUES(@ProductId,@VariantId,@serial,'IN_STOCK') RETURNING product_unit_id",
new
{
    item.ProductId,
    variant.VariantId,
    serial
},
t));
                    }
                }
                else
                {
                    units.Add(null);
                }

                foreach (var unit in units)
                {
                    await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,product_unit_id,transaction_type,quantity,reference_type,reference_id,remarks,created_by) VALUES(@ProductId,@VariantId,@unit,'ADJUSTMENT_IN',@quantity,'OPENING_STOCK',@id,@remarks,@userId)",
new
{
    item.ProductId,
    variant.VariantId,
    unit,
    quantity = variant.IsSerialTracked ? 1 : item.Quantity,
    id,
    remarks = "Opening stock: " + request.Notes,
    userId
},
t);
                }
            }
        }

        foreach (var item in request.Items)
        {
            var variant = await VariantRepository.ResolveAsync(c, t, item.ProductId, item.VariantId, requireExplicitVariant: factory.RequiresExplicitRetailVariant);
            await c.ExecuteAsync("""
                INSERT INTO stock_intake_items(intake_id,product_id,variant_id,product_name_snapshot,variant_name_snapshot,quantity,purchase_price,tax_percentage,serial_numbers)
                SELECT @id,@ProductId,@VariantId,p.product_name,v.variant_name,@Quantity,@PurchasePrice,@TaxPercentage,@serials
                FROM products p JOIN product_variants v ON v.product_id=p.product_id WHERE p.product_id=@ProductId AND v.variant_id=@VariantId
                """,
new
{
    id,
    item.ProductId,
    variant.VariantId,
    item.Quantity,
    item.PurchasePrice,
    item.TaxPercentage,
    serials = item.SerialNumbers.ToArray()
},
t);
        }

        await c.ExecuteAsync("UPDATE stock_intakes SET purchase_id=@purchaseId WHERE intake_id=@id; INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,reason) VALUES(@userId,'Stock inflow',@kind,'stock_intakes',@id,'Guided stock intake posted')",
new
{
    purchaseId,
    id,
    userId,
    kind
},
t);
        await t.CommitAsync();
        return new()
        {
            IntakeId = id,
            PurchaseId = purchaseId,
            Kind = kind
        };
    }

    private sealed class Retry : StockIntakeResult
    {
        public bool Matches
        {
            get; set;
        }
    }
}
