using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Npgsql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class PrivateShopExpenseChecks
{
    const string Host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    static async Task Reject(Func<Task> action,string name){try{await action();}catch(Exception e)when(e is UnauthorizedAccessException or KeyNotFoundException or System.ComponentModel.DataAnnotations.ValidationException || e is PostgresException p&&p.SqlState is "42501" or "23514" or "23505" or "23503" or "40001"){Check(true,name);return;}throw new Exception("FAIL: "+name);}
    public static async Task Run(bool expenses)
    {
        var db="private_expenses_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(Host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
        try
        {
            await using var setup=new NpgsqlConnection(Host+db);await setup.OpenAsync();
            foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(f));
            await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1; UPDATE users SET must_change_password=false WHERE tenant_id=1 AND user_id=1; UPDATE user_action_permissions SET is_allowed=true WHERE tenant_id=1 AND user_id=1; SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",IsOwner=true,CanReviewStores=true,ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true,BusinessProfileConfigurationAvailable=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",Host+db}}).Build();var factory=new DbConnectionFactory(config,scope);
            var other=await setup.QuerySingleAsync<(long Store,long Location)>("WITH s AS(INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'PRIVATE-TWO','Private shop two') RETURNING store_id) INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) SELECT 1,store_id,'Private two counter','SALES_FLOOR' FROM s RETURNING store_id,location_id");
            using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{
                s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                s.RemoveAll<DbConnectionFactory>();s.AddScoped(sp=>new DbConnectionFactory(config,sp.GetRequiredService<BusinessScope>()));
                s.RemoveAll<IBranchRepository>();s.AddScoped<IBranchRepository,BranchRepository>();s.RemoveAll<ISecurityRepository>();s.AddScoped<ISecurityRepository,SecurityRepository>();
                s.RemoveAll<ICustomerRepository>();s.AddScoped<ICustomerRepository,CustomerRepository>();s.RemoveAll<IVariantRepository>();s.AddScoped<IVariantRepository,VariantRepository>();
                s.RemoveAll<IProductRepository>();s.AddScoped<IProductRepository,ProductRepository>();s.RemoveAll<IBusinessProfileRepository>();s.AddScoped<IBusinessProfileRepository,BusinessProfileRepository>();
                s.RemoveAll<ICashClosingRepository>();s.AddScoped<ICashClosingRepository,CashClosingRepository>();
                s.RemoveAll<IInvoiceRepository>();s.AddScoped<IInvoiceRepository,InvoiceRepository>();
            }));
            using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
            client.DefaultRequestHeaders.Add("Test-Version",(await setup.ExecuteScalarAsync<long>("SELECT security_version FROM users WHERE user_id=1")).ToString());
            var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
            if(expenses)await Expenses(setup,factory,scope,client,other);else await Shops(setup,factory,scope,client,other);
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
        if(!expenses)await Quarantine();
    }
    static async Task Shops(NpgsqlConnection setup,DbConnectionFactory factory,BusinessScope scope,HttpClient client,(long Store,long Location) other)
    {
        var categories=new CategoryRepository(factory);var brands=new BrandRepository(factory);var customers=new CustomerRepository(factory);var suppliers=new SupplierRepository(factory);var variants=new VariantRepository(factory);
        var cat=await categories.CreateAsync(new(){CategoryName="Private accessories",BusinessProfileCode="ELECTRICAL",IsActive=true});
        var brand=await brands.CreateAsync(new(){BrandName="Private manufacturer",IsActive=true,CategoryIds=[cat]});
        var customer=await customers.CreateAsync(new(){CustomerName="PRIVATE-CUSTOMER",MobileNumber="9876500101"});var supplier=await suppliers.CreateAsync(new(){SupplierName="PRIVATE-SUPPLIER",CountryCode="IN"});
        await using var runtime=factory.CreateConnection();var registration=Guid.NewGuid();var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@cat,@registration,NULL)",new{cat,registration});
        var product=await new ProductService(new ProductRepository(factory),categories,brands,scope,null,variants).CreateAsync(new(){RegistrationRequestId=registration,ParentProductCode=code,ProductName="Private product",CategoryId=cat,BrandId=brand,TrackingType="QUANTITY",PurchasePrice=40,SellingPrice=100});var variant=(await variants.GetAsync(product,null,false)).Single().VariantId;
        await new StockIntakeRepository(factory).PostAsync(new(){RequestId=Guid.NewGuid(),LocationId=1,ReceivedDate=DateTime.Today,Items=[new(){ProductId=product,VariantId=variant,Quantity=5}]},true,1);
        Check((await new ScanRepository(factory).FindByCodeAsync(code!))?.ProductId==product,"Private shop one exact SKU resolves its own product");
        scope.StoreId=other.Store;scope.LocationId=other.Location;
        Check(await categories.GetByIdAsync(cat)==null&&await brands.GetByIdAsync(brand)==null&&await customers.GetByIdAsync(customer)==null&&await suppliers.GetByIdAsync(supplier)==null,"Same-profile shop two cannot read shop one category/brand/customer/supplier IDs");
        Check(!(await variants.SearchForSaleAsync(code!)).Any()&&await new ScanRepository(factory).FindByCodeAsync(code!)==null,"SKU search and scanner lookup cannot leak another private shop's product/prices");
        await using(var privateTwo=factory.CreateConnection())await Reject(()=>privateTwo.ExecuteAsync("SELECT sf_reserve_product_code(@cat,@request,NULL)",new{cat,request=Guid.NewGuid()}),"Definer code reservation rejects another shop category ID");
        var catTwo=await categories.CreateAsync(new(){CategoryName="Private accessories",BusinessProfileCode="ELECTRICAL",IsActive=true});
        var brandTwo=await brands.CreateAsync(new(){BrandName="Private manufacturer",IsActive=true,CategoryIds=[catTwo]});
        var customerTwo=await customers.CreateAsync(new(){CustomerName="SECOND-SHOP-CUSTOMER",MobileNumber="9876500101"},true);
        Check(catTwo!=cat&&brandTwo!=brand&&customerTwo!=customer,"Independent same-profile shops register their own names/phone without merged master IDs");
        client.DefaultRequestHeaders.Add("Test-Store",other.Store.ToString());client.DefaultRequestHeaders.Add("Test-Location",other.Location.ToString());
        Check(!(await client.GetStringAsync("/api/customers?term=PRIVATE-CUSTOMER")).Contains("PRIVATE-CUSTOMER"),"Actual branch/security-backed customer API search cannot expose private shop one");
        Check(!(await client.GetStringAsync("/api/products/variants?billing=true&term="+code)).Contains(code!),"Actual variant search API cannot expose private SKU/pricing");
        client.DefaultRequestHeaders.Remove("Test-Store");client.DefaultRequestHeaders.Remove("Test-Location");scope.StoreId=1;scope.LocationId=1;
        async Task Share(string kind,long master,bool enabled=true){var r=await client.PostAsJsonAsync("/api/shopmasters",new{kind,id=master,storeId=other.Store,enabled,resolve=false,reason="Explicit owner-approved test sharing"});Check(r.IsSuccessStatusCode,$"Authorized {kind} sharing API: "+await r.Content.ReadAsStringAsync());}
        await Share("CATEGORY",cat);await Share("BRAND",brand);await Share("PRODUCT",product);
        scope.StoreId=other.Store;scope.LocationId=other.Location;
        Check((await variants.SearchForSaleAsync(code!)).Single().SellingPrice==100,"Explicitly shared product is readable at its original saved catalog price");
        await Reject(()=>categories.CreateAsync(new(){CategoryName="Forbidden shared child",BusinessProfileCode="ELECTRICAL",ParentCategoryId=cat,IsActive=true}),"Read-only category sharing cannot create private children in another shop's hierarchy");
        await Reject(()=>new GstRepository(factory).SaveTaxAsync(new(){ProductId=product,VariantId=variant,HsnSac="610910",Uqc="NOS",TaxRate=12,EffectiveFrom=DateTime.Today,ReviewReference="Attempted shared product tax rewrite"}),"Read-only shared product cannot have its tax configuration rewritten from the target shop");
        await using(var shared=factory.CreateConnection())Check(await shared.ExecuteAsync("UPDATE product_variants SET selling_price=1 WHERE variant_id=@variant",new{variant})==0,"Shared catalog prices cannot be changed from target shop");
        Check(await customers.GetByIdAsync(customer)==null&&await suppliers.GetByIdAsync(supplier)==null,"Product sharing does not implicitly share private contacts");
        scope.StoreId=1;scope.LocationId=1;await Share("PRODUCT",product,false);scope.StoreId=other.Store;scope.LocationId=other.Location;
        Check(!(await variants.SearchForSaleAsync(code!)).Any(),"Revoking product sharing removes target search access");
        scope.StoreId=0;scope.LocationId=0;scope.Consolidated=true;
        Check((await customers.GetAllAsync()).Any(x=>x.CustomerId==customer)&&(await customers.GetAllAsync()).Any(x=>x.CustomerId==customerTwo),"Authorized tenant owner consolidated master read intersects tenant boundary");
        scope.Consolidated=false;scope.StoreId=1;scope.LocationId=1;
        var ownLocation=await setup.ExecuteScalarAsync<long>("INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) VALUES(1,1,'Authorized second location','STOCKROOM') RETURNING location_id");
        scope.LocationId=ownLocation;
        Check(await customers.GetByIdAsync(customer)!=null&&(await variants.GetAsync(product,null,false)).Single().CurrentStock==0,"Authorized second location in the same shop shares master identity but not another location's stock");scope.LocationId=1;
        var reporting=new ReportingRepository(factory,scope);scope.StoreId=0;scope.LocationId=0;scope.Consolidated=true;
        var beforeConsolidated=(await reporting.FinancialAsync(DateTime.Today,DateTime.Today)).Sum(r=>r.SalesValue);scope.StoreId=1;scope.LocationId=1;scope.Consolidated=false;
        var gst=new GstRepository(factory);var day=new ShopTimeOptions().Today(TimeProvider.System);
        var business=await gst.SaveBusinessAsync(new(){LegalName="Synthetic shared registration business",Address="Synthetic office",StateCode="29",PinCode="560001"});
        var reg=await gst.SaveRegistrationAsync(new(){BusinessId=business,Gstin="29ABCDE1234F1ZW",StateCode="29",Address="Synthetic registration",EffectiveFrom=day});
        await gst.SaveMappingAsync(new(){RegistrationId=reg,StoreId=1,BusinessProfileCode="ELECTRICAL",EffectiveFrom=day});
        await gst.SaveMappingAsync(new(){RegistrationId=reg,StoreId=other.Store,BusinessProfileCode="ELECTRICAL",EffectiveFrom=day});
        async Task<long> GstSale(int pid,long vid,int cid,long location){await gst.SaveTaxAsync(new(){ProductId=pid,VariantId=vid,HsnSac="610910",Uqc="NOS",TaxRate=5,EffectiveFrom=day,ReviewReference="Synthetic reviewed fixture; not statutory rate advice"});var r=new CreateSaleRequest{RequestId=Guid.NewGuid(),CustomerId=cid,LocationId=location,GstPlaceOfSupplyStateCode="29",Items=[new(){ProductId=pid,VariantId=vid,Quantity=1,SellingPrice=100}]};var sales=new SalesRepository(factory);r.Payments=[new(){PaymentMode="CASH",Amount=(await sales.QuoteAsync(r)).TotalAmount}];return await sales.CreateSaleAsync(r,1);}
        var firstGstSale=await GstSale(product,variant,customer,1);
        scope.StoreId=other.Store;scope.LocationId=other.Location;
        long secondGstSale;await using(var second=factory.CreateConnection()){
            var request=Guid.NewGuid();var nextCode=await second.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@catTwo,@request,NULL)",new{catTwo,request});
            var p2=await new ProductService(new ProductRepository(factory),categories,brands,scope,null,variants).CreateAsync(new(){RegistrationRequestId=request,ParentProductCode=nextCode,ProductName="Second private GST product",CategoryId=catTwo,BrandId=brandTwo,TrackingType="QUANTITY",SellingPrice=100,PurchasePrice=40});var v2=(await variants.GetAsync(p2,null,false)).Single().VariantId;
            await new StockIntakeRepository(factory).PostAsync(new(){RequestId=Guid.NewGuid(),LocationId=other.Location,ReceivedDate=day,Items=[new(){ProductId=p2,VariantId=v2,Quantity=2}]},true,1);secondGstSale=await GstSale(p2,v2,customerTwo,other.Location);
        }
        Check(await new InvoiceRepository(factory).GetInvoiceAsync(firstGstSale)==null,"GST registration-wide permission does not grant another shop's operational invoice");
        scope.StoreId=1;scope.LocationId=1;
        var firstTotal=(await new InvoiceRepository(factory).GetInvoiceAsync(firstGstSale))!.TotalAmount;scope.StoreId=other.Store;scope.LocationId=other.Location;var secondTotal=(await new InvoiceRepository(factory).GetInvoiceAsync(secondGstSale))!.TotalAmount;scope.StoreId=0;scope.LocationId=0;scope.Consolidated=true;
        Check((await reporting.FinancialAsync(day,day)).Sum(r=>r.SalesValue)-beforeConsolidated==firstTotal+secondTotal,"Explicit owner consolidated report reconciles both private shops without merging their master records");scope.StoreId=1;scope.LocationId=1;scope.Consolidated=false;
        Check((await gst.ReportAsync(new(){RegistrationId=reg,From=day,To=day,Report="sales"})).Count==1,"Selected-shop GST report includes only that shop's tax snapshot");
        Check((await gst.ReportAsync(new(){RegistrationId=reg,From=day,To=day,Report="sales",RegistrationWide=true})).Count==2,"Explicit authorized GST registration consolidation includes two mapped private shops without master sharing");
        await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='GST_REPORTS' AND a.action_code='VIEW_REGISTRATION'");
        await Reject(()=>gst.ReportAsync(new(){RegistrationId=reg,From=day,To=day,Report="sales",RegistrationWide=true}),"Registration-wide GST view requires its actual dedicated permission");
        await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='GST_REPORTS' AND a.action_code='VIEW_REGISTRATION'");
        var tenant=await setup.ExecuteScalarAsync<long>("INSERT INTO tenants(tenant_code,business_name) VALUES('PRIVATE-TENANT-TWO','Separate test tenant') RETURNING tenant_id");
        var tenantStore=await setup.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(@tenant,'OTHER','Other tenant shop') RETURNING store_id",new{tenant});
        var tenantLocation=await setup.ExecuteScalarAsync<long>("INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) VALUES(@tenant,@tenantStore,'Other tenant counter','SALES_FLOOR') RETURNING location_id",new{tenant,tenantStore});
        var role=await setup.ExecuteScalarAsync<int>("INSERT INTO roles(tenant_id,role_name) VALUES(@tenant,'Tenant owner') RETURNING role_id",new{tenant});
        var owner=await setup.ExecuteScalarAsync<int>("INSERT INTO users(tenant_id,role_id,full_name,username,password_hash,is_tenant_owner) VALUES(@tenant,@role,'Other test owner','other-test-owner','not-a-login-hash',true) RETURNING user_id",new{tenant,role});
        scope.TenantId=tenant;scope.UserId=owner;scope.StoreId=tenantStore;scope.LocationId=tenantLocation;
        var foreignCustomer=await customers.CreateAsync(new(){CustomerName="OTHER-TENANT-CUSTOMER"});Check(await customers.GetByIdAsync(customer)==null,"Actual second-tenant owner cannot resolve tenant one customer ID");
        scope.TenantId=1;scope.UserId=1;scope.StoreId=1;scope.LocationId=1;
        Check(await customers.GetByIdAsync(foreignCustomer)==null,"Tenant one owner cannot resolve actual tenant two customer ID");
        client.DefaultRequestHeaders.Add("Test-Tenant",tenant.ToString());client.DefaultRequestHeaders.Add("Test-Store",tenantStore.ToString());client.DefaultRequestHeaders.Add("Test-Location",tenantLocation.ToString());
        Check((await client.GetAsync("/api/shopmasters")).StatusCode==HttpStatusCode.Forbidden,"Forged other-tenant owner context rejected by actual membership middleware");
        client.DefaultRequestHeaders.Remove("Test-Tenant");client.DefaultRequestHeaders.Remove("Test-Store");client.DefaultRequestHeaders.Remove("Test-Location");
        var staff=await setup.ExecuteScalarAsync<int>("INSERT INTO users(tenant_id,role_id,full_name,username,password_hash) SELECT 1,role_id,'Restricted worker','private-worker','not-a-login-hash' FROM roles WHERE tenant_id=1 ORDER BY role_id LIMIT 1 RETURNING user_id");
        await setup.ExecuteAsync("INSERT INTO user_store_access(tenant_id,user_id,store_id,is_default,is_active) VALUES(1,@staff,1,true,true)",new{staff});scope.UserId=staff;
        await using(var restricted=factory.CreateConnection())await Reject(()=>restricted.ExecuteAsync("SELECT sf_share_master('CUSTOMER',@customer,@target,true,'Unapproved worker sharing attempt')",new{customer,target=other.Store}),"Restricted employee cannot grant sharing even when an action ID is forged");
        scope.StoreId=other.Store;scope.LocationId=other.Location;Check(!(await customers.GetAllAsync()).Any(),"Restricted employee cannot forge another store GUC to read its private customers");scope.UserId=1;scope.StoreId=1;scope.LocationId=1;
        Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM master_sharing_events WHERE tenant_id=1")==4,"Explicit share/revoke decisions retain actor/reason audit records");
        Console.WriteLine("Private shop integration passed: actual branch and permission services, test authentication, PostgreSQL RLS, explicit sharing and real second tenant.");
    }
    static async Task Expenses(NpgsqlConnection setup,DbConnectionFactory factory,BusinessScope scope,HttpClient client,(long Store,long Location) other)
    {
        var repo=new ExpenseRepository(factory);var day=new ShopTimeOptions().Today(TimeProvider.System);var incurred=day.AddDays(-2);
        var cat=await repo.MasterAsync("CATEGORY",Guid.NewGuid(),"Rent");var payee=await repo.MasterAsync("PAYEE",Guid.NewGuid(),"=Test landlord");var id=Guid.NewGuid();
        var cash=new CashClosingRepository(factory,scope,Options.Create(new ShopTimeOptions()),TimeProvider.System);var cashBefore=(await cash.GetAsync(day)).Totals.ExpenseCash;
        var stock=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");var gst=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents");
        long version=0;
        async Task<long> Write(string action,object body,bool concurrent=false){var op=new ExpenseOperation(Guid.NewGuid(),version,JsonSerializer.SerializeToElement(body));var path=$"/api/expenses/{id}/{action}";var responses=concurrent?await Task.WhenAll(client.PostAsJsonAsync(path,op),client.PostAsJsonAsync(path,op)):[await client.PostAsJsonAsync(path,op)];foreach(var response in responses){var text=await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode,$"Expense HTTP {action}: {(int)response.StatusCode} "+(response.IsSuccessStatusCode?"":text));}var values=await Task.WhenAll(responses.Select(async r=>(await r.Content.ReadFromJsonAsync<JsonObject>())!["version"]!.GetValue<long>()));Check(values.Distinct().Count()==1,"Concurrent/retry "+action+" has one version");version=values[0];return version;}
        version=await ExpenseRecordingChecks.SeedLegacyAsync(factory,id,new(Guid.NewGuid(),0,JsonSerializer.SerializeToElement(new{categoryId=cat,payeeId=payee,incurredDate=incurred.ToString("yyyy-MM-dd"),amount=100,description="Office rent"})));
        await Reject(()=>repo.WriteAsync(id,"PAY",new(Guid.NewGuid(),version,JsonSerializer.SerializeToElement(new{paidDate=day.ToString("yyyy-MM-dd"),amount=10,paymentMode="CASH"}))),"Draft expense cannot be paid");
        await Write("post",new{});
        var paymentId=Guid.NewGuid();var payment=new ExpenseOperation(paymentId,version,JsonSerializer.SerializeToElement(new{paidDate=day.ToString("yyyy-MM-dd"),amount=40,paymentMode="CASH",reference="Cash receipt"}));
        var paid=await Task.WhenAll(repo.WriteAsync(id,"PAY",payment),repo.WriteAsync(id,"PAY",payment));Check(paid[0]==paid[1],"Concurrent cash payment retry posts once");version=paid[0];
        Check((await repo.GetAsync(id)).GetProperty("paid").GetDecimal()==40,"Partial payment produces 60 outstanding");
        await Reject(()=>repo.WriteAsync(id,"PAY",payment with{Body=JsonSerializer.SerializeToElement(new{paidDate=day.ToString("yyyy-MM-dd"),amount=41,paymentMode="CASH"})}),"Changed payment payload with same operation ID rejected");
        await Write("pay",new{paidDate=day.ToString("yyyy-MM-dd"),amount=60,paymentMode="UPI"});
        Check((await repo.GetAsync(id)).GetProperty("paid").GetDecimal()==100,"Full payment clears the expense balance");
        await Reject(()=>repo.WriteAsync(id,"PAY",new(Guid.NewGuid(),version,JsonSerializer.SerializeToElement(new{paidDate=day.ToString("yyyy-MM-dd"),amount=1,paymentMode="CASH"}))),"Overpayment rejected atomically");
        await Write("refund",new{paidDate=day.ToString("yyyy-MM-dd"),amount=10,paymentMode="CASH",reason="Returned overcharge"},true);
        Check((await cash.GetAsync(day)).Totals.ExpenseCash-cashBefore==30,"Cash Closing includes cash payments minus cash refund, excludes UPI");
        var report=await repo.ReportAsync(incurred,incurred);Check(report.GetProperty("incurred").GetDecimal()==100&&report.GetProperty("paid").GetDecimal()==0&&report.GetProperty("outstandingAtCutoff").GetDecimal()>=100,"Incurred and paid date cutoffs remain distinct");
        report=await repo.ReportAsync(day,day);Check(report.GetProperty("incurred").GetDecimal()==0&&report.GetProperty("paid").GetDecimal()==90,"Today's expense report includes signed payments, not old incurred expense");
        await Write("reverse",new{paymentId,paidDate=day.ToString("yyyy-MM-dd"),reason="Wrong cash payment reversed"},true);
        Check((await cash.GetAsync(day)).Totals.ExpenseCash-cashBefore==-10,"Cash reversal is a new signed movement, not a second refund");
        await Reject(()=>repo.WriteAsync(id,"REVERSE",new(Guid.NewGuid(),version,JsonSerializer.SerializeToElement(new{paymentId,paidDate=day.ToString("yyyy-MM-dd"),reason="Duplicate reversal denied"}))),"Same payment cannot be reversed twice");
        await Write("correct",new{categoryId=cat,payeeId=payee,incurredDate=incurred.ToString("yyyy-MM-dd"),amount=120,description="Corrected rent",reason="Landlord corrected the amount"});
        var corrected=await repo.GetAsync(id);Check(corrected.GetProperty("events").GetArrayLength()==7&&corrected.GetProperty("events")[0].GetProperty("snapshot").GetProperty("amount").GetDecimal()==100&&corrected.GetProperty("amount").GetDecimal()==120,"Correction preserves original amount in immutable history and changes only current basis");
        var attachmentOp=Guid.NewGuid();using(var form=new MultipartFormDataContent()){form.Add(new StringContent(attachmentOp.ToString()),"operationId");form.Add(new StringContent(version.ToString()),"expectedVersion");form.Add(new ByteArrayContent("%PDF-1.4 synthetic receipt"u8.ToArray()),"file","receipt.pdf");var response=await client.PostAsync($"/api/expenses/{id}/attachments",form);Check(response.IsSuccessStatusCode,"Authorized expense receipt upload: "+await response.Content.ReadAsStringAsync());version++;}
        Check((await client.GetAsync($"/api/expenses/{id}/attachments/{attachmentOp}")).IsSuccessStatusCode,"Authorized receipt downloads through scoped API");
        var exported=await client.GetStringAsync($"/api/expenses/export?from={incurred:yyyy-MM-dd}&to={day:yyyy-MM-dd}");Check(exported.Contains("'=Test landlord")&&exported.Contains(id.ToString()),"CSV includes source IDs and neutralizes formula payee names");
        foreach(var route in new[]{"/Expenses","/Expenses/Create","/Expenses/Details?id="+id,"/Expenses/Report"})Check((await client.GetAsync(route)).IsSuccessStatusCode,"Actual Razor screen renders: "+route);
        client.DefaultRequestHeaders.Add("Test-Store",other.Store.ToString());client.DefaultRequestHeaders.Add("Test-Location",other.Location.ToString());
        Check((await client.GetAsync($"/api/expenses/{id}")).StatusCode==HttpStatusCode.NotFound,"Actual branch membership switching cannot read another shop's expense");
        Check((await client.GetAsync($"/api/expenses/{id}/attachments/{attachmentOp}")).StatusCode==HttpStatusCode.NotFound,"Foreign shop cannot download attachment by forged IDs");
        Check((await client.PostAsJsonAsync($"/api/expenses/{id}/pay",new ExpenseOperation(Guid.NewGuid(),version,JsonSerializer.SerializeToElement(new{paidDate=day.ToString("yyyy-MM-dd"),amount=1,paymentMode="CASH"})))).StatusCode==HttpStatusCode.Forbidden,"Wrong shop cannot mutate an expense using a forged source ID");
        Check(!(await client.GetStringAsync($"/api/expenses/export?from={incurred:yyyy-MM-dd}&to={day:yyyy-MM-dd}")).Contains(id.ToString()),"Expense export does not include another shop");
        client.DefaultRequestHeaders.Remove("Test-Store");client.DefaultRequestHeaders.Remove("Test-Location");
        client.DefaultRequestHeaders.Add("Test-Tenant","999999");Check((await client.GetAsync($"/api/expenses/{id}")).StatusCode==HttpStatusCode.Forbidden,"Forged tenant owner claim rejected by actual branch membership");client.DefaultRequestHeaders.Remove("Test-Tenant");
        await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='EXPENSES' AND a.action_code<>'VIEW'; UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='EXPENSES' AND a.action_code='VIEW'");
        Check((await client.GetAsync("/api/expenses")).StatusCode==HttpStatusCode.Forbidden,"Actual database expense permission denial is authoritative at the API");
        Check((await cash.GetAsync(day)).Totals.ExpenseCash-cashBefore==-10,"Cash Closing retains complete aggregate even when expense-detail permission is denied");
        await setup.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='EXPENSES' AND a.action_code='VIEW'; UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='EXPENSES'");
        using(var invalid=new MultipartFormDataContent()){invalid.Add(new StringContent(Guid.NewGuid().ToString()),"operationId");invalid.Add(new StringContent(version.ToString()),"expectedVersion");invalid.Add(new ByteArrayContent("<script>spoofed receipt</script>"u8.ToArray()),"file","receipt.pdf");Check((await client.PostAsync($"/api/expenses/{id}/attachments",invalid)).StatusCode==HttpStatusCode.BadRequest,"Attachment MIME/name spoof cannot bypass content validation");}
        await Write("refund",new{paidDate=day.ToString("yyyy-MM-dd"),amount=50,paymentMode="UPI",reason="Final refund of remaining recorded payment"});
        await Write("cancel",new{reason="Expense fully refunded and cancelled"},true);
        Check((await repo.GetAsync(id)).GetProperty("status").GetString()=="CANCELLED","Fully refunded expense can be cancelled without erasing payment history");
        await Reject(()=>repo.WriteAsync(id,"PAY",new(Guid.NewGuid(),version,JsonSerializer.SerializeToElement(new{paidDate=day.ToString("yyyy-MM-dd"),amount=1,paymentMode="CASH"}))),"Cancelled expense cannot accept another payment");
        foreach(var profile in BusinessProfiles.Codes.Where(p=>p!="ELECTRICAL")){
            await setup.ExecuteAsync("UPDATE tenants SET business_profile_code=@profile WHERE tenant_id=1",new{profile});scope.BusinessProfileCode=profile;
            var profileCat=await repo.MasterAsync("CATEGORY",Guid.NewGuid(),"Utilities");var profilePayee=await repo.MasterAsync("PAYEE",Guid.NewGuid(),"Profile payee");var profileExpense=Guid.NewGuid();
            await ExpenseRecordingChecks.SeedLegacyAsync(factory,profileExpense,new(Guid.NewGuid(),0,JsonSerializer.SerializeToElement(new{categoryId=profileCat,payeeId=profilePayee,incurredDate=day.ToString("yyyy-MM-dd"),amount=10,description="Profile-specific fixture"})));
            Check((await repo.GetAsync(profileExpense)).GetProperty("business_profile_code").GetString()==profile,profile+" expense uses existing profile scope");
            await Reject(()=>repo.GetAsync(id),profile+" cannot read Electrical expense history");
            scope.StoreId=other.Store;scope.LocationId=other.Location;await Reject(()=>repo.GetAsync(profileExpense),profile+" second same-profile shop cannot read private expense");scope.StoreId=1;scope.LocationId=1;
        }scope.BusinessProfileCode="ELECTRICAL";await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");
        Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==stock&&await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents")==gst,"Expense creation/payments/refunds/corrections/attachments never post inventory or GST documents");
        var gstRepository=new GstRepository(factory);var browserRule=await gstRepository.SaveRuleAsync(new(){RuleName="Browser GST 18",Description="Synthetic browser expense GST rule",Tax=new(){Classification="SERVICE",HsnSac="998719",TaxRate=18,EffectiveFrom=day.AddYears(-1),ReviewReference="Synthetic browser rate review"}});await gstRepository.ReviewRuleAsync(browserRule);
        await Browser(client);
        var browserRows=await repo.ListAsync("Browser verified overhead",day,day,1,20);
        Check(browserRows.GetProperty("rows").GetArrayLength()==1&&browserRows.GetProperty("rows")[0].GetProperty("paid").GetDecimal()==25,"Live Razor/JS workflow reconciles to one PostgreSQL expense with exactly 25 paid after lost-response retry");
        Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==stock,"Browser expense workflow has no inventory posting");
        Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents")==gst,"Browser GST expense references do not create tax or ITC claim documents");
        Console.WriteLine("Expense integration passed against real branch/security repositories, test authentication and disposable PostgreSQL.");
    }
    static async Task Browser(HttpClient client)
    {
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");await using var proxy=builder.Build();
        proxy.Run(async context=>{
            using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);
            if(context.Request.ContentLength>0||context.Request.ContentType is not null){using var buffer=new MemoryStream();await context.Request.Body.CopyToAsync(buffer);request.Content=new ByteArrayContent(buffer.ToArray());}
            foreach(var header in context.Request.Headers){if(header.Key.Equals("Host",StringComparison.OrdinalIgnoreCase)||header.Key.StartsWith("Test-",StringComparison.OrdinalIgnoreCase))continue;if(!request.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray()))request.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray());}
            using var response=await client.SendAsync(request);context.Response.StatusCode=(int)response.StatusCode;
            foreach(var h in response.Headers)context.Response.Headers[h.Key]=h.Value.ToArray();foreach(var h in response.Content.Headers)context.Response.Headers[h.Key]=h.Value.ToArray();context.Response.Headers.Remove("transfer-encoding");await response.Content.CopyToAsync(context.Response.Body);
        });await proxy.StartAsync();
        try{
            var start=new System.Diagnostics.ProcessStartInfo(Path.GetFullPath("tests/StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("tests/StockFlow.UiChecks/expense-live.cjs");start.ArgumentList.Add(proxy.Urls.Single());
            using var process=System.Diagnostics.Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Console.WriteLine(await output);Check(process.ExitCode==0,"Live expense browser: "+(process.ExitCode==0?"passed":await error));
        }finally{await proxy.StopAsync();}
    }
    static async Task Quarantine()
    {
        var db="quarantine_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(Host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
        try{
            await using var c=new NpgsqlConnection(Host+db);await c.OpenAsync();
            foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3]) is >=70 and <85).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
            var legacy=await c.QuerySingleAsync<(long Sale,string Profile,int Product,int Category,int? Parent,int? Brand)>("SELECT s.sale_id,s.business_profile_code,i.product_id,p.category_id,cat.parent_category_id,p.brand_id FROM sales s JOIN sale_items i USING(tenant_id,sale_id) JOIN products p USING(tenant_id,product_id) JOIN categories cat USING(tenant_id,category_id) WHERE s.status='COMPLETED' AND i.product_id IS NOT NULL ORDER BY s.sale_id LIMIT 1");
            await c.ExecuteAsync("UPDATE tenants SET business_profile_code=@Profile WHERE tenant_id=1; INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'AMBIGUOUS','Legacy competing shop')",new{legacy.Profile});
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode=legacy.Profile,ProfileDataVisibilityAvailable=true};var factory=new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",Host+db}}).Build(),scope);
            var before=(await new InvoiceRepository(factory).GetInvoiceAsync(legacy.Sale))!;var stockBefore=(await new ReportingRepository(factory,scope).ActivityAsync(DateTime.Today.AddYears(-5),DateTime.Today,true,false)).Stock.Sum(x=>x.Closing);
            var snapshot=await c.ExecuteScalarAsync<string>("SELECT md5(string_agg(to_jsonb(s)::text,'' ORDER BY sale_id)) FROM sales s");
            await c.ExecuteAsync(await File.ReadAllTextAsync("database/migrations/085_private_shop_masters.sql"));
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM products WHERE owner_store_id IS NOT NULL")==0,"Multi-store legacy products remain quarantined; usage is never guessed as ownership");
            var after=(await new InvoiceRepository(factory).GetInvoiceAsync(legacy.Sale))!;
            Check(after.TotalAmount==before.TotalAmount&&after.Items.Count==before.Items.Count&&after.Items.Select(x=>x.Sku).SequenceEqual(before.Items.Select(x=>x.Sku)),"Quarantined historical invoice retains saved totals, lines and SKUs");
            Check(await c.ExecuteScalarAsync<string>("SELECT md5(string_agg(to_jsonb(s)::text,'' ORDER BY sale_id)) FROM sales s")==snapshot,"Ownership migration does not rewrite historical invoice rows");
            Check((await new ReportingRepository(factory,scope).ActivityAsync(DateTime.Today.AddYears(-5),DateTime.Today,true,false)).Stock.Sum(x=>x.Closing)==stockBefore,"Quarantined master does not drop historical stock movement report totals");
            await using var runtime=factory.CreateConnection();
            if(legacy.Parent.HasValue)await runtime.ExecuteAsync("SELECT sf_resolve_master('CATEGORY',@id,1,'Owner reviewed original category evidence')",new{id=legacy.Parent.Value});
            await runtime.ExecuteAsync("SELECT sf_resolve_master('CATEGORY',@id,1,'Owner reviewed original category evidence')",new{id=legacy.Category});
            if(legacy.Brand.HasValue)await runtime.ExecuteAsync("SELECT sf_resolve_master('BRAND',@id,1,'Owner reviewed original manufacturer evidence')",new{id=legacy.Brand.Value});
            await runtime.ExecuteAsync("SELECT sf_resolve_master('PRODUCT',@id,1,'Owner reviewed original product evidence')",new{id=legacy.Product});
            Check(await runtime.ExecuteScalarAsync<bool>("SELECT sf_master_access('PRODUCT',@id,true)",new{id=legacy.Product}),"Explicit owner decision resolves quarantined product additively with its original ID");
            await Reject(()=>runtime.ExecuteAsync("SELECT sf_resolve_master('PRODUCT',@id,1,'Attempt to reassign existing allocated record')",new{id=legacy.Product}),"Allocated historical records cannot be reassigned by the resolution API");
        }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
    }
}
