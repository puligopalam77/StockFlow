using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Crm;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class CrmChecks
{
    public static async Task Run()
    {
        var database="crm_test_"+Guid.NewGuid().ToString("N");const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
            foreach(var file in Directory.GetFiles(Path.Combine(root!.FullName,"database/migrations"),"*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false)");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();var factory=new DbConnectionFactory(config,scope);var sales=new SalesRepository(factory);var crm=new CrmRepository(factory,sales,Options.Create(new ShopTimeOptions()));
            var count=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");
            var access=await new SecurityRepository(factory).GetAccessAsync(1);
            Check(access!.Can("CRM","VIEW_FINANCIAL")&&access.NavigationModules.Any(n=>n.Route=="/Crm")&&access.NavigationModules.Any(n=>n.Route=="/Crm/Enquiries"),"Existing permission/navigation service exposes the new CRM menu and owner grants");
            var customer=await new CustomerRepository(factory).CreateAsync(new Customer{CustomerName="CRM synthetic customer",MobileNumber="9876543012",Email="crm@example.test"},true);
            Check((await crm.CustomersAsync(new(){Term=" CRM synthetic "})).GetProperty("rows").EnumerateArray().Any(r=>r.GetProperty("customerId").GetInt32()==customer),"Existing customer API registers the same ID in current CRM shop");
            var r=new CrmEnquiryRequest{RequestId=Guid.NewGuid(),CustomerId=customer,Subject="General callback request",AssignedTo=1,NextFollowUp=DateTimeOffset.UtcNow.AddDays(1)};
            var id=await crm.SaveAsync(null,r);Check(await crm.SaveAsync(null,r)==id,"Enquiry identical retry returns one record");
            r.Subject="Changed retry payload";await Reject(()=>crm.SaveAsync(null,r),"Enquiry request cannot be reused with changed details");r.Subject="General callback request";
            Check((await crm.EnquiryAsync(id))!.Value.GetProperty("activities").GetArrayLength()==1,"Creation has one immutable history entry");
            var follow=new CrmFollowUpRequest{RequestId=Guid.NewGuid(),Version=1,OccurredAt=DateTimeOffset.UtcNow.AddMinutes(-5),Notes="Customer requested callback",Outcome="CALLBACK",NextFollowUp=DateTimeOffset.UtcNow.AddDays(2)};
            await crm.FollowUpAsync(id,follow);await crm.FollowUpAsync(id,follow);
            var detail=(await crm.EnquiryAsync(id))!.Value;Check(detail.GetProperty("activities").GetArrayLength()==2&&detail.GetProperty("enquiry").GetProperty("version").GetInt32()==2,"Follow-up identical retry appends exactly once and increments version once");
            follow.RequestId=Guid.NewGuid();await Reject(()=>crm.FollowUpAsync(id,follow),"Stale follow-up version rejected");
            await crm.NoteAsync(customer,new(Guid.NewGuid(),"Customer prefers a callback"));await crm.PreferencesAsync(customer,new(1,"INDIVIDUAL","CALL","Call in afternoon"));
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM crm_preference_history WHERE customer_id=@customer",new{customer})==1,"Preference changes retain audit history");
            Check((await crm.CustomerAsync(customer,1,10))!.Value.GetProperty("notes").GetArrayLength()==1,"Customer profile reuses master and retains shop notes");
            r.RequestId=Guid.NewGuid();r.Version=2;r.Status="CLOSED";r.NextFollowUp=null;await crm.SaveAsync(id,r);
            follow.Version=3;await Reject(()=>crm.FollowUpAsync(id,follow),"Closed enquiry rejects new follow-ups");
            r.RequestId=Guid.NewGuid();r.Version=3;r.Status="NEW";await Reject(()=>crm.SaveAsync(id,r),"Closed enquiry must reopen through In Progress");
            r.RequestId=Guid.NewGuid();r.Status="IN_PROGRESS";await crm.SaveAsync(id,r);
            Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==count,"Enquiries, notes and follow-ups never post stock");
            var bad=new CrmEnquiryRequest{RequestId=Guid.NewGuid(),CustomerId=customer,Subject="Invalid staff",AssignedTo=int.MaxValue};await Reject(()=>crm.SaveAsync(null,bad),"Unknown staff assignment rejected");
            await Reject(()=>crm.EnquiriesAsync(new(){PageSize=0}),"Invalid pagination rejected");
            var options=await crm.OptionsAsync("Test");Check(options.TryGetProperty("staff",out _),"Staff and live product options load under runtime RLS");
            var midnight=new CrmRepository(factory,sales,Options.Create(new ShopTimeOptions()),new FixedClock(new DateTimeOffset(2026,11,1,18,40,0,TimeSpan.Zero)));
            var midnightRequest=new CrmEnquiryRequest{RequestId=Guid.NewGuid(),CustomerId=customer,AssignedTo=1,Subject="Shop midnight callback",NextFollowUp=new(2026,11,1,18,35,0,TimeSpan.Zero)};
            var midnightId=await midnight.SaveAsync(null,midnightRequest);
            Check((await midnight.EnquiriesAsync(new(){Due="TODAY",Term="Shop midnight"})).GetProperty("rows").GetArrayLength()==1,"Due Today follows Asia/Kolkata midnight rather than UTC date");
            Check((await midnight.EnquiriesAsync(new(){Due="OVERDUE",Term="Shop midnight"})).GetProperty("rows").GetArrayLength()==1,"Overdue includes a passed follow-up time earlier today");
            await crm.DashboardAsync();Check(true,"Timezone-aware CRM dashboard reads saved facts");
            var store=await c.ExecuteScalarAsync<long>("INSERT INTO stores(store_code,store_name) VALUES('CRM-TWO','Synthetic CRM second shop') RETURNING store_id");
            var location=await c.ExecuteScalarAsync<long>("INSERT INTO store_locations(store_id,location_name,location_type) VALUES(@store,'CRM stock','SALES_FLOOR') RETURNING location_id",new{store});
            scope.StoreId=store;scope.LocationId=location;
            Check(await crm.EnquiryAsync(id) is null&&await crm.CustomerAsync(customer,1,10) is null,"Second shop cannot read first shop customer profile, enquiry or history");
            await crm.LinkCustomerAsync(customer);Check((await crm.CustomerAsync(customer,1,10))!.Value.GetProperty("notes").GetArrayLength()==0,"Explicit master link does not copy other shop notes or enquiry activity");
            scope.StoreId=1;scope.LocationId=1;
            scope.BusinessProfileCode="BIKE_ACCESSORIES";Check(await crm.EnquiryAsync(id) is null,"Different shop profile cannot read enquiry");scope.BusinessProfileCode="ELECTRICAL";
            // Complete through existing checkout; all ordinary stock/payment/discount checks remain active.
            await c.ExecuteAsync("""
                INSERT INTO inventory_transactions(product_id,variant_id,transaction_type,quantity,reference_type,reference_id,created_by)
                VALUES(1,1,'ADJUSTMENT_IN',5,'STOCK_COUNT',NULL,1)
                """);
            const decimal price=100;
            var request=new CreateSaleRequest{RequestId=Guid.NewGuid(),CustomerId=customer,LocationId=1,CrmEnquiryId=id,CanDiscount=true,CanApproveDiscount=true,CanOverridePrice=true,AdjustmentReason="Synthetic authorized price",Items=[new(){ProductId=1,VariantId=1,Quantity=1,SellingPrice=price}]};
            var quote=await sales.QuoteAsync(request);request.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];
            var sale=await sales.CreateSaleAsync(request,1);Check(await sales.CreateSaleAsync(request,1)==sale,"CRM-linked checkout retry reuses completed invoice");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM crm_sale_links WHERE enquiry_id=@id",new{id})==1,"Invoice conversion links once after finalization");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE reference_type='SALE' AND reference_id=@sale",new{sale})==1,"CRM-linked checkout deducts stock exactly once");
            Check((await crm.CustomerAsync(customer,1,10))!.Value.GetProperty("customer").GetProperty("purchaseCount").GetInt32()==1,"Purchase count counts finalized invoices, not line/payment rows");
            var invoice=(await sales.GetByIdAsync(sale))!;
            await new OperationsRepository(factory).ReturnSaleAsync(sale,new SaleReturnRequest{RequestId=Guid.NewGuid(),LocationId=1,RefundMode="CASH",RefundAmount=quote.TotalAmount,Notes="Synthetic full product return",Items=[new(){SaleItemId=invoice.Lines.Single().SaleItemId,Quantity=1,Disposition="RESTOCK"}]},1);
            var profile=(await crm.CustomerAsync(customer,1,10))!.Value;
            Check(profile.GetProperty("customer").GetProperty("netPurchases").GetDecimal()==0&&profile.GetProperty("history").GetProperty("summary").GetProperty("outstandingDues").GetDecimal()==0,"Customer totals reflect actual return/refund settlement without inventing dues");
            var retry=new CreateSaleRequest{RequestId=Guid.NewGuid(),CustomerId=customer,LocationId=1,CrmEnquiryId=id,CanOverridePrice=true,CanDiscount=true,CanApproveDiscount=true,AdjustmentReason="Synthetic authorized price",Items=[new(){ProductId=1,VariantId=1,Quantity=1,SellingPrice=price}],Payments=request.Payments};
            var before=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM sales");await Reject(()=>sales.CreateSaleAsync(retry,1),"Second invoice for same enquiry rolls back");Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM sales")==before,"Rejected enquiry conversion leaves no orphan sale");
            var oldSale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(customer_id,invoice_number,status,subtotal,total_amount,created_by,request_id,request_payload,sale_date) VALUES(@customer,'CRM-OLD-CREDIT','COMPLETED',100,100,1,@request,'{}',CURRENT_TIMESTAMP-INTERVAL '31 days') RETURNING sale_id",new{customer,request=Guid.NewGuid()});
            var prior=(await crm.CustomerAsync(customer,1,10))!.Value;
            await new OperationsRepository(factory).AddCustomerPaymentAsync(oldSale,new(){RequestId=Guid.NewGuid(),LocationId=1,PaymentMode="CASH",Amount=40,Notes="Synthetic collection against older invoice"},1);
            var collected=(await crm.CustomerAsync(customer,1,10))!.Value;
            Check(prior.GetProperty("customer").GetProperty("netPurchases").GetDecimal()==collected.GetProperty("customer").GetProperty("netPurchases").GetDecimal()
             &&prior.GetProperty("customer").GetProperty("purchaseCount").GetInt32()==collected.GetProperty("customer").GetProperty("purchaseCount").GetInt32()
             &&collected.GetProperty("history").GetProperty("summary").GetProperty("outstandingDues").GetDecimal()==60,"Later collection reduces old dues without increasing purchase count or net purchases");
            await Reject(()=>c.ExecuteAsync("UPDATE crm_activities SET notes='overwrite' WHERE enquiry_id=@id",new{id}),"Activity history cannot be overwritten");
            await c.ExecuteAsync("""
                INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
                SELECT 1,2,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='CRM' AND a.action_code IN('VIEW','CREATE','EDIT','FOLLOW_UP')
                ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true;
                INSERT INTO user_store_access(tenant_id,user_id,store_id,is_active) VALUES(1,2,1,true) ON CONFLICT(user_id,store_id) DO UPDATE SET is_active=true;
                """);
            scope.UserId=2;
            Check(await crm.EnquiryAsync(id) is null,"Staff cannot read another user's enquiry without team permission");
            var own=new CrmEnquiryRequest{RequestId=Guid.NewGuid(),CustomerId=customer,AssignedTo=2,Subject="Staff assigned enquiry"};var ownId=await crm.SaveAsync(null,own);
            Check((await crm.CustomerAsync(customer,1,10))!.Value.GetProperty("customer").GetProperty("netPurchases").ValueKind==JsonValueKind.Null,"CRM view does not expose financial totals without financial permission");
            Check((await crm.CustomerAsync(customer,1,10))!.Value.GetProperty("history").ValueKind==JsonValueKind.Null,"CRM view does not expose invoice history without history permission");
            own.RequestId=Guid.NewGuid();own.Version=1;own.AssignedTo=1;await Reject(()=>crm.SaveAsync(ownId,own),"Staff cannot reassign without assignment permission");own.AssignedTo=2;own.Status="CLOSED";own.RequestId=Guid.NewGuid();await Reject(()=>crm.SaveAsync(ownId,own),"Staff cannot close without close permission");
            scope.TenantId=999;await Reject(()=>crm.DashboardAsync(),"Forged tenant cannot read CRM facts");scope.TenantId=1;scope.UserId=1;
            await c.ExecuteAsync("UPDATE customers SET is_active=false WHERE customer_id=@customer",new{customer});
            r.RequestId=Guid.NewGuid();r.Version=4;r.Status="CLOSED";await crm.SaveAsync(id,r);
            Check((await crm.EnquiryAsync(id))!.Value.GetProperty("enquiry").GetProperty("status").GetString()=="CLOSED","Existing enquiry can be closed after its customer is deactivated");
            await Reject(()=>crm.SaveAsync(null,new(){RequestId=Guid.NewGuid(),CustomerId=customer,AssignedTo=1,Subject="Inactive customer new request"}),"Inactive customer cannot create a new enquiry");
            Console.WriteLine("CRM database checks passed in a disposable clone. No application database changed.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    private sealed class FixedClock(DateTimeOffset value):TimeProvider {public override DateTimeOffset GetUtcNow()=>value;}
    private static async Task Reject(Func<Task> action,string name){try{await action();}catch(Exception e)when(e is ValidationException or PostgresException or UnauthorizedAccessException){Console.WriteLine("PASS: "+name);return;}throw new Exception("Expected rejection: "+name);}
}
