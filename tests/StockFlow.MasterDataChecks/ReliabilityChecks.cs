using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using Npgsql;

namespace StockFlow.MasterDataChecks;

// Synthetic records only; never reads application database configuration.
internal static class ReliabilityChecks
{
    static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
    public static async Task Run()
    {
        const string prefix="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var name="p0_reconciliation_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(prefix+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+name+" TEMPLATE product_edit_069_test");
        try
        {
            await using var setup=new NpgsqlConnection(prefix+name);await setup.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
            await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1; SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
            var factory=new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",prefix+name}}).Build(),scope);
            var variants=new VariantRepository(factory);
            var products=new ProductService(new ProductRepository(factory),new CategoryRepository(factory),new BrandRepository(factory),scope,null,variants);
            await using var runtime=factory.CreateConnection();
            var registration=Guid.NewGuid();var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(4,@registration,NULL)",new{registration});
            var product=await products.CreateAsync(new(){RegistrationRequestId=registration,ParentProductCode=code,ProductName="P0 reconciliation fixture",CategoryId=4,TrackingType="QUANTITY",HasVariants=false,PurchasePrice=50,SellingPrice=100});
            var variant=(await variants.GetAsync(product,null,false)).Single().VariantId;
            var sales=new SalesRepository(factory);var operations=new OperationsRepository(factory);var purchases=new PurchaseRepository(factory);var receipts=new PurchaseReceiptRepository(factory);
            var shopTime=new ShopTimeOptions();var businessDate=shopTime.Today(TimeProvider.System);
            var cash=new CashClosingRepository(factory,scope,Options.Create(shopTime),TimeProvider.System);
            var cashBefore=(await cash.GetAsync(businessDate)).Totals;
            async Task Balance(int expected,string source)
            {
                var actual=(await variants.GetAsync(product,null,false)).Single().CurrentStock;
                Check(actual==expected,$"{source}: SKU {code}, variant {variant}, expected {expected}, ledger {actual}");
            }
            await Balance(0,"Catalog registration is non-posting");
            // Opening is posted through the existing intake engine, never by a balancing insert.
            await new StockIntakeRepository(factory).PostAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){ProductId=product,VariantId=variant,Quantity=10}]},true,1);
            await Balance(10,"Opening stock +10");
            var supplier=await new SupplierRepository(factory).CreateAsync(new(){SupplierName="P0 synthetic supplier",CountryCode="IN"});
            var po=await purchases.CreatePurchaseAsync(new(){LocationId=1,RequestId=Guid.NewGuid(),SupplierId=supplier,PurchaseDate=DateTime.Today,Items=[new(){ProductId=product,VariantId=variant,Quantity=5,PurchasePrice=50}]},1);
            await Balance(10,$"Purchase order {po} does not receive stock");
            var purchase=(await purchases.GetByIdAsync(po))!;
            var receiving=new ReceivePurchaseRequest{LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=purchase.Items.Single().PurchaseItemId,Quantity=4,DamagedQuantity=1}]};
            var ids=await Task.WhenAll(receipts.ReceiveAsync(po,receiving,1),receipts.ReceiveAsync(po,receiving,1));
            Check(ids[0]==ids[1],$"Concurrent receipt {po} retries share one receipt {ids[0]}");await Balance(14,$"Receipt {ids[0]}: accepted +4; damaged 1 is not saleable");
            var inventory=new InventoryRepository(factory);
            foreach(var direction in new[]{"ADJUSTMENT_IN","ADJUSTMENT_OUT"}){
                var adjustment=new StockFlow.Application.DTOs.Inventory.StockAdjustmentRequest{LocationId=1,RequestId=Guid.NewGuid(),ProductId=product,VariantId=variant,TransactionType=direction,Quantity=2,Reason="P0 explicit signed adjustment"};
                var movement=await inventory.AdjustAsync(adjustment,1);Check(await inventory.AdjustAsync(adjustment,1)==movement,"Adjustment retry returns original movement "+movement);
                await Balance(direction=="ADJUSTMENT_IN"?16:14,direction+" source "+movement);
            }
            var customer=await new CustomerRepository(factory).CreateAsync(new(){CustomerName="P0 synthetic customer"});
            var sale=new CreateSaleRequest{LocationId=1,CustomerId=customer,RequestId=Guid.NewGuid(),Items=[new(){ProductId=product,VariantId=variant,Quantity=2,SellingPrice=100}]};
            var quote=await sales.QuoteAsync(sale);sale.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];
            using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{
                s.RemoveAll<ISalesRepository>();s.AddSingleton<ISalesRepository>(sales);
                s.RemoveAll<IInvoiceRepository>();s.AddSingleton<IInvoiceRepository>(new InvoiceRepository(factory));
                s.RemoveAll<IOperationsRepository>();s.AddSingleton<IOperationsRepository>(operations);
                s.RemoveAll<IVariantRepository>();s.AddSingleton<IVariantRepository>(variants);
                s.RemoveAll<IPurchaseReceiptRepository>();s.AddSingleton<IPurchaseReceiptRepository>(receipts);
                s.RemoveAll<ITransferRepository>();s.AddSingleton<ITransferRepository>(new TransferRepository(factory));
                s.RemoveAll<ICashClosingRepository>();s.AddSingleton<ICashClosingRepository>(cash);
            }));
            using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
            var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
            async Task<JsonObject> Post(string path,object body)
            {
                var response=await client.PostAsJsonAsync(path,body);var text=await response.Content.ReadAsStringAsync();
                Check(response.IsSuccessStatusCode,$"HTTP {path}: {(int)response.StatusCode}"+(response.IsSuccessStatusCode?"":" "+text));return JsonNode.Parse(text)!.AsObject();
            }
            Check((await Post($"/api/purchases/{po}/receipts",receiving))["receiptId"]!.GetValue<long>()==ids[0],"HTTP receiving retry returns original PostgreSQL receipt");
            Check((await client.GetAsync("/api/products/variants?billing=true&term="+code)).IsSuccessStatusCode,"HTTP exact-variant search uses production PostgreSQL repository");
            var results=await Task.WhenAll(Post("/api/sales",sale),Post("/api/sales",sale));
            var saleId=results[0]["saleId"]!.GetValue<long>();Check(results[1]["saleId"]!.GetValue<long>()==saleId,"Concurrent HTTP Complete Sale posts one invoice");
            await Balance(12,$"Sale {saleId} -2 once");
            Check((await client.GetAsync("/api/invoices/"+saleId)).IsSuccessStatusCode,"HTTP invoice retrieval uses the PostgreSQL snapshot");
            var invoice=(await new InvoiceRepository(factory).GetInvoiceAsync(saleId))!;
            Check(invoice.TotalAmount==quote.TotalAmount&&invoice.AmountReceived==quote.TotalAmount,$"Invoice {saleId}: saved total = quote = collections");
            Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM sales WHERE request_id=@RequestId",sale)==1,"One invoice for request identity");
            sale.Items[0].Quantity=3;Check((await client.PostAsJsonAsync("/api/sales",sale)).StatusCode==HttpStatusCode.BadRequest,"HTTP changed-payload retry rejected");sale.Items[0].Quantity=2;
            var source=(await sales.GetByIdAsync(saleId))!;
            var returnRequest=new SaleReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="P0 original line refund",RefundMode="CASH",RefundAmount=quote.TotalAmount/2,Items=[new(){SaleItemId=source.Lines.Single().SaleItemId,Quantity=1,Disposition="RESTOCK"}]};
            var refundResults=await Task.WhenAll(Post($"/api/operations/sales/{saleId}/returns",returnRequest),Post($"/api/operations/sales/{saleId}/returns",returnRequest));
            var returned=refundResults[0];Check(returned["id"]!.ToJsonString()==refundResults[1]["id"]!.ToJsonString(),"Concurrent refund retries return one original return/refund");
            returnRequest.RefundAmount-=1;Check((await client.PostAsJsonAsync($"/api/operations/sales/{saleId}/returns",returnRequest)).StatusCode==HttpStatusCode.BadRequest,"Changed refund payload with same identity rejected");returnRequest.RefundAmount+=1;
            await Balance(13,$"Sale return {returned["id"]} +1");
            var settlement=await operations.SaleAsync(saleId);
            Check(settlement.PaidTotal==quote.TotalAmount&&settlement.ReturnedTotal==quote.TotalAmount/2&&settlement.RefundedTotal==quote.TotalAmount/2&&settlement.Outstanding==0,$"Invoice {saleId}: total - return - paid + refund = zero due; refund is not revenue");
            var payment=new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CASH",Amount=70};
            await Post($"/api/operations/purchases/{po}/payments",payment);await Post($"/api/operations/purchases/{po}/payments",payment);
            var supplierReturn=await Post($"/api/operations/purchases/{po}/returns",new PurchaseReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="P0 supplier return",Items=[new(){PurchaseItemId=purchase.Items.Single().PurchaseItemId,Quantity=1,Reason="P0 source return"}]});
            await Balance(12,$"Purchase return {supplierReturn["id"]} -1");
            var payable=await operations.PurchaseAsync(po);
            Check(payable.PaidTotal==70&&payable.ReturnedTotal==50&&payable.Outstanding==130,$"Purchase {po}: 250 order-basis amount - 70 paid - 50 returned = 130 operational due (not verified AP invoice liability)");
            var cashAfter=(await client.GetFromJsonAsync<CashDayReport>($"/api/cashclosing?date={businessDate:yyyy-MM-dd}"))!;
            Check(cashAfter.Totals.SalesCash-cashBefore.SalesCash==quote.TotalAmount&&cashAfter.Totals.CashRefunds-cashBefore.CashRefunds==quote.TotalAmount/2&&cashAfter.Totals.SupplierCash-cashBefore.SupplierCash==70,"Cash Closing reconciles actual collections, one refund and one supplier payment on their movement dates");
            var closing=new CashClosingRequest{RequestId=Guid.NewGuid(),LocationId=1,Date=businessDate,Fingerprint=cashAfter.Totals.Fingerprint,OpeningCash=1000,CountedCash=1000+cashAfter.Totals.NetCash};
            var movementsBeforeClosing=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");
            var closings=await Task.WhenAll(Post("/api/cashclosing",closing),Post("/api/cashclosing",closing));
            Check(closings[0]["closingId"]!.ToJsonString()==closings[1]["closingId"]!.ToJsonString(),"Concurrent Cash Closing retries save one immutable version");
            closing.CountedCash+=1;Check((await client.PostAsJsonAsync("/api/cashclosing",closing)).StatusCode==HttpStatusCode.BadRequest,"Changed cash count with reused identity rejected");closing.CountedCash-=1;
            var latePayment=new MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CASH",Amount=5};
            await Post($"/api/operations/purchases/{po}/payments",latePayment);
            var changedCash=(await cash.GetAsync(businessDate));Check(changedCash.ChangedSinceClosing&&changedCash.Closings[0].Totals.SupplierCash==cashAfter.Totals.SupplierCash,"Later cash activity flags stale closing and preserves its original totals");
            closing.RequestId=Guid.NewGuid();closing.ExpectedVersion=1;closing.Reason="P0 recount after late supplier payment";
            Check((await client.PostAsJsonAsync("/api/cashclosing",closing)).StatusCode==HttpStatusCode.BadRequest,"Stale cash movement fingerprint cannot create a new closing");
            closing.Fingerprint=changedCash.Totals.Fingerprint;closing.CountedCash=1000+changedCash.Totals.NetCash;
            Check((await Post("/api/cashclosing",closing))["version"]!.GetValue<int>()==2,"Recount appends closing version two with a reason");
            var closed=await cash.GetAsync(businessDate);Check(closed.Closings.Count==2&&!closed.ChangedSinceClosing&&closed.Closings[0].Difference==0,"Cash Closing history and latest expected cash reconcile");
            closing.RequestId=Guid.NewGuid();closing.ExpectedVersion=2;closing.LocationId=999;
            Check((await client.PostAsJsonAsync("/api/cashclosing",closing)).StatusCode==HttpStatusCode.BadRequest,"Cash Closing rejects a forged submitted location");closing.LocationId=1;
            client.DefaultRequestHeaders.Remove("Test-Role");client.DefaultRequestHeaders.Add("Test-Role","Staff");
            Check((await client.GetAsync("/api/cashclosing")).StatusCode==HttpStatusCode.Forbidden,"Restricted HTTP role cannot read cash totals");
            client.DefaultRequestHeaders.Remove("Test-Role");client.DefaultRequestHeaders.Add("Test-Role","Admin");
            Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==movementsBeforeClosing,"Closing, retries and recounts never post inventory movements");
            var count=new StockCountRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="P0 counted stock discrepancy",Items=[new(){ProductId=product,VariantId=variant,CountedQuantity=11,Remarks="Counted one fewer"}]};
            var counted=await Post("/api/operations/stock-counts",count);await Post("/api/operations/stock-counts",count);await Balance(11,$"Count {counted["id"]} posts -1 once");
            var other=await setup.QuerySingleAsync<(long Store,long Location)>("WITH s AS(INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'P0-OTHER','P0 independent fixture shop') RETURNING store_id) INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) SELECT 1,store_id,'P0 receiving counter','SALES_FLOOR' FROM s RETURNING store_id,location_id");
            await using(var sharing=factory.CreateConnection())
            {
                await sharing.ExecuteAsync("SELECT sf_share_master('CATEGORY',4,@target,true,'Authorized transfer catalog fixture'); SELECT sf_share_master('PRODUCT',@product,@target,true,'Authorized transfer product fixture')",new{target=other.Store,product});
            }
            var transfers=new TransferRepository(factory);
            var transfer=(await Post("/api/transfers",new StockFlow.Application.DTOs.Inventory.CreateTransferRequest{RequestId=Guid.NewGuid(),FromLocationId=1,ToLocationId=other.Location,Items=[new(){VariantId=variant,Quantity=3}]}))["transferId"]!.GetValue<long>();
            foreach(var action in new[]{"request","approve"})await Post($"/api/transfers/{transfer}/{action}",new StockFlow.Application.DTOs.Inventory.TransferActionRequest{RequestId=Guid.NewGuid()});
            var transferLine=(await transfers.GetAsync(transfer))!.Items.Single().TransferItemId;
            var dispatch=new StockFlow.Application.DTOs.Inventory.TransferActionRequest{RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=transferLine,Quantity=3}]};
            await Post($"/api/transfers/{transfer}/dispatch",dispatch);await Post($"/api/transfers/{transfer}/dispatch",dispatch);await Balance(8,$"Transfer {transfer} dispatch -3 once");
            scope.StoreId=other.Store;scope.LocationId=other.Location;
            Check((await cash.GetAsync(businessDate)).Closings.Count==0,"Same-profile other shop cannot read source Cash Closing history through RLS");
            await using(var otherShop=factory.CreateConnection())
            {
                var shared=await otherShop.ExecuteScalarAsync<int>("SELECT (SELECT count(*) FROM products WHERE product_id=@product)+(SELECT count(*) FROM customers WHERE customer_id=@customer)+(SELECT count(*) FROM suppliers WHERE supplier_id=@supplier)",new{product,customer,supplier});
                Check(shared==1,"Target transfer shop sees explicitly shared product but cannot read private customer or supplier masters");
            }
            Check(await sales.GetByIdAsync(saleId)==null,"Same-profile destination shop cannot read source invoice");
            await Balance(0,$"Transfer {transfer}: dispatch is not destination stock");
            var receive=new StockFlow.Application.DTOs.Inventory.TransferActionRequest{RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=transferLine,Quantity=2}]};
            await transfers.ActAsync(transfer,"receive",receive);await transfers.ActAsync(transfer,"receive",receive);await Balance(2,$"Transfer {transfer}: partial receipt +2 once, one in transit");
            Check((await transfers.GetAsync(transfer))!.Items.Single().DispatchedQuantity-(await transfers.GetAsync(transfer))!.Items.Single().ReceivedQuantity==1,"Transfer dispatch = received + in transit");
            Check(await setup.ExecuteScalarAsync<long>("SELECT coalesce(sum(CASE WHEN transaction_type='TRANSFER_IN' THEN quantity ELSE -quantity END),0) FROM inventory_transactions WHERE transfer_id=@transfer AND location_id=(SELECT transit_location_id FROM stock_transfers WHERE transfer_id=@transfer)",new{transfer})==1,"Partial transfer has one actual unit in the transit ledger");
            await transfers.ActAsync(transfer,"receive",new(){RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=transferLine,Quantity=1}]});await Balance(3,$"Transfer {transfer}: final receipt +1");
            scope.StoreId=1;scope.LocationId=1;await Balance(8,$"Transfer {transfer}: source 8 + destination 3 = original 11");
            var reviewStart=new System.Diagnostics.ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File","tests/OwnershipReviewChecks.ps1","-DatabaseName",name,"-ProductId",product.ToString(),"-CustomerId",customer.ToString(),"-SupplierId",supplier.ToString()})reviewStart.ArgumentList.Add(argument);
            using(var review=System.Diagnostics.Process.Start(reviewStart)!)
            {
                var output=review.StandardOutput.ReadToEndAsync();var error=review.StandardError.ReadToEndAsync();await review.WaitForExitAsync();
                Console.WriteLine(await output);Check(review.ExitCode==0,"Read-only ownership review tool: "+(review.ExitCode==0?"passed":await error));
            }
            var before=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions WHERE variant_id=@variant",new{variant});
            client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:VIEW");Check((await client.GetAsync($"/api/operations/sales/{saleId}")).StatusCode==HttpStatusCode.Forbidden,"Restricted HTTP role cannot read source invoice operations");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
            foreach(var profile in BusinessProfiles.Codes.Where(p=>p!="ELECTRICAL")){scope.BusinessProfileCode=profile;Check(await sales.GetByIdAsync(saleId)==null&&(await cash.GetAsync(businessDate)).Closings.Count==0,profile+" cannot read Electrical sale or Cash Closing via RLS");}scope.BusinessProfileCode="ELECTRICAL";
            scope.TenantId=999;Check(await sales.GetByIdAsync(saleId)==null,"Forged tenant cannot read source invoice");scope.TenantId=1;
            Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions WHERE variant_id=@variant",new{variant})==before,"Read and denied requests do not generate balancing movements");
            Console.WriteLine("P0 reconciliation HTTP/PostgreSQL checks passed in disposable clone; no application database used.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+name+" WITH (FORCE)");}
    }

    public static async Task RestoredApplication()
    {
        var database=Environment.GetEnvironmentVariable("STOCKFLOW_RESTORE_TEST_DATABASE")??"";
        if(!System.Text.RegularExpressions.Regex.IsMatch(database,"^stockflow_restore_test_[a-f0-9]{32}$"))throw new Exception("Only generated disposable restore databases are accepted");
        var connection="Host=127.0.0.1;Port=55444;Username=postgres;Database="+database+";Pooling=false";
        // Normal application startup and DI, with background delivery/workers disabled.
        // Authentication and physical deployment recovery remain separate acceptance gates.
        using var app=new WebApplicationFactory<global::Program>().WithWebHostBuilder(b=>{
            b.UseEnvironment("Development");b.UseContentRoot(Path.GetFullPath("src/StockFlow.Web"));
            b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",connection}}));
            b.ConfigureServices(s=>{s.RemoveAll<IHostedService>();s.AddDataProtection().UseEphemeralDataProtectionProvider();});
        });
        using var client=app.CreateClient(new(){AllowAutoRedirect=false});
        Check((await client.GetAsync("/api/health")).IsSuccessStatusCode,"Restored application starts and answers public liveness");
        using var services=app.Services.CreateScope();
        var scope=services.ServiceProvider.GetRequiredService<BusinessScope>();scope.TenantId=1;scope.UserId=1;scope.StoreId=1;scope.LocationId=1;scope.ProfileDataVisibilityAvailable=true;
        await using var admin=new NpgsqlConnection(connection);await admin.OpenAsync();scope.BusinessProfileCode=(await admin.ExecuteScalarAsync<string>("SELECT business_profile_code FROM tenants WHERE tenant_id=1"))!;
        var factory=services.ServiceProvider.GetRequiredService<DbConnectionFactory>();
        await using var runtime=factory.CreateConnection();await runtime.OpenAsync();
        Check(await runtime.ExecuteScalarAsync<string>("SELECT current_user")=="stockflow_runtime","Restored application uses its restricted database role");
        Check(await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM products")>0,"Restored application reads product data through RLS");
        var sale=await runtime.ExecuteScalarAsync<long?>("SELECT sale_id FROM sales ORDER BY sale_id LIMIT 1");
        if(sale.HasValue)Check(await services.ServiceProvider.GetRequiredService<IInvoiceRepository>().GetInvoiceAsync(sale.Value)!=null,"Restored application retrieves an existing invoice through production repository");
        else throw new Exception("Restore fixture must contain an issued invoice");
        var expenseId=Guid.Parse("11111111-1111-4111-8111-111111111111");var expenses=services.ServiceProvider.GetRequiredService<IExpenseRepository>();var restoredExpense=await expenses.GetAsync(expenseId);
        Check(restoredExpense.GetProperty("amount").GetDecimal()==25&&restoredExpense.GetProperty("paid").GetDecimal()==10,"Restored application reads the original expense and signed payment through scoped repository");
        var receipt=await expenses.AttachmentAsync(expenseId,Guid.Parse("11111111-1111-4111-8111-111111111106"));
        Check(receipt.HasValue&&System.Text.Encoding.UTF8.GetString(receipt.Value.Data)=="%PDF-1.4 synthetic restored expense receipt","Restored private expense receipt bytes match original evidence");
    }
}
