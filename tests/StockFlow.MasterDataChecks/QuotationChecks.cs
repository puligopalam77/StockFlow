using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class QuotationChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    private static async Task Reject(Func<Task> action,string message)
    {
        try{await action();}catch(Exception ex)when(ex is ValidationException or UnauthorizedAccessException or KeyNotFoundException || ex is PostgresException p&&p.SqlState is "23514" or "42501" or "40001"){Check(true,message);return;}
        throw new Exception("FAIL: "+message);
    }
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var database="quotations_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var setup=new NpgsqlConnection(host+database);await setup.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
            await setup.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false)");
            var profile=await setup.ExecuteScalarAsync<string>("SELECT business_profile_code FROM tenants WHERE tenant_id=1");await setup.ExecuteAsync("SELECT set_config('app.business_profile_code',@profile,false)",new{profile});
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode=profile!,ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var factory=new DbConnectionFactory(config,scope);var sales=new SalesRepository(factory);var repo=new QuotationRepository(factory,sales,Options.Create(new ShopTimeOptions()),TimeProvider.System);
            var today=new ShopTimeOptions().Today(TimeProvider.System);
            var customer=await new CustomerRepository(factory).CreateAsync(new(){CustomerName="=Quotation fixture",MobileNumber="9861200991"});
            var sku=await setup.QuerySingleAsync<(int Product,long Variant)>("SELECT p.product_id,v.variant_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.identifier_profile=@profile AND p.is_active AND v.is_active AND NOT v.is_serial_tracked ORDER BY p.product_id,v.variant_id LIMIT 1",new{profile});
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=100,mrp=1000 WHERE variant_id=@Variant",new{sku.Variant});
            await new StockIntakeRepository(factory).PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=today,Items=[new(){ProductId=sku.Product,VariantId=sku.Variant,Quantity=6}]},true,1);
            var inclusive=(await new CompanySettingsRepository(factory).GetAsync()).DefaultTaxMode=="INCLUSIVE";
            SaveQuotationRequest New()=>new(){QuotationId=Guid.NewGuid(),OperationId=Guid.NewGuid(),DocumentDate=today,ValidUntil=today.AddDays(7),Terms="Original commercial terms",Request=new(){RequestId=Guid.NewGuid(),LocationId=1,CustomerId=customer,PricesIncludeTax=inclusive,Items=[new(){ProductId=sku.Product,VariantId=sku.Variant,Quantity=2,SellingPrice=100}]}};
            async Task<string> Financial()=>(await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM gst_documents)::text"))!;
            var before=await Financial();var save=New();var versions=await Task.WhenAll(repo.SaveAsync(save),repo.SaveAsync(save));Check(versions.All(v=>v==1),"concurrent identical create reuses one immutable quotation revision");
            var detail=await repo.GetAsync(save.QuotationId);Check(detail.Header.QuotationNumber.StartsWith("QTN-")&&detail.Header.CurrentRevision==1&&detail.Header.Status=="DRAFT","persisted sequential reference and initial draft state");
            Check(await Financial()==before,"quotation creation posts no sale, payment, tax invoice or stock movement");
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=130 WHERE variant_id=@Variant",new{sku.Variant});
            Check(await repo.SaveAsync(save)==1,"lost-response retry retrieves original revision even after catalog price changes");
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=100 WHERE variant_id=@Variant",new{sku.Variant});
            var revise=JsonSerializer.Deserialize<SaveQuotationRequest>(JsonSerializer.Serialize(save))!;revise.ExpectedVersion=1;revise.OperationId=Guid.NewGuid();revise.Terms="Revised commercial terms";
            Check(await repo.SaveAsync(revise)==2,"edit creates a second immutable commercial revision");
            Check((await repo.GetAsync(save.QuotationId,1)).Selected.Body.GetProperty("terms").GetString()=="Original commercial terms","previous commercial terms remain unchanged");
            await Reject(()=>repo.SaveAsync(withNewOperation()),"stale version cannot overwrite a shared quotation");
            SaveQuotationRequest withNewOperation(){var r=New();r.QuotationId=save.QuotationId;r.ExpectedVersion=1;return r;}
            var sent=new QuotationStateRequest(Guid.NewGuid(),2,"SENT","Owner confirms PDF was manually shared with customer");
            Check(await repo.StateAsync(save.QuotationId,sent)==3&&await repo.StateAsync(save.QuotationId,sent)==3,"manual sent lifecycle decision is idempotent");
            await repo.StateAsync(save.QuotationId,new(Guid.NewGuid(),3,"ACCEPTED","Customer acceptance confirmed at counter"));
            Check(await Financial()==before,"sending and accepting a quotation does not reserve stock or post money");
            await new CompanySettingsRepository(factory).SaveTaxModeAsync(inclusive?"EXCLUSIVE":"INCLUSIVE",1);
            await Reject(()=>repo.CheckoutAsync(save.QuotationId,new(4,2)),"changed shop tax mode requires a reviewed quotation revision before conversion");
            await new CompanySettingsRepository(factory).SaveTaxModeAsync(inclusive?"INCLUSIVE":"EXCLUSIVE",1);
            await Reject(()=>repo.CheckoutAsync(save.QuotationId,new(3,1)),"old revision/version cannot convert");
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=125 WHERE variant_id=@Variant",new{sku.Variant});
            var checkout=JsonSerializer.SerializeToElement(await repo.CheckoutAsync(save.QuotationId,new(4,2)),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(checkout.GetProperty("snapshot").GetProperty("cart")[0].GetProperty("sellingPrice").GetDecimal()==125&&checkout.GetProperty("changes").GetArrayLength()>0,"conversion loads today's catalog price with an explicit change notice");
            Check(await Financial()==before&&(await repo.GetAsync(save.QuotationId)).Header.Status=="ACCEPTED","opening New Sale does not mark converted or change stock");
            CreateSaleRequest Sale(Guid requestId)
            {
                var r=detail.Selected.Body.GetProperty("request").Deserialize<CreateSaleRequest>()!;
                r.RequestId=requestId;r.Items[0].SellingPrice=125;r.SourceQuotationId=save.QuotationId;r.SourceQuotationRevision=2;r.SourceQuotationVersion=4;r.CanOverridePrice=true;r.CanDiscount=true;r.CanApproveDiscount=true;return r;
            }
            var invoiceRequest=Sale(Guid.NewGuid());invoiceRequest.Payments=[new(){Amount=(await sales.QuoteAsync(invoiceRequest)).TotalAmount}];
            await setup.ExecuteAsync("CREATE FUNCTION public.fixture_quotation_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic late checkout failure' USING ERRCODE='23514'; END $$; CREATE TRIGGER fixture_quotation_fail BEFORE INSERT ON public.payments FOR EACH ROW EXECUTE FUNCTION public.fixture_quotation_fail()");
            await Reject(()=>sales.CreateSaleAsync(invoiceRequest,1),"late checkout failure rolls back quotation conversion");
            Check((await repo.GetAsync(save.QuotationId)).Header.Status=="ACCEPTED"&&await Financial()==before,"failed sale leaves quotation, stock and money unchanged");
            await setup.ExecuteAsync("DROP TRIGGER fixture_quotation_fail ON public.payments; DROP FUNCTION public.fixture_quotation_fail()");
            var posted=await Task.WhenAll(sales.CreateSaleAsync(invoiceRequest,1),sales.CreateSaleAsync(invoiceRequest,1));Check(posted[0]==posted[1],"duplicate concurrent checkout request creates one invoice");
            var converted=await repo.GetAsync(save.QuotationId);Check(converted.Header.Status=="CONVERTED"&&converted.Header.SaleId==posted[0],"successful existing sale transaction marks converted and links original revision");
            var forbiddenRevision=withNewOperation();forbiddenRevision.ExpectedVersion=converted.Header.Version;forbiddenRevision.Request.Items[0].SellingPrice=125;
            await Reject(()=>repo.SaveAsync(forbiddenRevision),"converted quotation cannot be revised");
            var second=Sale(Guid.NewGuid());second.Payments=invoiceRequest.Payments;await Reject(()=>sales.CreateSaleAsync(second,1),"a second independent checkout cannot convert the same quotation again");
            await setup.ExecuteAsync("UPDATE product_variants SET selling_price=100 WHERE variant_id=@Variant",new{sku.Variant});
            var expiry=New();expiry.DocumentDate=today.AddDays(-3);expiry.ValidUntil=today.AddDays(-1);await repo.SaveAsync(expiry);Check((await repo.GetAsync(expiry.QuotationId)).Header.Status=="EXPIRED","expiry is effective even without a background job");
            await Reject(()=>repo.CheckoutAsync(expiry.QuotationId,new(1,1)),"expired quotation cannot open checkout");await repo.StateAsync(expiry.QuotationId,new(Guid.NewGuid(),1,"EXPIRED","Validity elapsed without acceptance"));
            var rejected=New();await repo.SaveAsync(rejected);await repo.StateAsync(rejected.QuotationId,new(Guid.NewGuid(),1,"SENT","Confirmed hand delivery of printed estimate"));await repo.StateAsync(rejected.QuotationId,new(Guid.NewGuid(),2,"REJECTED","Customer declined this offer"));
            rejected.ExpectedVersion=3;rejected.OperationId=Guid.NewGuid();await repo.SaveAsync(rejected);Check((await repo.GetAsync(rejected.QuotationId)).Header.Status=="DRAFT","revising a rejected quotation returns to Draft with history");
            await repo.StateAsync(rejected.QuotationId,new(Guid.NewGuid(),4,"CANCELLED","Owner withdrew the new offer"));await Reject(()=>repo.CheckoutAsync(rejected.QuotationId,new(5,2)),"cancelled quotations cannot convert");
            var many=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>repo.SaveAsync(New())));var page=await repo.ListAsync(null,null,null,null,1,20);Check(page.Total==9&&page.Rows.Select(r=>r.QuotationNumber).Distinct().Count()==9,"concurrent number allocation and paged listing retain unique shop references");
            async Task Accept(SaveQuotationRequest value){await repo.SaveAsync(value);await repo.StateAsync(value.QuotationId,new(Guid.NewGuid(),1,"SENT","Confirmed manual delivery"));await repo.StateAsync(value.QuotationId,new(Guid.NewGuid(),2,"ACCEPTED","Customer acceptance recorded"));}
            await using var stockConnection=factory.CreateConnection();
            var available=await stockConnection.ExecuteScalarAsync<int>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@Variant",new{sku.Variant});
            var unavailable=New();unavailable.Request.Items[0].Quantity=available+1;var stockBaseline=await Financial();await Accept(unavailable);
            await Reject(()=>repo.CheckoutAsync(unavailable.QuotationId,new(3,1)),"accepted quotation fails safely when current stock is insufficient");Check(await Financial()==stockBaseline,"insufficient-stock conversion leaves all stock and money sources unchanged");
            var oldTax=await setup.ExecuteScalarAsync<decimal>("SELECT tax_percentage FROM product_variants WHERE variant_id=@Variant",new{sku.Variant});var taxChanged=New();await Accept(taxChanged);
            await setup.ExecuteAsync("UPDATE product_variants SET tax_percentage=@rate WHERE variant_id=@Variant",new{sku.Variant,rate=oldTax==12?5:12});
            var taxCheckout=JsonSerializer.SerializeToElement(await repo.CheckoutAsync(taxChanged.QuotationId,new(3,1)),new JsonSerializerOptions(JsonSerializerDefaults.Web));Check(taxCheckout.GetProperty("changes").EnumerateArray().Any(x=>x.GetString()!.Contains("tax changed")),"effective tax changes are recalculated and explicitly disclosed before checkout");
            await setup.ExecuteAsync("UPDATE product_variants SET tax_percentage=@rate WHERE variant_id=@Variant",new{sku.Variant,rate=oldTax});
            var manual=New();manual.Request.Items=[new(){Quantity=1,SellingPrice=20,ManualItem=new(){Name="Non-catalog quotation line",Mrp=30,TaxRuleId=0}}];stockBaseline=await Financial();await repo.SaveAsync(manual);
            Check((await repo.GetAsync(manual.QuotationId)).Selected.Body.GetProperty("lines")[0].GetProperty("Name").GetString()=="Non-catalog quotation line"&&await Financial()==stockBaseline,"controlled manual quotation lines persist without catalog or financial posting");
            var racing=New();racing.Request.Items[0].Quantity=1;await Accept(racing);
            async Task<long?> Race(){var r=(await repo.GetAsync(racing.QuotationId)).Selected.Body.GetProperty("request").Deserialize<CreateSaleRequest>()!;r.RequestId=Guid.NewGuid();r.SourceQuotationId=racing.QuotationId;r.SourceQuotationRevision=1;r.SourceQuotationVersion=3;r.CanDiscount=true;r.CanOverridePrice=true;r.CanApproveDiscount=true;r.Payments=[new(){Amount=(await sales.QuoteAsync(r)).TotalAmount}];try{return await sales.CreateSaleAsync(r,1);}catch(Exception ex)when(ex is ValidationException || ex is PostgresException p&&p.SqlState=="23514"){return null;}}
            var race=await Task.WhenAll(Race(),Race());Check(race.Count(x=>x.HasValue)==1,"independent concurrent conversion requests finalize exactly one invoice");
            var denied=new QuotationRepository(new DbConnectionFactory(config,new(){TenantId=1,UserId=2,StoreId=1,LocationId=1,BusinessProfileCode=profile!,ProfileDataVisibilityAvailable=true}),sales,Options.Create(new ShopTimeOptions()),TimeProvider.System);
            await Reject(()=>denied.GetAsync(save.QuotationId),"restricted staff cannot read quotation records");
            await setup.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='QUOTATIONS' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true; INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,false FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='SALES' AND a.action_code IN('DISCOUNT','PRICE_OVERRIDE') ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=false");
            var restrictedFactory=new DbConnectionFactory(config,new(){TenantId=1,UserId=2,StoreId=1,LocationId=1,BusinessProfileCode=profile!,ProfileDataVisibilityAvailable=true});
            var restricted=new QuotationRepository(restrictedFactory,new SalesRepository(restrictedFactory),Options.Create(new ShopTimeOptions()),TimeProvider.System);
            Check((await restricted.GetAsync(save.QuotationId)).Header.QuotationId==save.QuotationId&&await restricted.SaveAsync(New())==1,"explicitly authorized staff can read and create ordinary quotations before financial override denials are tested");
            var unauthorizedDiscount=New();unauthorizedDiscount.Request.Items[0].DiscountPercent=5;unauthorizedDiscount.Request.Items[0].AdjustmentReason="Proposed customer discount";
            await Reject(()=>restricted.SaveAsync(unauthorizedDiscount),"quotation permission does not grant the existing sales discount permission");
            var unauthorizedPrice=New();unauthorizedPrice.Request.Items[0].SellingPrice=90;unauthorizedPrice.Request.Items[0].PriceAdjustmentReason="Proposed special selling price";
            await Reject(()=>restricted.SaveAsync(unauthorizedPrice),"quotation permission does not grant the existing sales price-override permission");
            var other=await setup.QuerySingleAsync<(long Store,long Location)>("WITH s AS(INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'QUOTE-OTHER','Quotation test other shop') RETURNING store_id) INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) SELECT 1,store_id,'Other stock','SALES_FLOOR' FROM s RETURNING store_id,location_id");
            scope.StoreId=other.Store;scope.LocationId=other.Location;await Reject(()=>repo.GetAsync(save.QuotationId),"different shop with the same profile cannot read original quotation");
            var forged=New();forged.StoreId=1;await Reject(()=>repo.SaveAsync(forged),"forged store identifier rejected before any quotation write");
            scope.BusinessProfileCode=profile==BusinessProfiles.Electrical?BusinessProfiles.BikeAccessories:BusinessProfiles.Electrical;await Reject(()=>repo.GetAsync(save.QuotationId),"different shop/profile context cannot access original quotation");
            scope.TenantId=999999;await Reject(()=>repo.GetAsync(save.QuotationId),"forged tenant cannot read original quotation");scope.TenantId=1;scope.StoreId=1;scope.LocationId=1;scope.BusinessProfileCode=profile!;
            await using(var runtime=factory.CreateConnection())Check(!await runtime.ExecuteScalarAsync<bool>("SELECT has_table_privilege(current_user,'public.quotations','INSERT') OR has_table_privilege(current_user,'public.quotation_revisions','UPDATE')"),"runtime role cannot bypass guarded writer or rewrite revisions");
            using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IQuotationRepository>();s.AddSingleton<IQuotationRepository>(repo);}));using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
            var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
            using(var withoutCsrf=web.CreateClient(new(){AllowAutoRedirect=false})){withoutCsrf.DefaultRequestHeaders.Add("Test-Role","Admin");Check((await withoutCsrf.PostAsJsonAsync("/api/quotations",New())).StatusCode==HttpStatusCode.BadRequest,"quotation mutation rejects requests without antiforgery token");}
            var apiSave=New();var response=await client.PostAsJsonAsync("/api/quotations",apiSave);Check(response.IsSuccessStatusCode,"actual quotation API persists with existing antiforgery and permission middleware: "+await response.Content.ReadAsStringAsync());
            Check((await client.GetAsync("/api/quotations?status=DRAFT&page=1&pageSize=2")).IsSuccessStatusCode,"actual list API enforces supported filters/pagination");
            var exported=await client.GetStringAsync("/api/quotations/export");Check(exported.Contains("'=Quotation fixture"),"quotation CSV neutralizes spreadsheet formulas");
            Directory.CreateDirectory("tests/bin/quotation-fixtures");
            foreach(var route in new[]{"/Quotations","/Quotations/Create","/Quotations/Edit?id="+apiSave.QuotationId,"/Quotations/Details?id="+save.QuotationId,"/Quotations/Print?id="+save.QuotationId+"&revision=1"}){var html=await client.GetStringAsync(route);var name=route.StartsWith("/Quotations/Print")?"Print":route.Split('?')[0].Split('/').Last();await File.WriteAllTextAsync("tests/bin/quotation-fixtures/"+name+".html",html);if(name=="Print")Check(html.Contains("Original commercial terms")&&!html.Contains("Revised commercial terms"),"actual Razor print renders selected immutable revision rather than latest terms");}
            client.DefaultRequestHeaders.Add("Test-Deny","QUOTATIONS");Check((await client.GetAsync("/api/quotations")).StatusCode==HttpStatusCode.Forbidden,"API rejects unauthorized module access before repository");
            Console.WriteLine("Quotation checks passed in a disposable database. Application database unchanged.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
}
