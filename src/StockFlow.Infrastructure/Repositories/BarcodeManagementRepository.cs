using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class BarcodeManagementRepository(DbConnectionFactory factory,IBarcodeLotRepository lots) : IBarcodeManagementRepository
{
    private static void Kind(string kind) { if(kind is not "intake" and not "receipt")throw new ValidationException("Choose a valid received lot."); }
    private static string PieceSql(string kind)
    {
        var intake=kind=="intake";
        var source=intake?"stock_intake_items":"purchase_receipt_items";
        var sourceId=intake?"intake_item_id":"receipt_item_id";
        var lotId=intake?"intake_id":"receipt_id";
        var labels=intake?"stock_item_labels":"purchase_item_labels";
        var variant=intake?"i.variant_id":"pi.variant_id";
        var join=intake?"":"JOIN purchase_items pi ON pi.tenant_id=i.tenant_id AND pi.purchase_item_id=i.purchase_item_id";
        return $"""
            SELECT i.{lotId} LotId,i.{sourceId} SourceItemId,n ItemNumber,v.variant_id VariantId,u.product_unit_id UnitId,
              CASE WHEN v.is_serial_tracked THEN 'u:'||COALESCE(u.product_unit_id::text,'missing:'||i.{sourceId}::text)
                ELSE i.{sourceId}::text||':'||n::text END Key,
              p.product_name||CASE WHEN v.variant_name='DEFAULT' THEN '' ELSE ' / '||v.variant_name END ProductName,v.sku Sku,v.selling_price SellingPrice,
              to_jsonb(p)->>'parent_product_code' ProductCode,v.variant_name VariantName,v.is_serial_tracked IsSerialTracked,
              u.serial_number SerialNumber,COALESCE(to_jsonb(u)->>'serial_source','LEGACY_UNKNOWN') SerialSource,
              CASE WHEN v.is_serial_tracked THEN CASE WHEN u.internal_barcode IS NOT NULL THEN 'INTERNAL_UNIT'
                WHEN u.manufacturer_barcode IS NOT NULL THEN 'MANUFACTURER_UNIT' WHEN u.barcode IS NOT NULL THEN 'SAVED_UNIT'
                ELSE 'UNIT_IDENTIFIER' END ELSE CASE WHEN l.barcode IS NOT NULL THEN 'SAVED_ITEM'
                ELSE (SELECT b.barcode_source FROM variant_barcodes b WHERE b.tenant_id=v.tenant_id AND b.variant_id=v.variant_id AND b.is_active
                  ORDER BY (b.barcode_source='MANUFACTURER') DESC,b.is_primary DESC,b.variant_barcode_id LIMIT 1) END END BarcodeSource,
              NOT v.is_serial_tracked CanGenerate,
              CASE WHEN v.is_serial_tracked THEN COALESCE(u.internal_barcode,u.manufacturer_barcode,u.barcode,u.serial_number)
                ELSE COALESCE(l.barcode,(SELECT b.barcode FROM variant_barcodes b WHERE b.tenant_id=v.tenant_id AND b.variant_id=v.variant_id
                  AND b.is_active ORDER BY (b.barcode_source='MANUFACTURER') DESC,b.is_primary DESC,b.variant_barcode_id LIMIT 1)) END Barcode
            FROM {source} i {join}
            JOIN product_variants v ON v.tenant_id=i.tenant_id AND v.variant_id={variant}
            JOIN products p ON p.tenant_id=v.tenant_id AND p.product_id=v.product_id
            CROSS JOIN LATERAL generate_series(1,CASE WHEN v.is_serial_tracked THEN 1 ELSE i.quantity END) n
            LEFT JOIN product_units u ON v.is_serial_tracked AND u.tenant_id=i.tenant_id AND u.variant_id=v.variant_id AND u.serial_number=ANY(i.serial_numbers)
            LEFT JOIN {labels} l ON NOT v.is_serial_tracked AND l.tenant_id=i.tenant_id AND l.location_id=i.location_id AND l.{sourceId}=i.{sourceId} AND l.item_number=n
            WHERE i.location_id=@locationId AND (CAST(@id AS bigint) IS NULL OR i.{lotId}=@id) AND i.quantity>0
            """;
    }
    private async Task<bool> HistoryAvailable(NpgsqlConnection c)=>await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.barcode_print_requests') IS NOT NULL");
    public async Task<IReadOnlyList<BarcodeLotResponse>> ListAsync()
    {
        factory.RequireLocation();
        var result=await lots.ListAsync();
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.purchase_item_labels') IS NOT NULL"))return result;
        foreach(var kind in new[]{"intake","receipt"})
        {
            var counts=(await c.QueryAsync<(long LotId,long WithBarcode)>("SELECT LotId,count(*) FILTER(WHERE Barcode IS NOT NULL) WithBarcode FROM ("+PieceSql(kind)+") x GROUP BY LotId",new{locationId=factory.Scope!.LocationId,id=(long?)null})).ToDictionary(x=>x.LotId,x=>x.WithBarcode);
            foreach(var lot in result.Where(l=>l.Kind==kind))lot.WithBarcode=counts.GetValueOrDefault(lot.LotId);
        }
        return result;
    }
    public async Task<BarcodeWorkspace?> ReadAsync(string kind,long id)
    {
        Kind(kind);factory.RequireLocation();
        var lot=(await lots.ListAsync()).SingleOrDefault(l=>l.Kind==kind&&l.LotId==id);
        if(lot is null)return null;
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.purchase_item_labels') IS NOT NULL"))throw new ValidationException("Apply manual migration 060 before managing received lot barcodes.");
        var rows=(await c.QueryAsync<BarcodePiece>(PieceSql(kind)+" ORDER BY SourceItemId,ItemNumber,UnitId",new{locationId=factory.Scope!.LocationId,id})).ToList();
        var historyAvailable=await HistoryAvailable(c);
        var history=historyAvailable?(await c.QueryAsync<BarcodePrintHistory>("""
            SELECT h.request_id RequestId,h.requested_at RequestedAt,h.requested_by_name RequestedBy,h.action Action,h.format Format,
              x->>'key' RowKey,x->>'barcode' Barcode,x->>'productName' ProductName,(x->>'copies')::integer Copies,
              (to_jsonb(h)->'sheet_template')::text TemplateJson,COALESCE((to_jsonb(h)->>'small_label_scan_tested')::boolean,false) SmallLabelScanTested
            FROM barcode_print_requests h CROSS JOIN LATERAL jsonb_array_elements(h.selection) x
            WHERE h.location_id=@locationId AND h.lot_kind=@kind AND h.lot_id=@id
            ORDER BY h.requested_at DESC,h.request_id
            """,new{locationId=factory.Scope!.LocationId,kind,id})).ToList():[];
        foreach(var entry in history)if(entry.TemplateJson is not null)entry.SheetTemplate=JsonSerializer.Deserialize<BarcodeSheetTemplate>(entry.TemplateJson);
        foreach(var row in rows)
        {
            var last=history.FirstOrDefault(h=>h.RowKey==row.Key&&h.Barcode==row.Barcode);
            row.LastRequestedAt=last?.RequestedAt;row.LastRequestedBy=last?.RequestedBy;
        }
        lot.WithBarcode=rows.Count(r=>r.Barcode is not null);
        return new(){Lot=lot,Rows=rows,History=history,HistoryAvailable=historyAvailable};
    }
    public async Task<BarcodeWorkspace?> PrepareAsync(string kind,long id,string[] keys,int userId)
    {
        if(keys.Length is <1 or >500||keys.Distinct().Count()!=keys.Length||keys.Any(k=>string.IsNullOrWhiteSpace(k)||k.Length>100))throw new ValidationException("Select 1 to 500 distinct items.");
        var workspace=await ReadAsync(kind,id);if(workspace is null)return null;
        var selected=keys.Select(key=>workspace.Rows.SingleOrDefault(r=>r.Key==key)??throw new ValidationException("A selected item does not belong to this lot.")).ToArray();
        var missing=selected.Where(r=>string.IsNullOrWhiteSpace(r.Barcode)).ToArray();
        if(missing.Any(r=>!r.CanGenerate))throw new ValidationException("A selected serialized unit is unavailable. Check its stock receipt.");
        await using var c=factory.CreateConnection();await c.OpenAsync();
        await using var t=await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('barcode-prepare:'||sf_tenant()::text||':'||CAST(@locationId AS text)||':'||@kind||':'||CAST(@id AS text),0))",new{locationId=factory.Scope!.LocationId,kind,id},t);
        await factory.LockBusinessProfileAsync(c,t);
        foreach(var row in missing.DistinctBy(r=>r.VariantId).OrderBy(r=>r.VariantId))
        {
            // Existing per-piece labels remain valid. New quantity stock uses one saved SKU barcode.
            await c.ExecuteAsync("SELECT variant_id FROM product_variants WHERE variant_id=@VariantId FOR UPDATE",row,t);
            await c.ExecuteAsync("""
                INSERT INTO variant_barcodes(variant_id,barcode,barcode_source)
                SELECT @VariantId,'SFV'||lpad(CAST(@VariantId AS text),greatest(8,length(CAST(@VariantId AS text))),'0'),'INTERNAL'
                WHERE NOT EXISTS(SELECT 1 FROM variant_barcodes WHERE variant_id=@VariantId AND is_active)
                """,row,t);
        }
        if(missing.Length>0)await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,reason) VALUES(@userId,'Barcodes','GENERATE',@table,@id,'Prepare only selected missing item labels')",new{userId,table=kind=="intake"?"stock_intakes":"purchase_receipts",id},t);
        await t.CommitAsync();
        return await ReadAsync(kind,id);
    }
    public async Task<BarcodeWorkspace?> PrintAsync(string kind,long id,BarcodePrintRequest request,int userId)
    {
        if(request.RequestId==Guid.Empty||request.Items.Length is <1 or >500||request.Items.Select(i=>i.Key).Distinct().Count()!=request.Items.Length||request.Items.Any(i=>i.Copies is <1 or >500)||request.Items.Sum(i=>(long)i.Copies)>500||request.Action is not "PRINT" and not "REPRINT"||request.Format is not "sheet" and not "thermal" and not "thermal50x30" and not "thermal40x25" and not "thermal30x20")throw new ValidationException("Choose 1 to 500 labels, valid copies and a paper format.");
        if(request.Format=="thermal30x20"&&!request.SmallLabelScanTested)throw new ValidationException("Confirm a successful scan test before using 30 x 20 mm labels.");
        if(request.SheetTemplate is not null&&(request.Format!="sheet"||!request.SheetTemplate.Valid))throw new ValidationException("The A4 template dimensions, rows, columns, margins and gaps must fit a 210 x 297 mm sheet.");
        var workspace=await ReadAsync(kind,id);if(workspace is null)return null;
        if(!workspace.HistoryAvailable)throw new ValidationException("Apply manual migration 061 to save print requests and enable reprinting history.");
        var selected=request.Items.Select(item=>
        {
            var row=workspace.Rows.SingleOrDefault(r=>r.Key==item.Key);
            if(row is null||string.IsNullOrWhiteSpace(row.Barcode)||row.Barcode!=item.Barcode)throw new ValidationException("A selected barcode changed or does not belong to this received lot. Reload before printing.");
            if(request.Action=="REPRINT"&&row.LastRequestedAt is null)throw new ValidationException("Reprint is available only for labels with a saved print request.");
            return new{key=row.Key,barcode=row.Barcode,copies=item.Copies,productName=row.ProductName};
        }).OrderBy(i=>i.key).ToArray();
        var selection=JsonSerializer.Serialize(selected);
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
        var sized=await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='barcode_print_requests' AND column_name='sheet_template')",transaction:t);
        if(!sized&&(request.Format is not "sheet" and not "thermal"||request.SheetTemplate is not null||request.SmallLabelScanTested))throw new ValidationException("Apply manual migration 062 before using the new thermal sizes and A4 templates.");
        var template=request.SheetTemplate is null?null:JsonSerializer.Serialize(request.SheetTemplate);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(CAST(@requestId AS text),0))",new{request.RequestId},t);
        var prior=await c.QuerySingleOrDefaultAsync<PrintRetry>("SELECT location_id=@locationId AND lot_kind=@kind AND lot_id=@id AND action=@action AND format=@format AND requested_by=@userId AND selection=CAST(@selection AS jsonb) AND NULLIF(to_jsonb(barcode_print_requests)->'sheet_template','null'::jsonb) IS NOT DISTINCT FROM CAST(@template AS jsonb) AND COALESCE((to_jsonb(barcode_print_requests)->>'small_label_scan_tested')::boolean,false)=@smallLabelScanTested Matches FROM barcode_print_requests WHERE request_id=@requestId",new{locationId=factory.Scope!.LocationId,kind,id,request.Action,request.Format,userId,selection,request.RequestId,template,request.SmallLabelScanTested},t);
        if(prior is not null&&!prior.Matches)throw new ValidationException("This print request ID belongs to a different selection. Reload before retrying.");
        if(prior is null)
        {
            if(request.Action=="PRINT"&&request.Items.Any(item=>workspace.Rows.Any(r=>r.Key==item.Key&&r.LastRequestedAt is not null)))throw new ValidationException("These labels already have a print request. Use Reprint Labels to print them again.");
            var inserted=await c.ExecuteAsync($"""
                INSERT INTO barcode_print_requests(request_id,lot_kind,lot_id,action,format,requested_by,requested_by_name,selection{(sized?",sheet_template,small_label_scan_tested":"")})
                SELECT @requestId,@kind,@id,@action,@format,@userId,COALESCE(NULLIF(full_name,''),username),CAST(@selection AS jsonb){(sized?",CAST(@template AS jsonb),@smallLabelScanTested":"")}
                FROM users WHERE tenant_id=sf_tenant() AND user_id=@userId
                """,new{request.RequestId,kind,id,request.Action,request.Format,userId,selection,template,request.SmallLabelScanTested},t);
            if(inserted!=1)throw new ValidationException("Unable to record the print request for the signed-in user.");
        }
        await t.CommitAsync();return await ReadAsync(kind,id);
    }
    private sealed class PrintRetry { public bool Matches {get;set;} }
}
