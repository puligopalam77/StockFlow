using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.Infrastructure.Repositories;

internal static class StockItemLabels
{
    internal static Task<bool> Available(NpgsqlConnection c) => c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.stock_item_labels') IS NOT NULL");
    internal static Task<bool> SupportsExistingStock(NpgsqlConnection c) => c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='stock_item_labels' AND column_name='variant_id')");

    internal static async Task Generate(NpgsqlConnection c, long locationId, long? variantId, long? intakeId, int userId)
    {
        if (!await Available(c)) throw new ValidationException("Apply manual migration 054 to create unique stock-item labels.");
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('stock-labels:'||sf_tenant()::text||':'||CAST(@locationId AS text),0))", new { locationId }, t);
        await c.ExecuteAsync("""
            INSERT INTO stock_item_labels(tenant_id,location_id,intake_item_id,item_number)
            SELECT i.tenant_id,i.location_id,i.intake_item_id,n
            FROM stock_intake_items i JOIN product_variants v USING(tenant_id,variant_id), generate_series(1,i.quantity) n
            WHERE i.location_id=@locationId AND NOT v.is_serial_tracked AND cardinality(i.serial_numbers)=0
              AND (CAST(@variantId AS bigint) IS NULL OR i.variant_id=@variantId)
              AND (CAST(@intakeId AS bigint) IS NULL OR i.intake_id=@intakeId)
            ORDER BY i.intake_item_id,n ON CONFLICT(tenant_id,intake_item_id,item_number) DO NOTHING;
            INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,reason)
            VALUES(@userId,'Barcodes','GENERATE','stock_item_labels',COALESCE(@variantId,@intakeId),'Prepare persistent labels for received quantity stock');
            """, new { locationId, variantId, intakeId, userId }, t);
        if (variantId.HasValue && !intakeId.HasValue)
        {
            // Older purchases/adjustments can have stock without a guided receiving manifest.
            // Allocate stable slots only for the stock not covered by receipt quantities.
            var uncovered = await c.ExecuteScalarAsync<long>("""
                SELECT GREATEST(0,s.current_stock-COALESCE((SELECT SUM(i.quantity) FROM stock_intake_items i
                    WHERE i.variant_id=v.variant_id AND i.location_id=@locationId),0))
                FROM product_variants v JOIN vw_variant_current_stock s USING(variant_id)
                WHERE v.variant_id=@variantId AND NOT v.is_serial_tracked
                """, new { locationId, variantId }, t);
            if (uncovered > 0)
            {
                if (!await SupportsExistingStock(c)) throw new ValidationException("Apply manual migration 059 to generate labels for existing stock without a receiving record.");
                if (uncovered > 10000) throw new ValidationException("Use receiving records to prepare labels for stock quantities above 10,000.");
                await c.ExecuteAsync("""
                    INSERT INTO stock_item_labels(tenant_id,location_id,variant_id,item_number)
                    SELECT v.tenant_id,@locationId,v.variant_id,n FROM product_variants v,generate_series(1,CAST(@uncovered AS integer)) n
                    WHERE v.variant_id=@variantId AND NOT v.is_serial_tracked
                    ON CONFLICT DO NOTHING
                    """, new { locationId, variantId, uncovered }, t);
            }
        }
        await t.CommitAsync();
    }

    internal static async Task<IReadOnlyList<StockLabelResponse>> Read(NpgsqlConnection c, long locationId, long? variantId, long? intakeId)
    {
        if (!await Available(c)) throw new ValidationException("Apply manual migration 054 to use unique stock-item labels.");
        var rows = (await c.QueryAsync<StockLabelResponse>("""
            SELECT l.label_id LabelId,i.intake_id IntakeId,l.item_number ItemNumber,i.variant_id VariantId,
              i.product_name_snapshot || CASE WHEN i.variant_name_snapshot='DEFAULT' THEN '' ELSE ' / '||i.variant_name_snapshot END ProductName,
              v.sku Sku,1 Quantity,l.barcode InternalBarcode,v.selling_price SellingPrice,
              (SELECT barcode FROM variant_barcodes b WHERE b.variant_id=v.variant_id AND b.is_active AND b.barcode_source='MANUFACTURER' ORDER BY b.is_primary DESC,b.variant_barcode_id LIMIT 1) ExistingBarcode
            FROM stock_item_labels l JOIN stock_intake_items i USING(tenant_id,location_id,intake_item_id)
            JOIN product_variants v ON v.tenant_id=i.tenant_id AND v.variant_id=i.variant_id
            WHERE l.location_id=@locationId
              AND (CAST(@variantId AS bigint) IS NULL OR i.variant_id=@variantId)
              AND (CAST(@intakeId AS bigint) IS NULL OR i.intake_id=@intakeId)
            ORDER BY i.intake_id,i.intake_item_id,l.item_number
            """, new { locationId, variantId, intakeId })).ToList();
        if (!intakeId.HasValue && await SupportsExistingStock(c))
            rows.AddRange(await c.QueryAsync<StockLabelResponse>("""
                SELECT l.label_id LabelId,l.item_number ItemNumber,v.variant_id VariantId,
                    p.product_name || CASE WHEN v.variant_name='DEFAULT' THEN '' ELSE ' / '||v.variant_name END ProductName,
                    v.sku Sku,1 Quantity,l.barcode InternalBarcode,v.selling_price SellingPrice
                FROM stock_item_labels l JOIN product_variants v USING(tenant_id,variant_id)
                JOIN products p USING(tenant_id,product_id)
                WHERE l.location_id=@locationId AND l.intake_item_id IS NULL
                    AND (CAST(@variantId AS bigint) IS NULL OR v.variant_id=@variantId)
                ORDER BY l.label_id
                """, new { locationId, variantId }));
        return rows;
    }
}
