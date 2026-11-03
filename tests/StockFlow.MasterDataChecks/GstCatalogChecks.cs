using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Services;

namespace StockFlow.MasterDataChecks;
internal static class GstCatalogChecks
{
    public static async Task Run(bool measureLarge=false)
    {
        var database="gst_catalog_"+Guid.NewGuid().ToString("N");const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,ProfileDataVisibilityAvailable=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var repo=new GstRepository(new DbConnectionFactory(config,scope));var today=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");
            var options=await repo.CatalogOptionsAsync();Check(options.Categories.Any()&&options.Brands.Any(),"Step 4 options materialize actual category and brand dropdown records");
            var before=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT md5(string_agg(coalesce(sku,'')||coalesce(barcode,''),',' ORDER BY product_id)) FROM products)");
            var page=await repo.CatalogAsync(new());Check(page.Total>0,"Server-side catalog reads scoped products");var product=page.Rows.First();
            var rule=new GstTaxRule{RuleName="Synthetic classification",Description="Synthetic test evidence; not a legal reference",Tax=new(){HsnSac=product.ProductHsn is {Length:4 or 6 or 8}?product.ProductHsn:"610910",Treatment="TAXABLE",TaxRate=5,EffectiveFrom=today,ReviewReference="Synthetic accountant classification"}};
            var id=await repo.SaveRuleAsync(rule);Check((await repo.TaxRulesAsync(new())).Rows.Single(r=>r.RuleId==id).ReviewStatus=="DRAFT","Rules begin as drafts");
            var draft=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,Targets=[new(product.ProductId)]});Check(draft.Counts.GetValueOrDefault("INVALID")==1,"Unreviewed rules cannot be assigned");
            await repo.ReviewRuleAsync(id);var request=new GstAssignment{RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,Targets=[new(product.ProductId)]};
            var preview=await repo.PreviewAssignmentAsync(request);Check(preview.Counts.GetValueOrDefault("READY")==1,"Single product gets validated preview");
            Check((await repo.PreviewAssignmentAsync(request)).OperationId==preview.OperationId,"Repeated preview reuses saved operation");
            var result=await repo.CommitAssignmentAsync(new(preview.OperationId,true));Check(result.Counts.GetValueOrDefault("UPDATED")==1,"Confirmed mapping saved");
            Check((await repo.CommitAssignmentAsync(new(preview.OperationId,true))).Counts.GetValueOrDefault("UPDATED")==1,"Repeated confirmation cannot double-apply");
            Check((await repo.CatalogAsync(new(){ProductId=product.ProductId})).Rows.Single().MappingStatus=="Configured","Readiness shows configured mapping");
            var history=await repo.MappingHistoryAsync(product.ProductId,null);Check(history.Versions.Any(t=>t.RuleId==id)&&history.Audit.Any(),"Effective mapping and audit history retained");
            var variant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@id AND is_active LIMIT 1",new{id=product.ProductId});
            var variantRequest=new GstAssignment{RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,Targets=[new(product.ProductId,variant)],Filter=new(){Variants=true}};
            var vp=await repo.PreviewAssignmentAsync(variantRequest);Check(vp.Counts.GetValueOrDefault("READY")==1,"Variant override preview uses stable variant identity");await repo.CommitAssignmentAsync(new(vp.OperationId,true));
            Check((await repo.CatalogAsync(new(){ProductId=product.ProductId,Variants=true})).Rows.Any(r=>r.MappingSource=="Variant override"),"Variant override is distinguishable from inheritance");
            var variantVersion=(await repo.MappingHistoryAsync(product.ProductId,variant)).Versions.Single(t=>t.VariantId==variant);variantVersion.EffectiveTo=today;await repo.SaveTaxAsync(variantVersion);
            var variantRule=await repo.SaveRuleAsync(new(){RuleName="Synthetic variant conditions",Description="Synthetic alternative classification evidence",Tax=new(){HsnSac=rule.Tax.HsnSac,TaxRate=12,EffectiveFrom=today.AddDays(1),ReviewReference="Synthetic variant-specific reviewed evidence"}});await repo.ReviewRuleAsync(variantRule);
            var future=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),RuleId=variantRule,EffectiveFrom=today.AddDays(1),Targets=[new(product.ProductId,variant)],Filter=new(){Variants=true},OverrideExisting=true,Reason="Synthetic variant conditions differ"});Check((await repo.CommitAssignmentAsync(new(future.OperationId,true))).Counts.GetValueOrDefault("UPDATED")==1,"Authorized future variant classification preserves the prior period");
            Check(await c.ExecuteScalarAsync<decimal>("SELECT tax_rate FROM sf_gst_resolve_tax(@productId,@variant,CAST(@day AS date))",new{productId=product.ProductId,variant,day=today.AddDays(1)})==12&&await c.ExecuteScalarAsync<decimal>("SELECT tax_rate FROM sf_gst_resolve_tax(@productId,NULL::bigint,CAST(@day AS date))",new{productId=product.ProductId,day=today.AddDays(1)})==5,"Checkout resolver selects the dated variant override without changing its product default");
            var duplicate=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,Targets=[new(product.ProductId),new(product.ProductId)]});Check(duplicate.Counts.GetValueOrDefault("INVALID")==2,"Duplicate assignment rows are rejected");
            var csv=GstMappingSpreadsheet.Header+"\r\n"+product.ProductId+",,,"+id+",,,5,"+today.ToString("yyyy-MM-dd")+",,\r\n";
            Check(GstMappingSpreadsheet.Read(System.Text.Encoding.UTF8.GetBytes(csv),"test.csv").Count==1,"CSV stable-ID import parsed");
            Check(GstMappingSpreadsheet.Read(GstMappingSpreadsheet.Xlsx(csv),"test.xlsx").Count==1,"XLSX template/export and import roundtrip");
            var imported=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),Import=GstMappingSpreadsheet.Read(System.Text.Encoding.UTF8.GetBytes(csv),"test.csv")});Check(imported.Counts.GetValueOrDefault("MATCHING")==1,"Import validates against reviewed rule and existing mapping");
            var invalidImport=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),Import=[new(){ProductId=product.ProductId,RuleId=id,EffectiveFrom=today,TaxRate=99}]});Check(invalidImport.Counts.GetValueOrDefault("INVALID")==1,"Imported rate inconsistent with reviewed rule is rejected");
            var staleProduct=page.Rows.First(r=>r.ProductId!=product.ProductId);
            var stale=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,Targets=[new(staleProduct.ProductId)],OverrideExisting=true,Reason="Synthetic classification correction"});
            await repo.SaveTaxAsync(new(){ProductId=staleProduct.ProductId,RuleId=id,HsnSac=rule.Tax.HsnSac,TaxRate=5,EffectiveFrom=today,ReviewReference=rule.Tax.ReviewReference,ChangeReason="Synthetic concurrent correction"});
            Check((await repo.CommitAssignmentAsync(new(stale.OperationId,true))).Counts.GetValueOrDefault("FAILED")==1,"Stale preview cannot overwrite a concurrent mapping change");
            var exported=await repo.ExportMappingsAsync(new(){ProductId=product.ProductId});Check(System.Text.Encoding.UTF8.GetString(exported.Content).Contains(product.ProductId.ToString()),"Filtered export includes stable IDs");
            var template=await repo.ImportTemplateAsync(new(){ProductId=product.ProductId});
            var templateTable=GstMappingSpreadsheet.ReadCsv(System.Text.Encoding.UTF8.GetString(template.Content).TrimStart('\ufeff'));
            Check(templateTable.Count==2&&templateTable[1][0]==product.ProductId.ToString()&&templateTable[1][2]==product.ProductCode&&templateTable[1][4]==product.ProductSku,"Import template prefills stable IDs, actual product codes and SKUs");
            Check(templateTable[1].Skip(6).All(string.IsNullOrEmpty),"Template invents no HSN, percentage or reviewed classification");
            var matchingCode=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),Import=[new(){ProductId=product.ProductId,ProductCode="WRONG-CODE",RuleId=id}]});Check(matchingCode.Counts.GetValueOrDefault("INVALID")==1,"Imported identifiers must agree with their stable product ID");
            var mismatchedPricing=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),Import=[new(){ProductId=product.ProductId,RuleId=id,PricesIncludeTax=true}]});Check(mismatchedPricing.Counts.GetValueOrDefault("INVALID")==1,"Explicit imported pricing must agree with the reviewed rule");
            await Reject(()=>Task.Run(()=>GstMappingSpreadsheet.Read(System.Text.Encoding.UTF8.GetBytes("ProductId,RuleId,Unknown\n1,1,value"),"bad.csv")),"Unexpected import headers are rejected");
            await c.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='GST_SETTINGS' AND a.action_code IN('BULK_TAX','OVERRIDE_TAX','APPROVE_TAX','IMPORT_TAX','EXPORT_TAX')");
            await Reject(()=>repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,Targets=[new(product.ProductId),new(product.ProductId+1)]}),"Bulk assignment cannot bypass explicit permission");
            await Reject(()=>repo.ExportMappingsAsync(new()),"Export cannot bypass explicit permission");
            await Reject(()=>repo.ImportTemplateAsync(new()),"Prefilled template download requires import permission");
            await Reject(()=>repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),Import=[new(){ProductId=product.ProductId,RuleId=id,EffectiveFrom=today}]}),"Import cannot bypass explicit permission");
            await c.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='GST_SETTINGS' AND a.action_code IN('BULK_TAX','OVERRIDE_TAX','APPROVE_TAX','IMPORT_TAX','EXPORT_TAX')");
            scope.StoreId=2;scope.LocationId=2;Check(!(await repo.CatalogAsync(new(){ProductId=product.ProductId})).Rows.Any(r=>r.RuleId==id),"Same-profile second shop does not inherit first shop's new mapping");
            await Reject(()=>repo.OperationAsync(preview.OperationId),"Another shop cannot resume operation");scope.StoreId=1;scope.LocationId=1;
            scope.TenantId=999;await Reject(()=>repo.CatalogAsync(new()),"Forged tenant denied");scope.TenantId=1;
            var after=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT md5(string_agg(coalesce(sku,'')||coalesce(barcode,''),',' ORDER BY product_id)) FROM products)");Check(before==after,"Tax administration preserves invoices, stock, payments, SKUs and barcodes");
            var category=await c.ExecuteScalarAsync<int>("SELECT category_id FROM products WHERE product_id=@id",new{id=product.ProductId});
            var connectionFactory=new DbConnectionFactory(config,scope);var productRepo=new ProductRepository(connectionFactory);
            var productService=new StockFlow.Application.Services.ProductService(productRepo,new CategoryRepository(connectionFactory),new BrandRepository(connectionFactory),scope,null,new VariantRepository(connectionFactory));
            var registrationRequest=Guid.NewGuid();await using var runtime=connectionFactory.CreateConnection();var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@category,@registrationRequest,NULL)",new{category,registrationRequest});
            var registered=await productService.CreateAsync(new(){RegistrationRequestId=registrationRequest,ParentProductCode=code,CategoryId=category,ProductName="GST Registered synthetic product",TrackingType="QUANTITY",SellingPrice=100,HsnCode=rule.Tax.HsnSac,TaxPercentage=5,GstTaxRuleId=id});
            Check((await repo.CatalogAsync(new(){ProductId=registered})).Rows.Single().RuleId==id,"Normal product registration atomically assigns its selected reviewed rule");
            var timer=Stopwatch.StartNew();
            await c.ExecuteAsync("INSERT INTO products(category_id,product_name,sku,hsn_code,selling_price) SELECT @category,'GST Perf '||n,'GSTPERF-'||n,'610910',100 FROM generate_series(1,2000) n",new{category});
            var large=await repo.CatalogAsync(new(){Search="GST Perf",PageSize=25});Check(large.Total==2000&&large.Rows.Count()==25,"Large catalog paginates on the server");
            var allRequest=new GstAssignment{RequestId=Guid.NewGuid(),RuleId=id,EffectiveFrom=today,AllMatching=true,ConfirmAllMatching=true,Filter=new(){Search="GST Perf"}};
            var allPreview=await repo.PreviewAssignmentAsync(allRequest);Check(allPreview.Total==2000&&allPreview.Counts.GetValueOrDefault("READY")==2000,"Select All Matching captures all 2,000 backend results beyond the visible page");
            var largeTemplate=await repo.ImportTemplateAsync(new(){Search="GST Perf",PageSize=10,Page=2});var largeRows=GstMappingSpreadsheet.ReadCsv(System.Text.Encoding.UTF8.GetString(largeTemplate.Content).TrimStart('\ufeff'));
            Check(largeRows.Count==2001,"Download includes all 2,000 filtered products rather than the visible page");
            foreach(var dataRow in largeRows.Skip(1))dataRow[6]=id.ToString();
            var completedCsv=string.Join("\r\n",largeRows.Select(row=>string.Join(',',row.Select(v=>"\""+v.Replace("\"","\"\"")+"\""))));
            var xlsx=GstMappingSpreadsheet.Xlsx(completedCsv,true);
            using(var archive=new System.IO.Compression.ZipArchive(new MemoryStream(xlsx)))Check(archive.GetEntry("xl/worksheets/sheet2.xml") is not null,"Excel template includes a separate instruction sheet");
            var parsed=GstMappingSpreadsheet.Read(xlsx,"filled-template.xlsx");Check(parsed.Count==2000&&parsed.All(row=>row.Error is null&&row.EffectiveFrom==default),"Owner only fills RuleId; both identifiers and blank optional tax fields roundtrip through Excel");
            await GstImportHttpChecks.Run(host+database,completedCsv);
            var importPreview=await repo.PreviewAssignmentAsync(new(){RequestId=Guid.NewGuid(),Import=parsed});Check(importPreview.Counts.GetValueOrDefault("READY")==2000,"All 2,000 file rows preview using their reviewed rule details and effective dates");
            Check((await repo.OperationAsync(importPreview.OperationId,1,25,"invalid")).FilteredTotal==0,"Preview status filtering reports correct totals");
            GstOperation batch=importPreview;var chunks=0;do{batch=await repo.CommitAssignmentAsync(new(batch.OperationId,true));chunks++;if(chunks==1)Check(batch.Status=="RUNNING"&&batch.Counts.GetValueOrDefault("UPDATED")==100,"First chunk is recoverable after 100 updates");}while(batch.Status=="RUNNING");
            Check(batch.Counts.GetValueOrDefault("UPDATED")==2000,"Resumed chunks import 2,000 mappings exactly once");Check((await repo.CommitAssignmentAsync(new(batch.OperationId,true))).Counts.GetValueOrDefault("UPDATED")==2000,"Repeated file confirmation is idempotent");Console.WriteLine("MEASURE: seed, templates, preview and 2,000-row import: "+timer.Elapsed.TotalSeconds.ToString("F2")+" seconds; "+chunks+" chunks.");
            await repo.EndRuleAsync(new(id,today.AddDays(2),"Synthetic notification change",true));
            Check((await repo.TaxRulesAsync(new())).Rows.Single(r=>r.RuleId==id).Tax.EffectiveTo==today.AddDays(2),"Rule ending prospectively also ends assigned mappings with audit history");
            Check((await repo.CatalogAsync(new(){ProductId=product.ProductId,Date=today.AddDays(3)})).Rows.Single().MappingStatus=="Expired","Expired classification never counts as configured");
            var v2=new GstTaxRule{RuleName=rule.RuleName,Description=rule.Description,Tax=new(){HsnSac=rule.Tax.HsnSac,TaxRate=12,EffectiveFrom=today.AddDays(3),ReviewReference="Synthetic rate change reference"}};var nextId=await repo.SaveRuleAsync(v2);await repo.ReviewRuleAsync(nextId);Check(nextId!=id,"Next non-overlapping rule version preserves earlier reviewed identity");
            if(measureLarge)
            {
                // Fixture-only bulk seed for read performance. The normal registration path is tested above.
                // Do not use trigger bypass in application code or on a client database.
                await using(var seed=await c.BeginTransactionAsync())
                {
                    await c.ExecuteAsync("ALTER TABLE products DISABLE TRIGGER USER; ALTER TABLE product_variants DISABLE TRIGGER USER",transaction:seed);
                    await c.ExecuteAsync("INSERT INTO products(tenant_id,identifier_profile,category_id,product_name,sku,parent_product_code,hsn_code,selling_price) SELECT 1,'ELECTRICAL',@category,'GST Perf '||n,'GSTPERF-'||n,'GSTPERF-'||n,'610910',100 FROM generate_series(1001,50000) n",new{category},seed,commandTimeout:120);
                    await c.ExecuteAsync("INSERT INTO product_variants(tenant_id,identifier_profile,product_id,variant_name,sku,is_default,selling_price) SELECT 1,'ELECTRICAL',product_id,'DEFAULT',sku,true,100 FROM products WHERE product_name LIKE 'GST Perf %' AND NOT EXISTS(SELECT 1 FROM product_variants v WHERE v.product_id=products.product_id)",transaction:seed,commandTimeout:120);
                    await c.ExecuteAsync("ALTER TABLE products ENABLE TRIGGER USER; ALTER TABLE product_variants ENABLE TRIGGER USER; ANALYZE products; ANALYZE product_variants; ANALYZE gst_tax_configurations",transaction:seed);await seed.CommitAsync();
                }
                timer.Restart();var fifty=await repo.CatalogAsync(new(){Search="GST Perf",PageSize=25,Page=1500});Check(fifty.Total==50000&&fifty.Rows.Count()==25,"50,000-product catalog returns only requested server page");Console.WriteLine("MEASURE: filtered count, summary and deep page over 50,000 products: "+timer.Elapsed.TotalSeconds.ToString("F2")+" seconds.");
            }
            Console.WriteLine("GST catalog checks passed.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private static async Task Reject(Func<Task> action,string message){try{await action();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or PostgresException){Console.WriteLine("PASS: "+message);return;}throw new Exception("Expected rejection: "+message);}
}
