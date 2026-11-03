using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;

static class BillingDatabaseChecks
{
    public static async Task Run()
    {
        var db="billing_test_"+Guid.NewGuid().ToString("N");
        var cs=$"Host=127.0.0.1;Port=55441;Database={db};Username=postgres";
        await using var admin=new NpgsqlConnection(cs.Replace($"Database={db}","Database=postgres"));await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db);
        try
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
            await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
            foreach(var f in Directory.GetFiles(Path.Combine(root!.FullName,"database/scripts"),"*.sql").Where(f=>!Path.GetFileName(f).StartsWith("001")&&!Path.GetFileName(f).StartsWith("006")).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
            foreach(var f in Directory.GetFiles(Path.Combine(root.FullName,"database/migrations"),"*.sql").Order())
            {
                if(Path.GetFileName(f).StartsWith("020"))await c.ExecuteAsync("INSERT INTO roles(role_name) VALUES('Admin'); INSERT INTO users(role_id,full_name,username,password_hash) VALUES(1,'Owner','owner','unused'); INSERT INTO categories(category_name) VALUES('General'); INSERT INTO products(category_id,product_name,sku,selling_price,tax_percentage) VALUES(1,'Shirt','SHIRT',100,18);");
                await c.ExecuteAsync(await File.ReadAllTextAsync(f));
            }
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false);SELECT set_config('app.user_id','1',false);SELECT set_config('app.location_id','1',false);");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
            var factory=new DbConnectionFactory(config,scope);var receiving=new StockIntakeRepository(factory);var sales=new SalesRepository(factory);
            var customerRepo=new CustomerRepository(factory);var customerId=await customerRepo.CreateAsync(new(){CustomerName="Counter customer",MobileNumber="9876543210"});
            Check((await customerRepo.GetAllAsync(true,"9876543210")).Single().CustomerId==customerId,"quick customer can be found by mobile");
            var batteryId=await c.ExecuteScalarAsync<int>("INSERT INTO products(category_id,product_name,sku,selling_price,is_serial_tracked) VALUES(1,'Battery','BAT',50,true) RETURNING product_id");
            var lastId=await c.ExecuteScalarAsync<int>("INSERT INTO products(category_id,product_name,sku,selling_price) VALUES(1,'Last item','LAST',10) RETURNING product_id");
            var batteryVariant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@batteryId",new{batteryId});
            var lastVariant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@lastId",new{lastId});
            await receiving.PostAsync(new(){RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){ProductId=1,VariantId=1,Quantity=3},new(){ProductId=batteryId,VariantId=batteryVariant,Quantity=2,SerialNumbers=["BAT-01","BAT-02"]},new(){ProductId=lastId,VariantId=lastVariant,Quantity=1}]},true,1);
            var units=(await c.QueryAsync<long>("SELECT product_unit_id FROM product_units WHERE product_id=@batteryId ORDER BY product_unit_id",new{batteryId})).ToArray();
            CreateSaleRequest Sale(params CreateSaleItemRequest[] items)=>new(){RequestId=Guid.NewGuid(),Items=items.ToList(),CanDiscount=true,CanOverridePrice=true,CanApproveDiscount=true,CanTradeIn=true,AdjustmentReason="Approved counter adjustment"};
            CreateSaleItemRequest Item(int id,long variant,int qty,decimal price,long? unit=null)=>new(){ProductId=id,VariantId=variant,Quantity=qty,SellingPrice=price,ProductUnitId=unit};
            async Task Pay(CreateSaleRequest request){var quote=await sales.QuoteAsync(request);request.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];}
            var mixed=Sale(Item(1,1,1,100),Item(batteryId,batteryVariant,1,50,units[0]));mixed.CustomerId=customerId;mixed.Items[0].DiscountAmount=10;mixed.DiscountAmount=1;
            var quote=await sales.QuoteAsync(mixed);Check(quote.TotalAmount==155.2m,"mixed quantity/serial cart calculates authoritative tax and discounts");
            mixed.Payments=[new(){PaymentMode="CASH",Amount=100},new(){PaymentMode="UPI",Amount=55.2m,ReferenceNumber="UPI-counter"}];
            mixed.CashTendered=150;
            var ids=await Task.WhenAll(sales.CreateSaleAsync(mixed,1),sales.CreateSaleAsync(mixed,1));Check(ids[0]==ids[1],"concurrent retry creates one invoice");
            var invoice=(await sales.GetByIdAsync(ids[0]))!;Check(invoice.CustomerName=="Counter customer"&&invoice.Payments.Count==2&&invoice.Lines.Any(l=>l.SerialNumber=="BAT-01"),"invoice retains customer, split payments and individual serial");
            var cashInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(ids[0]))!;
            Check(cashInvoice.CashTendered==150 && cashInvoice.CashChange==50 && cashInvoice.AmountReceived==155.2m,"receipt records cash tender and change without inflating actual payment");
            Check(await sales.RecoverCompletedAsync(mixed,1)==ids[0]&&await sales.RecoverCompletedAsync(mixed,999) is null,"legacy recovery returns only the matching cashier invoice");
            var neverPosted=Sale(Item(1,1,1,100));await Pay(neverPosted);Check(await sales.RecoverCompletedAsync(neverPosted,1) is null,"legacy recovery never posts an uncommitted draft");
            await c.ExecuteAsync("UPDATE customers SET customer_name='Renamed' WHERE customer_id=@customerId",new{customerId});Check((await sales.GetByIdAsync(ids[0]))!.CustomerName=="Counter customer","customer edits do not rewrite completed invoice identity");
            var soldAgain=Sale(Item(batteryId,batteryVariant,1,50,units[0]));await Reject(()=>sales.QuoteAsync(soldAgain),"sold serial rejected during quote before payment");soldAgain.Payments=[new(){Amount=50}];await Reject(()=>sales.CreateSaleAsync(soldAgain,1),"sold battery cannot be sold again while another battery remains");
            var duplicate=Sale(Item(batteryId,batteryVariant,1,50,units[1]),Item(batteryId,batteryVariant,1,50,units[1]));duplicate.Payments=[new(){PaymentMode="CASH",Amount=100}];await Reject(()=>sales.CreateSaleAsync(duplicate,1),"duplicate serial in one request is rejected");
            var missingUnit=Sale(Item(batteryId,batteryVariant,1,50));await Pay(missingUnit);await Reject(()=>sales.CreateSaleAsync(missingUnit,1),"shared battery model cannot complete without physical unit");
            var tooMany=Sale(Item(1,1,3,100));await Pay(tooMany);await Reject(()=>sales.CreateSaleAsync(tooMany,1),"insufficient stock is rejected atomically");
            var badPay=Sale(Item(1,1,1,100));badPay.Payments=[new(){PaymentMode="CARD",Amount=1}];await Reject(()=>sales.CreateSaleAsync(badPay,1),"unbalanced payment cannot consume stock");
            var shortCash=Sale(Item(1,1,1,100));await Pay(shortCash);shortCash.CashTendered=50;
            await Reject(()=>sales.CreateSaleAsync(shortCash,1),"server rejects cash tender smaller than cash payment");
            var walkInCredit=Sale(Item(1,1,1,100));walkInCredit.Payments=[new(){PaymentMode="CREDIT",Amount=118}];await Reject(()=>sales.CreateSaleAsync(walkInCredit,1),"walk-in cannot be billed on credit");
            var a=Sale(Item(lastId,lastVariant,1,10));var b=Sale(Item(lastId,lastVariant,1,10));await Pay(a);await Pay(b);
            var race=await Task.WhenAll(Attempt(a),Attempt(b));Check(race.Count(x=>x)==1,"two tills cannot sell the same last quantity unit");
            var unitA=Sale(Item(batteryId,batteryVariant,1,50,units[1]));var unitB=Sale(Item(batteryId,batteryVariant,1,50,units[1]));await Pay(unitA);await Pay(unitB);
            Check((await Task.WhenAll(Attempt(unitA),Attempt(unitB))).Count(x=>x)==1,"two tills cannot sell the same individual serial");
            await c.ExecuteAsync("UPDATE products SET has_variants=true WHERE product_id=1");
            var blue=await new VariantRepository(factory).SaveAsync(null,new(){ProductId=1,VariantName="Blue / M",Sku="BLUE-M",SellingPrice=100,TaxPercentage=18,Attributes=new(){["Size"]="M",["Color"]="Blue"}},1);
            var wrongVariant=Sale(Item(1,blue,1,100));await Pay(wrongVariant);await Reject(()=>sales.CreateSaleAsync(wrongVariant,1),"another size/color's stock cannot satisfy selected variant");
            var credit=Sale(Item(1,1,1,100));credit.CustomerId=customerId;credit.Payments=[new(){PaymentMode="CASH",Amount=18},new(){PaymentMode="CREDIT",Amount=100}];await sales.CreateSaleAsync(credit,1);
            Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=1")==1,"failed sales never consume quantity and named split credit is recorded");
            Check(await c.ExecuteScalarAsync<decimal>("SELECT sf_customer_credit_balance(@customerId)",new{customerId})==100,"credit balance excludes cash and UPI payments");
            await c.ExecuteAsync("INSERT INTO customer_credit_limits(customer_id,credit_limit) VALUES(@customerId,150)",new{customerId});
            var excessCredit=Sale(Item(1,1,1,100));excessCredit.CustomerId=customerId;excessCredit.Payments=[new(){PaymentMode="CREDIT",Amount=118}];
            await Reject(()=>sales.CreateSaleAsync(excessCredit,1),"server blocks credit beyond customer limit without consuming stock");
            var adjusted=Sale(Item(1,1,1,80));adjusted.CanOverridePrice=false;await Pay(adjusted);
            await Reject(()=>sales.CreateSaleAsync(adjusted,1),"server rejects unauthorized price override");
            adjusted=Sale(Item(1,1,1,100));adjusted.Items[0].DiscountAmount=1;adjusted.CanDiscount=false;await Pay(adjusted);
            await Reject(()=>sales.CreateSaleAsync(adjusted,1),"server rejects unauthorized discount");
            adjusted.CanDiscount=true;adjusted.AdjustmentReason=null;
            await Reject(()=>sales.CreateSaleAsync(adjusted,1),"server requires an adjustment reason");
            var approved=Sale(Item(1,1,1,100));approved.Items[0].DiscountAmount=20;approved.CanApproveDiscount=false;await Pay(approved);
            await Reject(()=>sales.CreateSaleAsync(approved,1),"discount above threshold needs approval");
            await c.ExecuteAsync("INSERT INTO billing_approvals(draft_id,requested_by,financial_hash,summary,approved_by,approved_at) VALUES(@id,1,@hash,'{}',1,now())",new{id=approved.RequestId,hash=BillingApproval.Hash(approved)});
            approved.AdjustmentReason="Changed after approval";
            await Reject(()=>sales.CreateSaleAsync(approved,1),"changed financial details invalidate prior approval");
            approved.AdjustmentReason="Approved counter adjustment";
            await c.ExecuteAsync("INSERT INTO billing_drafts(draft_id,label,payload) VALUES(@id,'Held bill','{}')",new{id=approved.RequestId});
            Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=1")==1,"held draft never deducts stock");
            approved.TradeIn=new(){ItemType="BATTERY",BrandModel="Old battery",SerialNumber="OLD-1",AllowanceAmount=10};approved.DiscountAmount=10;
            await Pay(approved);
            await c.ExecuteAsync("UPDATE billing_approvals SET financial_hash=@hash WHERE draft_id=@id",new{hash=BillingApproval.Hash(approved),id=approved.RequestId});
            var integrated=await sales.CreateSaleAsync(approved,1);await sales.CreateSaleAsync(approved,1);
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM trade_ins WHERE sale_id=@integrated",new{integrated})==1,"trade-in is recorded atomically once across sale retry");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_drafts WHERE draft_id=@id",new{id=approved.RequestId})==0,"completed bill removes its saved draft");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_approvals WHERE draft_id=@id",new{id=approved.RequestId})==0,"approval is consumed by completion");
            Check((await sales.GetByIdAsync(integrated))!.TotalAmount==84.4m,"trade-in allowance is included once in payable total");
            Check((await new InvoiceRepository(factory).GetInvoiceAsync(integrated))!.TradeInAllowance==10,"invoice retains the trade-in allowance separately from discretionary discount");
            using(var baseWeb=new Factory())
            using(var web=baseWeb.WithWebHostBuilder(builder=>builder.ConfigureServices(services=> {
                services.RemoveAll<DbConnectionFactory>();services.AddScoped(sp=>new DbConnectionFactory(config,sp.GetRequiredService<BusinessScope>()));
            })))
            {
                using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
                var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
                var draft=Guid.NewGuid();var editorId=Guid.NewGuid();var body=new {label="Test active bill",editorId,revision=0,operationId=Guid.NewGuid(),payload=new {draftId=draft,cart=new[]{new{productId=1}},savedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}};
                var savedDraft=await client.PutAsJsonAsync("/api/sales/billing/drafts/"+draft,body);Check((int)savedDraft.StatusCode==200,"draft API saves Active with authenticated editor, owner and CSRF");
                var draftState=await savedDraft.Content.ReadFromJsonAsync<JsonObject>();
                Check((await client.GetAsync("/api/sales/billing/drafts/"+draft)).IsSuccessStatusCode,"owner can read own Active bill");
                Check((int)(await client.PutAsJsonAsync("/api/sales/billing/drafts/"+Guid.NewGuid(),body)).StatusCode==400,"draft API rejects mismatched bill identity");
                client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:CREATE");Check((int)(await client.GetAsync("/api/sales/billing/drafts")).StatusCode==403,"draft list requires billing permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
                client.DefaultRequestHeaders.Add("Test-Deny-Action","CUSTOMERS:EDIT");Check((int)(await client.PutAsJsonAsync($"/api/sales/billing/customers/{customerId}/credit",new{creditLimit=999})).StatusCode==403,"cashier cannot change credit limit without customer-edit permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
                client.DefaultRequestHeaders.Add("Test-Deny-Action","COMPANY_SETTINGS:EDIT");Check((int)(await client.PutAsJsonAsync("/api/sales/billing/policy",new{discountLimitPercent=99})).StatusCode==403,"cashier cannot change discount policy without settings permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
                var approvalBill=Sale(Item(1,1,1,80));var approvalId=Guid.NewGuid();
                Check((int)(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{approvalId}/approval",approvalBill)).StatusCode==204,"cashier requests approval of financial details");
                var requests=await client.GetFromJsonAsync<JsonArray>("/api/sales/billing/approvals");var row=requests!.Select(n=>n!.AsObject()).Single(n=>n["draftId"]!.GetValue<Guid>()==approvalId);
                client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:DISCOUNT_APPROVE");Check((int)(await client.PutAsJsonAsync($"/api/sales/billing/approvals/{approvalId}",new{hash=row["hash"]!.GetValue<string>()})).StatusCode==403,"approval requires distinct approver permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
                Check((int)(await client.PutAsJsonAsync($"/api/sales/billing/approvals/{approvalId}",new{hash=row["hash"]!.GetValue<string>()})).StatusCode==204,"manager approves exact financial fingerprint");
                await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api/sales/billing/drafts/"+draft){Content=JsonContent.Create(new{editorId,revision=draftState!["revision"]!.GetValue<long>(),operationId=Guid.NewGuid()})});
            }
            var alienScope=new BusinessScope{TenantId=1,UserId=999,StoreId=1,LocationId=1};
            await using(var isolated=new DbConnectionFactory(config,alienScope).CreateConnection()) Check(await isolated.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_drafts")==0,"draft RLS hides other users' saved bills");
            var branches=new BranchRepository(factory,scope);var second=await branches.CreateStoreAsync(new(){StoreCode="SECOND",StoreName="Second store"});scope.LocationId=(await branches.LocationsAsync()).Single(l=>l.StoreId==second).LocationId;scope.StoreId=second;
            var other=Sale(Item(1,1,1,100));await Pay(other);await Reject(()=>sales.CreateSaleAsync(other,1),"stock from another store cannot be billed");Check(await sales.GetByIdAsync(ids[0]) is null,"another store cannot read the invoice");
            await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/counter_billing_checks.sql")));
            Console.WriteLine("Counter billing database checks passed on an isolated temporary database.");
            async Task<bool> Attempt(CreateSaleRequest sale){try{await sales.CreateSaleAsync(sale,1);return true;}catch(System.ComponentModel.DataAnnotations.ValidationException){return false;}}
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
    }
    static void Check(bool ok,string msg){if(!ok)throw new Exception("FAIL: "+msg);Console.WriteLine("PASS: "+msg);}
    static async Task Reject(Func<Task> action,string msg){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: "+msg);return;}throw new Exception("FAIL: "+msg);}
}
