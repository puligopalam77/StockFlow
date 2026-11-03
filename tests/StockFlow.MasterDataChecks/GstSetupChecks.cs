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
            foreach(var file in Directory.GetFiles(Path.Combine(root!.FullName,"database/migrations"),"*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
            var before=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,ProfileDataVisibilityAvailable=true};var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();var repo=new GstRepository(new DbConnectionFactory(config,scope));
            var day=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");
            GstBusiness Business()=>new(){LegalName="Synthetic setup business",Address="Synthetic legal address",StateCode="27",PinCode="400001",Pan="AAPFU0939F"};
            var businesses=await Task.WhenAll(repo.SaveBusinessAsync(Business()),repo.SaveBusinessAsync(Business()));Check(businesses.Distinct().Count()==1,"Concurrent identical business creates reuse one saved record");var business=businesses[0];
            var registration=new GstRegistration{BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="27",Address="Synthetic registration address",EffectiveFrom=day.AddDays(-1)};
            var reg=await repo.SaveRegistrationAsync(registration);Check(await repo.SaveRegistrationAsync(registration)==reg,"Identical registration retry reuses saved identity and date");
            var map=new GstMapping{RegistrationId=reg,BusinessProfileCode="ELECTRICAL",StoreId=1,EffectiveFrom=day.AddDays(-1),Status="INACTIVE"};var mapping=await repo.SaveMappingAsync(map);Check(await repo.SaveMappingAsync(map)==mapping,"Identical assignment retry reuses saved row");
            var initial=await repo.SetupReadinessAsync(reg);Check(!initial.CanActivate&&!initial.Ready&&initial.Checks.Any(x=>x.Key=="taxes"&&x.Status=="error"),"Readiness uses backend product tax coverage rather than visited steps");
            await Reject(()=>repo.ActivateSetupAsync(new(reg,initial.Fingerprint)),"Incomplete setup cannot activate");
            long firstTax=0;foreach(var product in (await repo.SettingsAsync()).Products)
            {
                var tax=new GstTaxConfiguration{ProductId=product.ProductId,HsnSac="610910",Uqc="NOS",TaxRate=0,EffectiveFrom=day.AddDays(-1),ReviewReference="Synthetic reviewed test classification"};
                var id=await repo.SaveTaxAsync(tax);Check(await repo.SaveTaxAsync(tax)==id,"Identical product tax version retry does not create an overlapping duplicate");if(firstTax==0)firstTax=id;
            }
            var ready=await repo.SetupReadinessAsync(reg);Check(ready.CanActivate&&!ready.Ready,"Saved inactive assignment can activate only after valid saved tax versions");
            await repo.SaveTaxAsync(new(){TaxConfigId=firstTax,ProductId=(await repo.SettingsAsync()).Taxes.First(t=>t.TaxConfigId==firstTax).ProductId,HsnSac="610910",Uqc="NOS",EffectiveFrom=day.AddDays(-1),EffectiveTo=day.AddDays(10),ReviewReference="Synthetic reviewed test classification"});
            await Reject(()=>repo.ActivateSetupAsync(new(reg,ready.Fingerprint)),"Changed configuration invalidates a stale activation review");
            ready=await repo.SetupReadinessAsync(reg);var activated=await repo.ActivateSetupAsync(new(reg,ready.Fingerprint));Check(activated.Ready,"Activation uses existing mapping ACTIVE status in a transaction");
            var auditCount=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_audit_history");await repo.ActivateSetupAsync(new(reg,ready.Fingerprint));Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_audit_history")==auditCount,"Activation retry is a no-op without duplicate audit writes");
            var audit=await repo.AuditAsync(new(){RegistrationId=reg,StoreId=1});Check(audit.Rows.Any()&&audit.Rows.All(r=>r.Entity=="gst_shop_mappings"),"Audit filters read actual direct shop-assignment history");
            await Reject(()=>repo.AuditAsync(new(){PageSize=0}),"Audit rejects invalid pagination");
            await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='GST_SETTINGS' AND a.action_code='VIEW' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true");
            var reader=new GstRepository(new DbConnectionFactory(config,new(){TenantId=1,UserId=2,StoreId=1,LocationId=1}));var limited=await reader.SettingsAsync();Check(limited.Registrations.Single().Gstin!.Contains('•')&&limited.Businesses.Single().Pan!.Contains('•'),"Read-only settings responses mask GSTIN and PAN on the backend");
            Check(!JsonSerializer.Serialize(await reader.AuditAsync(new())).Contains("AAPFU0939F"),"Read-only audit payload masks tax identities");await Reject(()=>reader.ActivateSetupAsync(new(reg,activated.Fingerprint)),"View-only user cannot activate setup");
            var store=await c.ExecuteScalarAsync<long>("INSERT INTO stores(store_code,store_name) VALUES('GST-SETUP-TWO','Synthetic second setup shop') RETURNING store_id");var location=await c.ExecuteScalarAsync<long>("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@store,'Synthetic stock','SALES_FLOOR') RETURNING location_id",new{store});scope.StoreId=store;scope.LocationId=location;
            Check(!(await repo.SetupReadinessAsync(reg)).CanActivate,"Readiness does not inherit another shop's mapping");await Reject(()=>repo.ActivateSetupAsync(new(reg,activated.Fingerprint)),"Another shop cannot activate using the first shop review");
            scope.TenantId=999;await Reject(()=>repo.SetupReadinessAsync(reg),"Forged tenant cannot review setup");scope.TenantId=1;
            Check(await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text")==before,"Setup review and activation do not post sales, stock or payments");
            Console.WriteLine("GST guided setup database checks passed in a disposable clone.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    private static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception ex)when(ex is ValidationException or UnauthorizedAccessException or PostgresException){Console.WriteLine("PASS: "+label);return;}throw new Exception("Expected rejection: "+label);}
}
