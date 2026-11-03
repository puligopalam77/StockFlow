using System.ComponentModel.DataAnnotations;
using Dapper;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class BarcodeLotRepository(DbConnectionFactory factory) : IBarcodeLotRepository
{
    public async Task<IReadOnlyList<BarcodeLotResponse>> ListAsync()
    {
        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        return (await c.QueryAsync<BarcodeLotResponse>("""
            SELECT i.intake_id LotId,'intake' Kind,i.intake_number LotNumber,i.received_date ReceivedDate,
              p.purchase_id PurchaseId,p.purchase_number PurchaseNumber,s.supplier_name SupplierName,
              count(*)::integer Items,sum(x.quantity)::bigint Quantity
            FROM stock_intakes i JOIN stock_intake_items x USING(tenant_id,location_id,intake_id)
            JOIN product_variants v ON v.tenant_id=x.tenant_id AND v.variant_id=x.variant_id
            LEFT JOIN purchases p ON p.tenant_id=i.tenant_id AND p.purchase_id=i.purchase_id
            LEFT JOIN suppliers s ON s.tenant_id=p.tenant_id AND s.supplier_id=p.supplier_id
            WHERE i.location_id=@locationId
            GROUP BY i.intake_id,i.intake_number,i.received_date,p.purchase_id,p.purchase_number,s.supplier_name
            UNION ALL
            SELECT r.receipt_id,'receipt',r.receipt_number,r.received_date,p.purchase_id,p.purchase_number,s.supplier_name,
              count(*)::integer,sum(x.quantity)::bigint
            FROM purchase_receipts r JOIN purchase_receipt_items x USING(tenant_id,location_id,receipt_id)
            JOIN purchase_items pi ON pi.tenant_id=x.tenant_id AND pi.purchase_item_id=x.purchase_item_id
            JOIN product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id
            JOIN purchases p ON p.tenant_id=r.tenant_id AND p.purchase_id=r.purchase_id
            LEFT JOIN suppliers s ON s.tenant_id=p.tenant_id AND s.supplier_id=p.supplier_id
            WHERE r.location_id=@locationId AND r.status='COMPLETED' AND x.quantity>0
              AND NOT EXISTS(SELECT 1 FROM stock_intakes i WHERE i.tenant_id=r.tenant_id AND i.purchase_id=r.purchase_id)
            GROUP BY r.receipt_id,r.receipt_number,r.received_date,p.purchase_id,p.purchase_number,s.supplier_name
            ORDER BY ReceivedDate DESC,LotId DESC
            """,new { locationId=factory.Scope!.LocationId })).ToList();
    }

    public async Task<IReadOnlyList<StockLabelResponse>?> ReceiptLabelsAsync(long receiptId,string? action,int userId)
    {
        factory.RequireLocation();
        await using var c=factory.CreateConnection();
        await c.OpenAsync();
        var locationId=factory.Scope!.LocationId;
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM purchase_receipts WHERE receipt_id=@receiptId AND location_id=@locationId AND status='COMPLETED')",new{receiptId,locationId})) return null;
        var available=await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.purchase_item_labels') IS NOT NULL");
        if(action=="GENERATE")
        {
            if(!available)throw new ValidationException("Apply manual migration 060 to generate barcode labels for purchase receipt lots.");
            await using var t=await c.BeginTransactionAsync();
            await c.ExecuteAsync("""
                SELECT pg_advisory_xact_lock(hashtextextended('receipt-labels:'||sf_tenant()::text||':'||CAST(@receiptId AS text),0));
                INSERT INTO purchase_item_labels(tenant_id,location_id,receipt_item_id,item_number)
                SELECT ri.tenant_id,ri.location_id,ri.receipt_item_id,n
                FROM purchase_receipt_items ri JOIN purchase_items pi USING(tenant_id,purchase_item_id)
                JOIN product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id,
                generate_series(1,ri.quantity) n
                WHERE ri.receipt_id=@receiptId AND ri.location_id=@locationId AND NOT v.is_serial_tracked
                ON CONFLICT(tenant_id,receipt_item_id,item_number) DO NOTHING
                """,new{receiptId,locationId},t);
            await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(@userId,'Barcodes','GENERATE','purchase_receipts',@receiptId)",new{userId,receiptId},t);
            await t.CommitAsync();
        }
        var rows=(await c.QueryAsync<StockLabelResponse>("""
            SELECT v.variant_id VariantId,u.product_unit_id UnitId,p.product_name||' / '||v.variant_name ProductName,v.sku Sku,
              u.serial_number SerialNumber,CASE WHEN v.is_serial_tracked THEN 1 ELSE ri.quantity END Quantity,
              CASE WHEN v.is_serial_tracked THEN COALESCE(u.manufacturer_barcode,u.barcode,u.serial_number)
                ELSE (SELECT barcode FROM variant_barcodes b WHERE b.variant_id=v.variant_id AND b.is_active AND b.barcode_source='MANUFACTURER' ORDER BY b.is_primary DESC,b.variant_barcode_id LIMIT 1) END ExistingBarcode,
              u.internal_barcode InternalBarcode,v.selling_price SellingPrice
            FROM purchase_receipt_items ri JOIN purchase_items pi USING(tenant_id,purchase_item_id)
            JOIN product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id
            JOIN products p ON p.tenant_id=v.tenant_id AND p.product_id=v.product_id
            LEFT JOIN product_units u ON u.tenant_id=ri.tenant_id AND u.variant_id=v.variant_id AND u.receipt_id=ri.receipt_id AND u.serial_number=ANY(ri.serial_numbers)
            WHERE ri.receipt_id=@receiptId AND ri.location_id=@locationId AND ri.quantity>0
            ORDER BY ri.receipt_item_id,u.product_unit_id
            """,new{receiptId,locationId})).ToList();
        if(available)
        {
            var labels=(await c.QueryAsync<StockLabelResponse>("""
                SELECT l.label_id LabelId,l.item_number ItemNumber,v.variant_id VariantId,p.product_name||' / '||v.variant_name ProductName,
                  v.sku Sku,1 Quantity,l.barcode InternalBarcode,v.selling_price SellingPrice,
                  (SELECT barcode FROM variant_barcodes b WHERE b.variant_id=v.variant_id AND b.is_active AND b.barcode_source='MANUFACTURER' ORDER BY b.is_primary DESC,b.variant_barcode_id LIMIT 1) ExistingBarcode
                FROM purchase_item_labels l JOIN purchase_receipt_items ri USING(tenant_id,location_id,receipt_item_id)
                JOIN purchase_items pi ON pi.tenant_id=ri.tenant_id AND pi.purchase_item_id=ri.purchase_item_id
                JOIN product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id
                JOIN products p ON p.tenant_id=v.tenant_id AND p.product_id=v.product_id
                WHERE ri.receipt_id=@receiptId AND l.location_id=@locationId ORDER BY l.label_id
                """,new{receiptId,locationId})).ToList();
            rows=rows.Where(r=>r.UnitId.HasValue||!labels.Any(l=>l.VariantId==r.VariantId)).Concat(labels).ToList();
        }
        if(action is "PRINT" or "REPRINT")
            await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(@userId,'Barcodes',@action,'purchase_receipts',@receiptId)",new{userId,action,receiptId});
        return rows;
    }
}
