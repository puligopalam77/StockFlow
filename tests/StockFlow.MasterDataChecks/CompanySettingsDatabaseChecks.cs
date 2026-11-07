using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class CompanySettingsDatabaseChecks
{
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var database="company_settings_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
            var factory=new DbConnectionFactory(config,scope);var repo=new CompanySettingsRepository(factory);
            var company=new CompanySettings{CompanyName="Synthetic company",Address="Original unsplit address",City="Mumbai",State="Maharashtra",PinCode="400001",MobileNumber="9876543210",Email="synthetic@example.test",InvoicePrefix="S-{YYYY}-"};
            await c.ExecuteAsync("UPDATE company_settings SET profile=profile||'{\"UnrelatedSavedFlag\":true}'::jsonb");
            var seq=await c.ExecuteScalarAsync<long>("SELECT last_value FROM invoice_number_seq");
            await repo.SaveAsync(company,1);company=await repo.GetAsync();company.InvoiceFooter="Updated footer";await repo.SaveAsync(company,1);
            Check((await repo.GetAsync()).Address=="Original unsplit address","Unrelated company edits preserve unsplit legacy address");
            Check(await c.ExecuteScalarAsync<bool>("SELECT (profile->>'UnrelatedSavedFlag')::boolean FROM company_settings"),"Company save preserves unrelated JSON settings");
            Check(await c.ExecuteScalarAsync<long>("SELECT last_value FROM invoice_number_seq")==seq,"Settings save never allocates an invoice number");
            company.AddressLine1="12 Explicit Street";company.AddressLine2=null;company.District="Urban District";await repo.SaveAsync(company,1);company=await repo.GetAsync();
            Check(company.AddressLine1=="12 Explicit Street"&&company.District=="Urban District"&&company.Address=="12 Explicit Street\nUrban District","Structured address persists in existing JSON profile and compatibility address");
            var tooLong=JsonSerializer.Deserialize<CompanySettings>(JsonSerializer.Serialize(company))!;tooLong.InvoicePrefix=new string('X',31);
            await Reject(()=>repo.SaveAsync(tooLong,1),"Overlong invoice prefix rejected before persistence");
            var invalid=JsonSerializer.Deserialize<CompanySettings>(JsonSerializer.Serialize(company))!;invalid.GstNumber="27AAPFU0939F1ZA";
            await Reject(()=>repo.SaveAsync(invalid,1),"Changed legacy GSTIN checksum is validated");
            invalid.GstNumber="29AAPFU0939F1ZV";await Reject(()=>repo.SaveAsync(invalid,1),"Conflicting legacy GST state or checksum rejected");
            await Reject(()=>repo.SaveAsync(company,2),"Forged company-save actor rejected");
            var reader=new CompanySettingsRepository(new DbConnectionFactory(config,new(){TenantId=1,UserId=2,StoreId=1,LocationId=1}));
            await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,false FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='COMPANY_SETTINGS' AND a.action_code='EDIT' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=false");
            await Reject(()=>reader.SaveAsync(company,2),"View-only user cannot write company details through repository");
            var customer=await new CustomerRepository(factory).CreateAsync(new(){CustomerName="Synthetic billing customer",MobileNumber="9861200044",Address="Synthetic customer address"});
            var sales=new SalesRepository(factory);
            CreateSaleRequest Sale()=>new(){LocationId=1,RequestId=Guid.NewGuid(),CustomerId=customer,PricesIncludeTax=false,Items=[new(){Quantity=1,SellingPrice=100,ManualItem=new(){Name="Synthetic settings test item",Mrp=100}}],Payments=[new(){PaymentMode="CASH",Amount=100}]};
            var ids=await Task.WhenAll(sales.CreateSaleAsync(Sale(),1),sales.CreateSaleAsync(Sale(),1));
            var numbers=(await c.QueryAsync<string>("SELECT invoice_number FROM sales WHERE sale_id=ANY(@ids)",new{ids})).ToArray();
            Check(numbers.Length==2&&numbers.Distinct().Count()==2&&numbers.All(n=>n.StartsWith("S-")),"Concurrent different sales use existing sequence and retain unique configured invoice numbers");
            var receipt=await c.QuerySingleAsync<(long Id,string Number)>("INSERT INTO purchase_receipts(purchase_id,received_date,created_by) SELECT purchase_id,CURRENT_DATE,1 FROM purchases WHERE tenant_id=1 AND location_id=1 ORDER BY purchase_id LIMIT 1 RETURNING receipt_id,receipt_number");
            var receiptHistory=await c.ExecuteScalarAsync<string>("SELECT to_jsonb(r)::text FROM purchase_receipts r WHERE receipt_id=@Id",new{receipt.Id});
            var history=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(s) ORDER BY sale_id)::text FROM sales s");
            var snapshot=await c.ExecuteScalarAsync<string>("SELECT company_snapshot::text FROM sales WHERE sale_id=@id",new{id=ids[0]});
            var totals=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT count(*) FROM purchase_receipts)::text");
            seq=await c.ExecuteScalarAsync<long>("SELECT last_value FROM invoice_number_seq");
            company.CompanyName="Updated company display";company.InvoicePrefix="NEW-{YYYY}-";company.ReceiptPrefix="RECEIPT-{YYYY}-";await repo.SaveAsync(company,1);
            Check(await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(s) ORDER BY sale_id)::text FROM sales s")==history&&await c.ExecuteScalarAsync<string>("SELECT company_snapshot::text FROM sales WHERE sale_id=@id",new{id=ids[0]})==snapshot,"Company and prefix changes preserve issued numbers and historical invoice snapshots");
            Check(await c.ExecuteScalarAsync<long>("SELECT last_value FROM invoice_number_seq")==seq&&await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT count(*) FROM purchase_receipts)::text")==totals,"Company saves do not allocate sequences, post stock/payments or create receipts");
            Check(await c.ExecuteScalarAsync<string>("SELECT to_jsonb(r)::text FROM purchase_receipts r WHERE receipt_id=@Id",new{receipt.Id})==receiptHistory,"Receipt-prefix changes preserve an issued receipt document and number");
            var futureReceipt=await c.ExecuteScalarAsync<string>("INSERT INTO purchase_receipts(purchase_id,received_date,created_by) SELECT purchase_id,CURRENT_DATE,1 FROM purchases WHERE tenant_id=1 AND location_id=1 ORDER BY purchase_id LIMIT 1 RETURNING receipt_number");
            Check(receipt.Number.StartsWith("GRN-")&&futureReceipt!.StartsWith("RECEIPT-")&&futureReceipt!=receipt.Number,"Future receipt uses saved prefix through the existing receipt trigger");
            var gst=new GstRepository(factory);var today=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");
            var business=await gst.SaveBusinessAsync(new(){LegalName="Authoritative GST business",Address="GST registered address",StateCode="27",PinCode="400001",Pan="AAPFU0939F"});
            var registration=await gst.SaveRegistrationAsync(new(){BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="27",Address="Independent GST premises",EffectiveFrom=today});
            await gst.SaveMappingAsync(new(){RegistrationId=registration,StoreId=1,BusinessProfileCode="ELECTRICAL",EffectiveFrom=today});
            var identity=await repo.GetGstIdentityAsync();Check(identity?.RegistrationId==registration&&identity.LegalName=="Authoritative GST business","Company screen reads the actual effective shop/profile GST mapping");
            Check(await reader.GetGstIdentityAsync() is null,"Company settings permission alone does not expose GST identity without GST view authority");
            company.LegalBusinessName="Independent fallback name";company.GstNumber=null;company.PanNumber=null;await repo.SaveAsync(company,1);
            Check((await repo.GetGstIdentityAsync())==identity,"Company fallback edits do not change effective legal identity or GSTIN");
            scope.StoreId=2;Check((await repo.GetAsync()).CompanyName==company.CompanyName,"Company settings retain existing company-wide sharing across shops");Check(await repo.GetGstIdentityAsync() is null,"GST identity does not inherit another shop mapping");
            scope.TenantId=999;await Reject(()=>repo.SaveAsync(company,1),"Cross-tenant company update denied");
            Console.WriteLine("Company settings database checks passed in a disposable clone.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private static async Task Reject(Func<Task> action,string message){try{await action();}catch(Exception ex)when(ex is ValidationException or UnauthorizedAccessException or PostgresException){Check(true,message);return;}throw new Exception("Expected rejection: "+message);}
}
