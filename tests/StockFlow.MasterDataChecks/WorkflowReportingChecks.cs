using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

// Synthetic source facts in a disposable clone. Never reads application configuration.
internal static class WorkflowReportingChecks
{
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var name="workflow_reporting_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+name+" TEMPLATE product_edit_069_test");
        try
        {
            await using var db=new NpgsqlConnection(host+name);await db.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await db.ExecuteAsync(await File.ReadAllTextAsync(file));
            var profile=(await db.ExecuteScalarAsync<string>("SELECT business_profile_code FROM tenants WHERE tenant_id=1"))!;
            await db.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code',@profile,false)",new{profile});
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true,BusinessProfileCode=profile,ProfileDataVisibilityAvailable=true};
            var factory=new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+name}}).Build(),scope);
            var repository=new OperationsRepository(factory);
            var sku=await db.QuerySingleAsync<(int Product,long Variant)>("SELECT p.product_id,v.variant_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.tenant_id=1 AND p.identifier_profile=@profile AND v.is_active ORDER BY v.variant_id LIMIT 1",new{profile});
            async Task<(long Sale,long Line)> Invoice(string number,DateTime date,int quantity,decimal price,decimal tax,decimal allowance=0,bool manual=false)
            {
                await using var tx=await db.BeginTransactionAsync();
                var sale=await db.ExecuteScalarAsync<long>("""
                    INSERT INTO sales(tenant_id,location_id,created_by,invoice_number,sale_date,subtotal,tax_amount,discount_amount,total_amount,status)
                    VALUES(1,1,1,@number,@date,@net,@tax,@allowance,@total,'COMPLETED') RETURNING sale_id
                    """,new{number,date,net=quantity*price,tax,allowance,total=quantity*price+tax-allowance},tx);
                var line=await db.ExecuteScalarAsync<long>("""
                    INSERT INTO sale_items(tenant_id,location_id,sale_id,product_id,variant_id,quantity,selling_price,tax_percentage,line_total,is_manual,product_name_snapshot,manual_tax_snapshot)
                    VALUES(1,1,@sale,@product,@variant,@quantity,@price,@rate,@total,@manual,'Saved report fixture',CASE WHEN @manual THEN public.sf_pos_manual_no_gst() ELSE NULL::jsonb END) RETURNING sale_item_id
                    """,new{sale,product=manual?(int?)null:sku.Product,variant=manual?(long?)null:sku.Variant,quantity,price,rate=quantity*price==0?0:tax/(quantity*price)*100,total=quantity*price+tax,manual},tx);
                await tx.CommitAsync();
                return(sale,line);
            }
            async Task Return((long Sale,long Line) source,DateTime date,decimal amount,decimal refund=0)
            {
                var id=await db.ExecuteScalarAsync<long>("""
                    INSERT INTO sale_returns(tenant_id,location_id,sale_id,return_number,return_date,return_total,refund_mode,refund_amount,notes,created_by,request_id,request_payload)
                    VALUES(1,1,@Sale,@number,@date,@amount,'CASH',@refund,'Synthetic report return',1,@request,'{}') RETURNING sale_return_id
                    """,new{source.Sale,number="WF-RETURN-"+Guid.NewGuid().ToString("N"),date,amount,refund,request=Guid.NewGuid()});
                await db.ExecuteAsync("""
                    INSERT INTO sale_return_items(tenant_id,location_id,sale_return_id,sale_item_id,product_id,variant_id,quantity,amount,disposition)
                    SELECT 1,1,@id,sale_item_id,product_id,variant_id,1,@amount,CASE WHEN product_id IS NULL THEN 'NON_STOCK' ELSE 'RESTOCK' END FROM sale_items WHERE sale_item_id=@Line
                    """,new{id,source.Line,amount});
            }
            async Task Receipt(DateTime date,decimal price,DateTime? recorded=null)
            {
                var supplier=await db.ExecuteScalarAsync<int>("SELECT min(supplier_id) FROM suppliers WHERE tenant_id=1");
                var purchase=await db.ExecuteScalarAsync<long>("""
                    INSERT INTO purchases(tenant_id,location_id,supplier_id,purchase_number,purchase_date,subtotal,total_amount,status,created_by)
                    VALUES(1,1,@supplier,@number,@date,@total,@total,'COMPLETED',1) RETURNING purchase_id
                    """,new{supplier,number="WF-PUR-"+Guid.NewGuid().ToString("N"),date,total=price*10});
                var item=await db.ExecuteScalarAsync<long>("""
                    INSERT INTO purchase_items(tenant_id,location_id,purchase_id,product_id,variant_id,quantity,received_quantity,purchase_price,line_total)
                    VALUES(1,1,@purchase,@Product,@Variant,10,10,@price,@total) RETURNING purchase_item_id
                    """,new{purchase,sku.Product,sku.Variant,price,total=price*10});
                var receipt=await db.ExecuteScalarAsync<long>("""
                    INSERT INTO purchase_receipts(tenant_id,location_id,purchase_id,received_date,created_at,receipt_number,created_by)
                    VALUES(1,1,@purchase,@date,@recorded,@number,1) RETURNING receipt_id
                    """,new{purchase,date,recorded=recorded??date,number="WF-GRN-"+Guid.NewGuid().ToString("N")});
                await db.ExecuteAsync("""
                    INSERT INTO purchase_receipt_items(tenant_id,location_id,receipt_id,purchase_item_id,quantity,purchase_price,tax_percentage)
                    VALUES(1,1,@receipt,@item,10,@price,18)
                    """,new{receipt,item,price});
            }
            var first=new DateTime(2003,1,10,12,0,0);var knownDate=first.AddDays(2);var returnsDate=first.AddDays(3);
            var unknown=await Invoice("WF-UNKNOWN",first,1,100,18);
            var manual=await Invoice("WF-MANUAL",first,1,10,0,manual:true);
            var noCost=(await repository.ProductSalesAsync(first,first)).Single(r=>r.VariantId==sku.Variant);
            Check(noCost.NetSales==100&&noCost.EstimatedCost is null&&noCost.EstimatedMargin is null,"missing receipt evidence is unknown, never zero-cost margin; saved tax excluded");
            await Receipt(first.AddDays(1),60);
            var known=await Invoice("WF-KNOWN",knownDate,2,100,36,10);
            var before=(await repository.ProductSalesAsync(knownDate,knownDate)).Single(r=>r.VariantId==sku.Variant);
            Check(before.QuantitySold==2&&before.NetSales==190&&before.EstimatedCost==120&&before.EstimatedMargin==70,"legacy after-tax discount is included once; purchase-price estimate and tax-exclusive margin agree");
            await Receipt(first.AddDays(4),900);
            await Receipt(first.AddDays(1),500,first.AddDays(5));
            var after=(await repository.ProductSalesAsync(knownDate,knownDate)).Single(r=>r.VariantId==sku.Variant);
            Check(after.NetSales==before.NetSales&&after.EstimatedCost==before.EstimatedCost,"later and later-recorded backdated receipts do not reprice an older period");
            await db.ExecuteAsync("INSERT INTO payments(tenant_id,location_id,sale_id,payment_mode,amount,created_by,payment_date) VALUES(1,1,@Sale,'CASH',226,1,@knownDate)",new{known.Sale,knownDate});
            await Return(known,returnsDate,118,118);await Return(manual,returnsDate,10);
            var returned=(await repository.ProductSalesAsync(returnsDate,returnsDate)).ToArray();
            var returnedSku=returned.Single(r=>r.VariantId==sku.Variant);
            Check(returnedSku.QuantitySold==0&&returnedSku.QuantityReturned==1&&returnedSku.NetSales==-100&&returnedSku.EstimatedCost==-60,"return-only SKU remains visible with negative quantity and original-sale cost evidence");
            Check(returned.Single(r=>r.ProductId is null).NetSales==-10&&returned.Single(r=>r.ProductId is null).EstimatedCost is null,"prior-period manual return remains visible without inventing product identity or cost");
            await db.ExecuteAsync("""
                INSERT INTO payments(tenant_id,location_id,sale_id,payment_mode,amount,created_by,payment_date)
                VALUES(1,1,@Sale,'CREDIT',118,1,@first),(1,1,@Sale,'CASH',40,1,@date),(1,1,@Sale,'UPI',10,1,@date),
                  (1,1,@Sale,'CASH',5,1,@future)
                """,new{unknown.Sale,first,date=returnsDate,future=returnsDate.Date.AddDays(1)});
            var reports=new ReportingRepository(factory,scope);
            var daily=(await reports.FinancialAsync(returnsDate,returnsDate)).Single();
            Check(daily.InvoiceCount==0&&daily.Collections==-68&&daily.SalesValue==-128,"old-invoice cash/digital collections and later refund stay on their dates; returns reduce sales separately");
            var returnsBefore=returnedSku.NetSales;
            Check((await repository.ProductSalesAsync(returnsDate,returnsDate)).Single(r=>r.VariantId==sku.Variant).NetSales==returnsBefore,"payment collection never creates product revenue");
            var sourceCounts=await db.ExecuteScalarAsync<string>("SELECT jsonb_build_array((SELECT count(*) FROM sales),(SELECT count(*) FROM payments),(SELECT count(*) FROM inventory_transactions))::text");
            var branches=new BranchRepository(factory,scope);
            var second=await branches.CreateStoreAsync(new(){StoreCode="WF2",StoreName="Workflow other shop"});
            var secondLocation=(await branches.LocationsAsync()).Single(l=>l.StoreId==second).LocationId;
            scope.StoreId=second;scope.LocationId=secondLocation;
            Check(!(await repository.ProductSalesAsync(first,returnsDate)).Any(),"actual same-profile second shop cannot read first-shop report");scope.StoreId=1;scope.LocationId=1;
            scope.TenantId=999;Check(!(await repository.ProductSalesAsync(first,returnsDate)).Any(),"report cannot read another tenant context");scope.TenantId=1;
            foreach(var other in new[]{"ELECTRICAL","BIKE_ACCESSORIES","CLOTHING","FOOTWEAR"}.Where(p=>p!=profile))
            {scope.BusinessProfileCode=other;Check(!(await repository.ProductSalesAsync(first,returnsDate)).Any(),"report cannot cross profile into "+other);}
            scope.BusinessProfileCode=profile;
            Check(sourceCounts==await db.ExecuteScalarAsync<string>("SELECT jsonb_build_array((SELECT count(*) FROM sales),(SELECT count(*) FROM payments),(SELECT count(*) FROM inventory_transactions))::text"),"read-only reports do not create invoices, collections or movements");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+name+" WITH (FORCE)");}
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
