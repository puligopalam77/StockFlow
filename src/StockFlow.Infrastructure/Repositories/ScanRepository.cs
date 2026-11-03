using System.ComponentModel.DataAnnotations;
using Dapper;
using StockFlow.Application.DTOs.Scanning;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class ScanRepository : IScanRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public ScanRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<ScanLookupResponse?> FindByCodeAsync(string code)
    {
        await using var connection = factory.CreateConnection();
        // A single statement gives a consistent lookup/stock snapshot. No inventory writes.
        var sql = """
            WITH matches AS (
                SELECT p.product_id, pu.variant_id, pu.product_unit_id,
                    CASE WHEN LOWER(BTRIM(pu.serial_number)) = LOWER(@Code)
                        THEN 'SERIAL_NUMBER' ELSE 'UNIT_BARCODE' END AS match_type,
                    pu.serial_number, COALESCE(pu.internal_barcode,pu.manufacturer_barcode,pu.barcode) AS unit_barcode, pu.unit_status, pu.sale_id
                FROM product_units pu JOIN products p ON p.product_id = pu.product_id
                WHERE LOWER(BTRIM(pu.serial_number)) = LOWER(@Code) OR LOWER(BTRIM(pu.barcode)) = LOWER(@Code)
                    OR LOWER(BTRIM(pu.internal_barcode))=LOWER(@Code) OR LOWER(BTRIM(pu.manufacturer_barcode))=LOWER(@Code)
                UNION ALL
                SELECT v.product_id, v.variant_id, NULL::BIGINT, 'PRODUCT_BARCODE', NULL, NULL, NULL, NULL::BIGINT
                FROM variant_barcodes vb JOIN product_variants v USING(variant_id)
                WHERE vb.is_active AND LOWER(BTRIM(vb.barcode)) = LOWER(@Code)
                UNION ALL
                SELECT v.product_id,v.variant_id,NULL::BIGINT,'PRODUCT_SKU',NULL,NULL,NULL,NULL::BIGINT
                FROM product_variants v WHERE LOWER(BTRIM(v.sku))=LOWER(@Code)
            )
            SELECT DISTINCT ON(m.variant_id,m.product_unit_id) m.match_type AS MatchType, p.product_id AS ProductId, v.variant_id AS VariantId, m.product_unit_id AS ProductUnitId, m.sale_id AS SaleId,
                p.product_name || ' / ' || v.variant_name AS ProductName, b.brand_name AS BrandName, c.category_name AS CategoryName,
                to_jsonb(p)->>'parent_product_code' AS ProductCode, p.model AS Model,v.sku AS Sku,v.attributes::text AS AttributesJson,p.unit_of_measure AS UnitOfMeasure,
                (SELECT barcode FROM variant_barcodes WHERE variant_id=v.variant_id AND is_active ORDER BY is_primary DESC,variant_barcode_id LIMIT 1) AS ProductBarcode, m.serial_number AS SerialNumber,
                COALESCE((SELECT to_jsonb(u)->>'serial_source' FROM product_units u WHERE u.product_unit_id=m.product_unit_id),'LEGACY_UNKNOWN') AS SerialSource, m.unit_barcode AS UnitBarcode, m.unit_status AS UnitStatus, (p.is_active AND v.is_active) AS IsActive,
                v.is_serial_tracked AS IsSerialTracked, v.selling_price AS SellingPrice, v.mrp AS Mrp, v.tax_percentage AS TaxPercentage,
                COALESCE((SELECT SUM(CASE
                    WHEN transaction_type IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN quantity::BIGINT
                    WHEN transaction_type IN ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -quantity::BIGINT
                    WHEN transaction_type = 'ADJUSTMENT' THEN quantity::BIGINT ELSE 0 END)
                    FROM inventory_transactions WHERE variant_id = v.variant_id),0)::BIGINT AS CurrentStock
            FROM matches m JOIN products p ON p.product_id = m.product_id JOIN product_variants v ON v.variant_id=m.variant_id
            JOIN categories c ON c.category_id = p.category_id
            LEFT JOIN brands b ON b.brand_id = p.brand_id
            WHERE COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')
            ORDER BY m.variant_id,m.product_unit_id,m.match_type LIMIT 2;
            """;
        if (await StockItemLabels.Available(connection))
            sql = sql.Replace("WITH matches AS (", """
                WITH matches AS (
                    SELECT i.product_id,i.variant_id,NULL::bigint AS product_unit_id,'PRODUCT_BARCODE' AS match_type,
                        NULL::text AS serial_number,l.barcode AS unit_barcode,NULL::varchar AS unit_status,NULL::bigint AS sale_id
                    FROM stock_item_labels l JOIN stock_intake_items i USING(tenant_id,location_id,intake_item_id)
                    WHERE lower(l.barcode)=lower(@Code)
                    UNION ALL
                """);
        if (await StockItemLabels.SupportsExistingStock(connection))
            sql = sql.Replace("WITH matches AS (", """
                WITH matches AS (
                    SELECT v.product_id,v.variant_id,NULL::bigint AS product_unit_id,'PRODUCT_BARCODE' AS match_type,
                        NULL::text AS serial_number,l.barcode AS unit_barcode,NULL::varchar AS unit_status,NULL::bigint AS sale_id
                    FROM stock_item_labels l JOIN product_variants v USING(tenant_id,variant_id)
                    WHERE l.intake_item_id IS NULL AND lower(l.barcode)=lower(@Code)
                    UNION ALL
                """);
        if (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.purchase_item_labels') IS NOT NULL"))
            sql = sql.Replace("WITH matches AS (", """
                WITH matches AS (
                    SELECT v.product_id,v.variant_id,NULL::bigint AS product_unit_id,'PRODUCT_BARCODE' AS match_type,
                        NULL::text AS serial_number,l.barcode AS unit_barcode,NULL::varchar AS unit_status,NULL::bigint AS sale_id
                    FROM purchase_item_labels l JOIN purchase_receipt_items ri USING(tenant_id,location_id,receipt_item_id)
                    JOIN purchase_receipts r ON r.tenant_id=ri.tenant_id AND r.receipt_id=ri.receipt_id AND r.status='COMPLETED'
                    JOIN purchase_items pi ON pi.tenant_id=ri.tenant_id AND pi.purchase_item_id=ri.purchase_item_id
                    JOIN product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id
                    WHERE lower(l.barcode)=lower(@Code)
                    UNION ALL
                """);
        if (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.catalog_sku_corrections') IS NOT NULL"))
            sql = sql.Replace("WITH matches AS (", """
                WITH matches AS (
                    SELECT a.product_id,a.variant_id,NULL::bigint AS product_unit_id,'PRODUCT_SKU' AS match_type,
                        NULL::text AS serial_number,NULL::text AS unit_barcode,NULL::varchar AS unit_status,NULL::bigint AS sale_id
                    FROM public.catalog_sku_corrections a
                    WHERE a.tenant_id=public.sf_tenant() AND a.profile=public.sf_visible_business_profile()
                        AND lower(btrim(a.old_sku))=lower(@Code)
                    UNION ALL
                """);
        var matches = (await connection.QueryAsync<ScanLookupResponse>(sql, new
        {
            Code = code
        })).ToList();
        if (matches.Count > 1)
        {
            throw new ValidationException("This code matches multiple records. Ask an administrator to resolve the barcode/serial conflict.");
        }

        return matches.SingleOrDefault();
    }
}
