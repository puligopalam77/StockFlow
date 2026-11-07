using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class GstSetupChecks
{
    public static async Task Run()
    {
        var database="gst_setup_test_"+Guid.NewGuid().ToString("N");const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
            long upgradeId=0;
            foreach(var file in Directory.GetFiles(Path.Combine(root!.FullName,"database/migrations"),"*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order()){
                if(Path.GetFileName(file).StartsWith("088")){
                    await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
                    upgradeId=await c.ExecuteScalarAsync<long>("INSERT INTO gst_legal_businesses(legal_name,address,state_code,pin_code) VALUES('Upgrade fixture','Original unsplit address','27','400001') RETURNING business_id");
                }
                await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            }
            Check(await c.ExecuteScalarAsync<bool>("SELECT address='Original unsplit address' AND address_line1 IS NULL AND city IS NULL AND pin_code='400001' FROM gst_legal_businesses WHERE business_id=@upgradeId",new{upgradeId}),"Migration preserves pre-existing combined address and PIN without guessed backfill");
            Check(await c.ExecuteScalarAsync<bool>("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='gst_legal_businesses'::regclass"),"Migration retains forced row-level security");
            await Reject(()=>c.ExecuteAsync("UPDATE gst_legal_businesses SET city='Partial city' WHERE business_id=@upgradeId",new{upgradeId}),"Database rejects incomplete structured components without altering legacy row");
            Check(await c.ExecuteScalarAsync<string>("SELECT address FROM gst_legal_businesses WHERE business_id=@upgradeId",new{upgradeId})=="Original unsplit address","Rollback-compatible combined-address read remains available");

            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
            var before=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,ProfileDataVisibilityAvailable=true};var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();var repo=new GstRepository(new DbConnectionFactory(config,scope));
            var day=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");
            var missingRegistration=await repo.SetupReadinessAsync(0);
            Check(!missingRegistration.CanActivate&&missingRegistration.Checks.Any(x=>x.Key=="registration"&&x.Status=="error"),"No registration cannot fabricate activation readiness");
            GstBusiness Business()=>new(){LegalName="Synthetic setup business",Address="Synthetic legal address",StateCode="27",PinCode="400001",Pan="AAPFU0939F"};
            var businesses=await Task.WhenAll(repo.SaveBusinessAsync(Business()),repo.SaveBusinessAsync(Business()));Check(businesses.Distinct().Count()==1,"Concurrent identical business creates reuse one saved record");var business=businesses[0];
            var legacy=(await repo.SettingsAsync()).Businesses.Single(b=>b.BusinessId==business);legacy.TradeName="Unrelated edit";await repo.SaveBusinessAsync(legacy);
            Check((await repo.SettingsAsync()).Businesses.Single(b=>b.BusinessId==business).Address=="Synthetic legal address","Unrelated legacy edit preserves original combined address");
            var structured=(await repo.SettingsAsync()).Businesses.Single(b=>b.BusinessId==business);structured.AddressLine1="12 Example Street";structured.AddressLine2=null;structured.City="Mumbai";structured.District=null;
            await repo.SaveBusinessAsync(structured);structured=(await repo.SettingsAsync()).Businesses.Single(b=>b.BusinessId==business);
            Check(structured.AddressLine1=="12 Example Street"&&structured.City=="Mumbai"&&structured.Address==GstRules.FormatAddress(structured)&&structured.PinCode=="400001","Structured address persists, reloads and formats with optional fields blank");
            Directory.CreateDirectory("tests/bin/gst-ui-fixtures");
            await File.WriteAllTextAsync("tests/bin/gst-ui-fixtures/structured-business.json",JsonSerializer.Serialize(structured,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var duplicate=new GstBusiness{LegalName="Synthetic setup business",TradeName="Unrelated edit",AddressLine1="12 Example Street",City="Mumbai",StateCode="27",PinCode="400001",Pan="AAPFU0939F"};
            Check(await repo.SaveBusinessAsync(duplicate)==business,"Structured address retry reuses the same business identity");
            await Reject(()=>repo.SaveBusinessAsync(new(){LegalName="Invalid address",AddressLine1="Street",StateCode="27",PinCode="400001"}),"Structured address requires city on backend");
            await Reject(()=>repo.SaveBusinessAsync(new(){LegalName="Invalid address",AddressLine1="Street",City="Mumbai",StateCode="27",PinCode="040001"}),"Existing PIN rule rejects leading zero without numeric conversion");
            await Reject(()=>repo.SaveBusinessAsync(new(){LegalName="Invalid address",AddressLine1=new string('x',251),City="Mumbai",StateCode="27",PinCode="400001"}),"Structured address limits enforced on backend");
            var forged=new GstBusiness{BusinessId=long.MaxValue,LegalName="Forged",AddressLine1="Street",City="Mumbai",StateCode="27",PinCode="400001"};
            await Reject(()=>repo.SaveBusinessAsync(forged),"Forged legal-business identity rejected");
            var registration=new GstRegistration{BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="27",Address="Synthetic registration address",EffectiveFrom=day.AddDays(-1)};
            var reg=await repo.SaveRegistrationAsync(registration);Check(await repo.SaveRegistrationAsync(registration)==reg,"Identical registration retry reuses saved identity and date");
            await Reject(()=>repo.SaveRegistrationAsync(new(){BusinessId=business,Gstin="27AAPFU0939F1ZA",StateCode="27",Address="Synthetic invalid registration",EffectiveFrom=day}),"Invalid registration checksum rejected");
            await Reject(()=>repo.SaveRegistrationAsync(new(){BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="29",Address="Synthetic mismatched state",EffectiveFrom=day}),"GSTIN registration state mismatch rejected");
            await Reject(()=>repo.SaveMappingAsync(new(){RegistrationId=reg,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=day.AddDays(-3),Status="INACTIVE"}),"Assignment dates outside registration period rejected");
            var map=new GstMapping{RegistrationId=reg,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=day.AddDays(-1),Status="INACTIVE"};var mapping=await repo.SaveMappingAsync(map);Check(await repo.SaveMappingAsync(map)==mapping,"Identical assignment retry reuses saved row");
            var initial=await repo.SetupReadinessAsync(reg);Check(!initial.CanActivate&&!initial.Ready&&initial.Checks.Any(x=>x.Key=="taxes"&&x.Status=="error"),"Readiness uses backend product tax coverage rather than visited steps");
            Directory.CreateDirectory("tests/bin/gst-ui-fixtures");
            var jsonOptions=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            await File.WriteAllTextAsync("tests/bin/gst-ui-fixtures/readiness-incomplete.json",JsonSerializer.Serialize(initial,jsonOptions));
            await Reject(()=>repo.ActivateSetupAsync(new(reg,initial.Fingerprint)),"Incomplete setup cannot activate");
            long firstTax=0;foreach(var product in (await repo.SettingsAsync()).Products)
            {
                var tax=new GstTaxConfiguration{ProductId=product.ProductId,HsnSac="610910",Uqc="NOS",TaxRate=0,EffectiveFrom=day.AddDays(-1),ReviewReference="Synthetic reviewed test classification"};
                var id=await repo.SaveTaxAsync(tax);Check(await repo.SaveTaxAsync(tax)==id,"Identical product tax version retry does not create an overlapping duplicate");if(firstTax==0)firstTax=id;
            }
            var ready=await repo.SetupReadinessAsync(reg);Check(ready.CanActivate&&!ready.Ready,"Saved inactive assignment can activate only after valid saved tax versions");
            await File.WriteAllTextAsync("tests/bin/gst-ui-fixtures/readiness-ready.json",JsonSerializer.Serialize(ready,jsonOptions));
            await repo.SaveTaxAsync(new(){TaxConfigId=firstTax,ProductId=(await repo.SettingsAsync()).Taxes.First(t=>t.TaxConfigId==firstTax).ProductId,HsnSac="610910",Uqc="NOS",EffectiveFrom=day.AddDays(-1),EffectiveTo=day.AddDays(10),ReviewReference="Synthetic reviewed test classification"});
            await Reject(()=>repo.ActivateSetupAsync(new(reg,ready.Fingerprint)),"Changed configuration invalidates a stale activation review");
            ready=await repo.SetupReadinessAsync(reg);var activated=await repo.ActivateSetupAsync(new(reg,ready.Fingerprint));Check(activated.Ready,"Activation uses existing mapping ACTIVE status in a transaction");
            var auditCount=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_audit_history");await repo.ActivateSetupAsync(new(reg,ready.Fingerprint));Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_audit_history")==auditCount,"Activation retry is a no-op without duplicate audit writes");
            Check(await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text")==before,"Initial activation does not post business transactions");
            await Reject(()=>repo.SaveMappingAsync(new(){RegistrationId=reg,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=day,Status="ACTIVE"}),"Overlapping active shop assignment rejected");
            // Real immutable GST capture in the disposable fixture, not a UI-calculated snapshot.
            var sku=await c.QuerySingleAsync<(int Product,long Variant)>("SELECT p.product_id,v.variant_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.identifier_profile='ELECTRICAL' AND p.is_active AND v.is_active ORDER BY p.product_id,v.variant_id LIMIT 1");
            await using var historyTx=await c.BeginTransactionAsync();
            var historicalSale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,status,created_by,request_id,request_payload,subtotal,tax_amount,discount_amount,total_amount,customer_name_snapshot,invoice_snapshot_version,company_snapshot) VALUES('GST-HISTORY1','COMPLETED',1,@request,'{}',100,0,0,100,'Synthetic setup customer',1,'{}') RETURNING sale_id",new{request=Guid.NewGuid()},historyTx);
            await c.ExecuteAsync("INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,discount_amount,tax_percentage,line_total,product_name_snapshot) VALUES(@historicalSale,@Product,@Variant,1,100,0,0,100,'Synthetic setup goods')",new{historicalSale,sku.Product,sku.Variant},historyTx);
            var documentId=await c.ExecuteScalarAsync<long>("SELECT public.sf_gst_capture_sale(@historicalSale,'27')",new{historicalSale},historyTx);await historyTx.CommitAsync();
            var historicalSnapshot=JsonSerializer.Serialize(await repo.DocumentAsync(documentId));
            var invoiceSnapshot=await c.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM sales s WHERE sale_id=@historicalSale",new{historicalSale});
            before=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text");
            structured.AddressLine2="Reviewed locality";await repo.SaveBusinessAsync(structured);
            var companyRepo=new CompanySettingsRepository(new DbConnectionFactory(config,scope));var company=await companyRepo.GetAsync();
            company.CompanyName="Updated company fallback";company.City="Mumbai";company.State="Maharashtra";company.PinCode="400001";company.MobileNumber="9876543210";company.Email="synthetic@example.test";company.GstNumber=null;company.PanNumber=null;
            await companyRepo.SaveAsync(company,1);
            Check((await companyRepo.GetGstIdentityAsync())?.RegistrationId==reg,"Company page effective identity follows the saved registration mapping");
            Check((await repo.SettingsAsync()).Registrations.Single().Address=="Synthetic registration address","Business edits preserve independent registration address");
            await repo.SaveMappingAsync(new(){MappingId=mapping,RegistrationId=reg,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=day.AddDays(-1),Status="INACTIVE"});
            var historyReview=await repo.SetupReadinessAsync(reg);await repo.ActivateSetupAsync(new(reg,historyReview.Fingerprint));
            Check(JsonSerializer.Serialize(await repo.DocumentAsync(documentId))==historicalSnapshot,"Company settings, structured address edits and activation preserve a populated historical GST snapshot");
            Check(await c.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM sales s WHERE sale_id=@historicalSale",new{historicalSale})==invoiceSnapshot,"Review and activation preserve the original posted invoice identity and financial fields");
            var audit=await repo.AuditAsync(new(){RegistrationId=reg,StoreId=1});Check(audit.Rows.Any()&&audit.Rows.All(r=>r.Entity=="gst_shop_mappings"),"Audit filters read actual direct shop-assignment history");
            await Reject(()=>repo.AuditAsync(new(){PageSize=0}),"Audit rejects invalid pagination");
            await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='GST_SETTINGS' AND a.action_code='VIEW' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true");
            var reader=new GstRepository(new DbConnectionFactory(config,new(){TenantId=1,UserId=2,StoreId=1,LocationId=1}));var limited=await reader.SettingsAsync();Check(limited.Registrations.Single().Gstin!.Contains('\u2022')&&limited.Businesses.Single(b=>b.BusinessId==business).Pan!.Contains('\u2022'),"Read-only settings responses mask GSTIN and PAN on the backend");
            Check(!JsonSerializer.Serialize(await reader.AuditAsync(new())).Contains("AAPFU0939F"),"Read-only audit payload masks tax identities");await Reject(()=>reader.SaveBusinessAsync(structured),"View-only user cannot edit structured legal-business address");await Reject(()=>reader.ActivateSetupAsync(new(reg,activated.Fingerprint)),"View-only user cannot activate setup");
            var store=await c.ExecuteScalarAsync<long>("INSERT INTO stores(store_code,store_name) VALUES('GST-SETUP-TWO','Synthetic second setup shop') RETURNING store_id");var location=await c.ExecuteScalarAsync<long>("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@store,'Synthetic stock','SALES_FLOOR') RETURNING location_id",new{store});scope.StoreId=store;scope.LocationId=location;
            Check(!(await repo.SetupReadinessAsync(reg)).CanActivate,"Readiness does not inherit another shop's mapping");await Reject(()=>repo.ActivateSetupAsync(new(reg,activated.Fingerprint)),"Another shop cannot activate using the first shop review");
            scope.TenantId=999;await Reject(()=>repo.SaveBusinessAsync(structured),"Cross-tenant structured address edit denied");await Reject(()=>repo.SetupReadinessAsync(reg),"Forged tenant cannot review setup");scope.TenantId=1;
            Check(await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text")==before,"Setup review and activation do not post sales, stock or payments");
            Console.WriteLine("GST guided setup database checks passed in a disposable clone.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    private static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception ex)when(ex is ValidationException or UnauthorizedAccessException or PostgresException){Console.WriteLine("PASS: "+label);return;}throw new Exception("Expected rejection: "+label);}
}
