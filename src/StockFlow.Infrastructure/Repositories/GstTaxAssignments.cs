using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Infrastructure.Repositories;

public sealed partial class GstRepository
{
    private sealed class AssignmentRow
    {
        public int Row{get;set;} public int ProductId{get;set;}public long? VariantId{get;set;}public string ProductName{get;set;}="";public string Status{get;set;}="";public string Message{get;set;}="";public string? BeforeValue{get;set;}public string? ProposedValue{get;set;}public string Fingerprint{get;set;}="";
        public GstOperationRow Model()=>new(Row,ProductId,VariantId,ProductName,Status,Message,BeforeValue,ProposedValue);
    }
    public async Task<GstOperation> PreviewAssignmentAsync(GstAssignment request)
    {
        if(request.RequestId==Guid.Empty||request.Targets.Count>50000||request.Import?.Count>50000||request.AllMatching&&!request.ConfirmAllMatching||request.Reason?.Length>1000)throw new ValidationException("Confirm the selection scope, use a valid request ID and at most 50,000 rows.");
        CatalogQuery(request.Filter);await using var c=factory.CreateConnection();await CatalogPermission(c,"EDIT");
        await RequireAsync(c,request.Import is not null?"IMPORT_TAX":request.AllMatching||request.Targets.Count>1?"BULK_TAX":"EDIT");
        if(request.OverrideExisting){await RequireAsync(c,"OVERRIDE_TAX");if(GstRules.Clean(request.Reason)?.Length<3||GstRules.Clean(request.Reason) is null)throw new ValidationException("An authorized override requires a reason.");}
        await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,tx);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        var previous=await c.ExecuteScalarAsync<string?>("SELECT request::text FROM gst_tax_operations WHERE operation_id=@id",new{id=request.RequestId},tx);
        if(previous is not null){if(!JsonEqual(previous,CatalogSerialize(request)))throw new ValidationException("This operation ID already belongs to a different preview.");await tx.CommitAsync();return await OperationAsync(request.RequestId);}
        await c.ExecuteAsync("INSERT INTO gst_tax_operations(operation_id,request) VALUES(@id,CAST(@body AS jsonb))",new{id=request.RequestId,body=CatalogSerialize(request)},tx);
        var query=CatalogParameters(request.Filter);
        var wanted=request.Import is not null?request.Import.Select(r=>new GstTarget(r.ProductId,r.VariantId)).ToList():request.Targets;
        if(!request.AllMatching&&wanted.Count==0)throw new ValidationException("Select at least one product or variant.");
        query.Add("ids",wanted.Select(t=>t.ProductId).Distinct().ToArray());
        var where=request.AllMatching?"":" WHERE \"ProductId\"=ANY(@ids)";
        var found=new List<GstCatalogRow>();
        foreach(var variants in request.Import is null?new[]{request.Filter.Variants}:wanted.Select(t=>t.VariantId is not null).Distinct())
        {
            query.Add("Variants",variants);
            var subset=where;
            if(request.Import is not null)
            {
                var requested=wanted.Where(t=>(t.VariantId is not null)==variants).ToArray();
                query.Add("ids",requested.Select(t=>t.ProductId).Distinct().ToArray());
                if(variants){query.Add("variantIds",requested.Select(t=>t.VariantId!.Value).ToArray());subset+=" AND \"VariantId\"=ANY(@variantIds)";}
            }
            found.AddRange(await c.QueryAsync<GstCatalogRow>(CatalogSql+"SELECT * FROM filtered"+subset+" ORDER BY \"ProductId\",\"VariantId\" LIMIT 50001",query,tx));
        }
        var catalog=found.ToArray();
        if(catalog.Length>50000)throw new ValidationException("Select at most 50,000 rows per operation.");
        var targets=request.AllMatching?catalog.Select(r=>new GstTarget(r.ProductId,r.VariantId)).ToArray():wanted.ToArray();
        var duplicate=targets.GroupBy(t=>t).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var records=catalog.ToDictionary(r=>new GstTarget(r.ProductId,r.VariantId));
        var ruleIds=request.Import is null?new[]{request.RuleId}:request.Import.Select(r=>r.RuleId).Distinct().ToArray();
        var rules=(await c.QueryAsync<RuleRecord>(RuleSelect+" WHERE rule_id=ANY(@ids)",new{ids=ruleIds},tx)).Select(r=>r.Model()).ToDictionary(r=>r.RuleId);
        var configs=(await c.QueryAsync<GstTaxConfiguration>(TaxSelect+" WHERE product_id=ANY(@ids)",new{ids=targets.Select(t=>t.ProductId).Distinct().ToArray()},tx)).ToLookup(t=>t.ProductId);
        var today=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE",transaction:tx);var store=await c.ExecuteScalarAsync<long>("SELECT sf_store()",transaction:tx);
        var staged=new List<AssignmentRow>();
        for(var i=0;i<targets.Length;i++)
        {
            var target=targets[i];records.TryGetValue(target,out var product);var import=request.Import?.ElementAt(i);var ruleId=import?.RuleId??request.RuleId;rules.TryGetValue(ruleId,out var rule);
            var row=new AssignmentRow{Row=import?.Row>0?import.Row:i+1,ProductId=target.ProductId,VariantId=target.VariantId,ProductName=product?.ProductName??"Unavailable product reference",Fingerprint=product?.Fingerprint??"",Status="READY",Message="Reviewed rule will be assigned. Existing variant overrides are retained."};
            staged.Add(row);var desired=rule is null?null:JsonSerializer.Deserialize<GstTaxConfiguration>(CatalogSerialize(rule.Tax),CatalogJson)!;
            if(desired is not null){desired.ProductId=target.ProductId;desired.VariantId=target.VariantId;desired.StoreId=store;desired.RuleId=ruleId;desired.EffectiveFrom=import?.EffectiveFrom??request.EffectiveFrom;desired.EffectiveTo=import?.EffectiveTo??request.EffectiveTo;desired.ChangeReason=request.Reason;desired.TaxConfigId=0;desired.CreatedAt=null;desired.CreatedBy=null;row.ProposedValue=CatalogSerialize(desired);}
            if(desired is not null&&import is not null)
            {
                if(import.EffectiveFrom==default)desired.EffectiveFrom=rule!.Tax.EffectiveFrom>today?rule.Tax.EffectiveFrom:today;
                desired.EffectiveTo=import.EffectiveTo??rule!.Tax.EffectiveTo;
                row.ProposedValue=CatalogSerialize(desired);
            }
            var relevant=configs[target.ProductId].Where(t=>t.VariantId==target.VariantId).ToArray();row.BeforeValue=CatalogSerialize(relevant);
            string? invalid=null;
            if(product is null)invalid="Product or variant is not an active member of the selected shop/profile/filter. It will not be created.";
            else if(duplicate.Contains(target))invalid="Duplicate or conflicting product/variant rows; remove duplicates before retrying.";
            else if(import?.Error is not null)invalid=import.Error;
            else if(rule is null||rule.ReviewStatus!="REVIEWED"||rule.Status!="ACTIVE")invalid="Select an active reviewed rule in this shop.";
            else if(import is not null&&(!SpreadsheetIdentifier(import.ProductCode,product.ProductCode)||!SpreadsheetIdentifier(import.ProductSku,product.ProductSku)||!SpreadsheetIdentifier(import.VariantSku,product.Sku)||import.HsnSac is not null&&import.HsnSac!=desired!.HsnSac||import.Treatment is not null&&import.Treatment!=desired!.Treatment||import.TaxRate is not null&&import.TaxRate!=desired!.TaxRate||import.PricesIncludeTax is not null&&import.PricesIncludeTax!=desired!.PricesIncludeTax||import.ReviewReference is not null&&import.ReviewReference!=desired!.ReviewReference))invalid="Imported product code, SKU, HSN, treatment, rate, pricing or reference differs from the selected reviewed rule/item.";
            else if(desired is not null)
            {
                try{GstRules.Validate(desired);}catch(ValidationException e){invalid=e.Message;}
                if(desired.EffectiveFrom<today||desired.EffectiveFrom<rule!.Tax.EffectiveFrom||rule.Tax.EffectiveTo is not null&&(desired.EffectiveTo is null||desired.EffectiveTo>rule.Tax.EffectiveTo))invalid="Use current/future dates entirely within the reviewed rule's validity.";
            }
            if(invalid is not null){row.Status="INVALID";row.Message=invalid;continue;}
            var overlaps=relevant.Where(t=>t.EffectiveFrom<=(desired!.EffectiveTo??DateTime.MaxValue)&&(t.EffectiveTo??DateTime.MaxValue)>=desired.EffectiveFrom).ToArray();
            if(overlaps.Any(t=>SameTax(t,desired!)&&t.EffectiveFrom<=desired!.EffectiveFrom&&(t.EffectiveTo is null||desired.EffectiveTo is not null&&t.EffectiveTo>=desired.EffectiveTo))){row.Status="MATCHING";row.Message="Already covered by the same classification; no write needed.";continue;}
            if(overlaps.Any(t=>t.StoreId==store&&t.EffectiveFrom>=desired!.EffectiveFrom)){row.Status="REVIEW";row.Message="Existing same-day/future mapping conflicts. Choose a later non-overlapping effective date.";continue;}
            var differing=overlaps.Length>0||product!.TaxConfigId is not null&&(product.RuleId!=desired!.RuleId||product.TaxRate!=desired.TaxRate||product.Treatment!=desired.Treatment)||product.HsnSac is not null&&product.HsnSac!=desired!.HsnSac||product.ProductHsn is {Length:>0}&&product.ProductHsn!=desired!.HsnSac;
            if(differing&&!request.OverrideExisting){row.Status="REVIEW";row.Message="Existing mapping or HSN differs. Review classification and request an authorized override with a reason.";continue;}
            if(differing&&desired!.EffectiveFrom<=today&&overlaps.Any(t=>t.StoreId==store)){row.Status="REVIEW";row.Message="Replacing a reviewed mapping must start after today; posted and current-day periods are retained.";}
            else if(differing)row.Message="Authorized classification override; prior periods and variant overrides retained.";
        }
        await c.ExecuteAsync("""
        INSERT INTO gst_tax_operation_rows(tenant_id,operation_id,row_number,product_id,variant_id,product_name,status,message,before_value,proposed_value,fingerprint)
        SELECT sf_tenant(),@id,x.row,x."productId",x."variantId",x."productName",x.status,x.message,CAST(x."beforeValue" AS jsonb),CAST(x."proposedValue" AS jsonb),x.fingerprint
        FROM jsonb_to_recordset(CAST(@rows AS jsonb)) AS x(row integer,"productId" integer,"variantId" bigint,"productName" text,status text,message text,"beforeValue" text,"proposedValue" text,fingerprint text)
        """,new{id=request.RequestId,rows=CatalogSerialize(staged)},tx);
        var selectedRows=targets.Distinct().Where(records.ContainsKey).Select(t=>records[t]).ToArray();
        var selection=new{Products=selectedRows.Select(r=>r.ProductId).Distinct().Count(),SellableVariants=selectedRows.DistinctBy(r=>r.ProductId).Sum(r=>r.VariantCount),Categories=selectedRows.Select(r=>r.Category).Distinct().ToArray(),ExistingClassifications=selectedRows.Select(r=>r.Treatment??"Missing").Distinct().ToArray(),HsnValues=selectedRows.Select(r=>r.HsnSac??r.ProductHsn).Where(v=>v is not null).Distinct().Count(),ExistingOverrides=selectedRows.DistinctBy(r=>r.ProductId).Sum(r=>r.OverrideCount),RuleConditions=rules.Values.Select(r=>r.RuleName+": "+r.Description+" | Evidence: "+r.Tax.ReviewReference).ToArray()};
        await c.ExecuteAsync("UPDATE gst_tax_operations SET selection_summary=CAST(@summary AS jsonb) WHERE operation_id=@id",new{id=request.RequestId,summary=CatalogSerialize(selection)},tx);
        await tx.CommitAsync();return await OperationAsync(request.RequestId);
    }
    private static bool JsonEqual(string a,string b)=>System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(a),System.Text.Json.Nodes.JsonNode.Parse(b));
    public async Task<GstOperationPage> OperationsAsync(int page=1,int pageSize=10)
    {
        if(page<1||page>100000||!new[]{10,25,50,100}.Contains(pageSize))throw new ValidationException("Choose valid history pagination.");
        await using var c=factory.CreateConnection();await CatalogPermission(c);var count=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_tax_operations");
        var rows=await c.QueryAsync<GstOperationSummary>("SELECT o.operation_id OperationId,o.status,count(r.row_number) Total,count(*) FILTER(WHERE r.status='UPDATED') Updated,count(*) FILTER(WHERE r.status IN('FAILED','INVALID','REVIEW')) Failed,o.created_at CreatedAt FROM (SELECT * FROM gst_tax_operations ORDER BY created_at DESC OFFSET @offset LIMIT @size) o LEFT JOIN gst_tax_operation_rows r USING(tenant_id,operation_id) GROUP BY o.operation_id,o.status,o.created_at ORDER BY o.created_at DESC",new{offset=(page-1L)*pageSize,size=pageSize});return new(count,rows);
    }
    private static bool SameTax(GstTaxConfiguration a,GstTaxConfiguration b)=>a.RuleId==b.RuleId&&a.HsnSac==b.HsnSac&&a.Classification==b.Classification&&a.Uqc==b.Uqc&&a.Treatment==b.Treatment&&a.TaxRate==b.TaxRate&&a.CessRate==b.CessRate&&a.ReverseCharge==b.ReverseCharge&&a.PricesIncludeTax==b.PricesIncludeTax&&a.ReviewReference==b.ReviewReference;
    public async Task<GstOperation> OperationAsync(Guid id,int page=1,int pageSize=25,string? status=null)
    {
        if(page<1||page>100000||!new[]{10,25,50,100}.Contains(pageSize))throw new ValidationException("Choose valid operation paging.");
        var statuses=status switch{null or ""=>Array.Empty<string>(),"valid"=>new[]{"READY","MATCHING","UPDATED"},"warning"=>new[]{"REVIEW"},"invalid"=>new[]{"INVALID","FAILED"},_=>throw new ValidationException("Choose a valid preview status filter.")};
        await using var c=factory.CreateConnection();await CatalogPermission(c);
        var operationStatus=await c.ExecuteScalarAsync<string?>("SELECT status FROM gst_tax_operations WHERE operation_id=@id",new{id})??throw new ValidationException("Operation is unavailable in this shop or belongs to another user.");
        var counts=(await c.QueryAsync<(string Status,long Count)>("SELECT status,count(*) FROM gst_tax_operation_rows WHERE operation_id=@id GROUP BY status",new{id})).ToDictionary(r=>r.Status,r=>r.Count);
        var filtered=counts.Where(k=>statuses.Length==0||statuses.Contains(k.Key)).Sum(k=>k.Value);
        var rows=await c.QueryAsync<AssignmentRow>("SELECT row_number Row,product_id ProductId,variant_id VariantId,product_name ProductName,status,message,before_value::text BeforeValue,proposed_value::text ProposedValue FROM gst_tax_operation_rows WHERE operation_id=@id AND (cardinality(@statuses)=0 OR status=ANY(@statuses)) ORDER BY row_number OFFSET @offset LIMIT @size",new{id,statuses,offset=(page-1L)*pageSize,size=pageSize});
        var scope=await c.ExecuteScalarAsync<string>("SELECT s.store_name||' / '||o.business_profile_code FROM gst_tax_operations o JOIN stores s USING(tenant_id,store_id) WHERE operation_id=@id",new{id});
        var summary=await c.ExecuteScalarAsync<string>("SELECT selection_summary::text FROM gst_tax_operations WHERE operation_id=@id",new{id});
        return new(id,operationStatus,counts.Values.Sum(),counts,rows.Select(r=>r.Model()),scope!,summary,filtered);
    }
    public async Task<GstOperation> CommitAssignmentAsync(GstOperationConfirm request)
    {
        if(!request.Confirm)throw new ValidationException("Review the preview and explicitly confirm the assignment.");
        await using var c=factory.CreateConnection();await CatalogPermission(c,"EDIT");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,tx);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        var op=await c.QuerySingleOrDefaultAsync<(string Request,string Status)?>("SELECT request::text,status FROM gst_tax_operations WHERE operation_id=@id FOR UPDATE",new{id=request.OperationId},tx)??throw new ValidationException("Operation is unavailable.");
        var original=JsonSerializer.Deserialize<GstAssignment>(op.Request,CatalogJson)!;await RequireAsync(c,original.Import is not null?"IMPORT_TAX":original.AllMatching||original.Targets.Count>1?"BULK_TAX":"EDIT");if(original.OverrideExisting)await RequireAsync(c,"OVERRIDE_TAX");
        if(op.Status=="COMPLETED"){await tx.CommitAsync();return await OperationAsync(request.OperationId);}
        var rows=(await c.QueryAsync<AssignmentRow>("SELECT row_number Row,product_id ProductId,variant_id VariantId,product_name ProductName,proposed_value::text ProposedValue,fingerprint FROM gst_tax_operation_rows WHERE operation_id=@id AND status='READY' ORDER BY row_number LIMIT 100 FOR UPDATE",new{id=request.OperationId},tx)).ToArray();
        foreach(var row in rows)
        {
            await tx.SaveAsync("tax_row");
            try
            {
                var t=JsonSerializer.Deserialize<GstTaxConfiguration>(row.ProposedValue!,CatalogJson)!;
                var q=new GstCatalogQuery{ProductId=t.ProductId,Variants=t.VariantId is not null};
                var current=(await c.QueryAsync<GstCatalogRow>(CatalogSql+"SELECT * FROM filtered",CatalogParameters(q),tx)).SingleOrDefault(r=>r.VariantId==t.VariantId);
                if(current is null||current.Fingerprint!=row.Fingerprint)throw new ValidationException("Product/mapping changed after preview. Create a fresh preview for this item.");
                var today=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE",transaction:tx);if(t.EffectiveFrom<today)throw new ValidationException("Preview dates have passed; create a fresh preview.");
                var rule=await c.QuerySingleOrDefaultAsync<RuleRecord>(RuleSelect+" WHERE rule_id=@id",new{id=t.RuleId},tx);
                if(rule is null||rule.ReviewStatus!="REVIEWED"||rule.Status!="ACTIVE")throw new ValidationException("Reviewed rule is unavailable.");
                if(original.OverrideExisting)
                    await c.ExecuteAsync("UPDATE gst_tax_configurations SET effective_to=@end WHERE product_id=@ProductId AND store_id=sf_store() AND variant_id IS NOT DISTINCT FROM @VariantId AND effective_from<@EffectiveFrom AND (effective_to IS NULL OR effective_to>=@EffectiveFrom)",new{t.ProductId,t.VariantId,t.EffectiveFrom,end=t.EffectiveFrom.AddDays(-1)},tx);
                await c.ExecuteAsync("""
                INSERT INTO gst_tax_configurations(store_id,business_profile_code,product_id,variant_id,rule_id,change_reason,classification,hsn_sac,uqc,treatment,tax_rate,cess_rate,reverse_charge,prices_include_tax,effective_from,effective_to,review_reference)
                VALUES(sf_store(),sf_visible_business_profile(),@ProductId,@VariantId,@RuleId,@ChangeReason,@Classification,@HsnSac,@Uqc,@Treatment,@TaxRate,@CessRate,@ReverseCharge,@PricesIncludeTax,@EffectiveFrom,@EffectiveTo,@ReviewReference)
                """,t,tx);
                row.Status="UPDATED";row.Message="Tax mapping saved; no operational records changed.";
            }
            catch(Exception e) when(e is ValidationException or PostgresException)
            {
                await tx.RollbackAsync("tax_row");row.Status="FAILED";row.Message=e is PostgresException p?p.MessageText:e.Message;
            }
            await c.ExecuteAsync("UPDATE gst_tax_operation_rows SET status=@Status,message=@Message WHERE operation_id=@id AND row_number=@Row",new{id=request.OperationId,row.Row,row.Status,row.Message},tx);
        }
        await c.ExecuteAsync("UPDATE gst_tax_operations SET status=CASE WHEN EXISTS(SELECT 1 FROM gst_tax_operation_rows WHERE operation_id=@id AND status='READY') THEN 'RUNNING' ELSE 'COMPLETED' END,updated_at=now() WHERE operation_id=@id",new{id=request.OperationId},tx);
        await tx.CommitAsync();return await OperationAsync(request.OperationId);
    }
}
