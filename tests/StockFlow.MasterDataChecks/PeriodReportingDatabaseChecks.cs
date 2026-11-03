using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
static class PeriodReportingDatabaseChecks
{
    public static async Task Run(NpgsqlConnection c,DbConnectionFactory factory,BusinessScope scope,long secondStore,long secondLocation)
    {
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        scope.UserId=1;scope.IsOwner=true;scope.CanReviewStores=true;scope.StoreId=1;scope.LocationId=1;scope.Consolidated=false;
        await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false)");
        var product=await c.ExecuteScalarAsync<int>("INSERT INTO products(tenant_id,category_id,product_name,sku,selling_price) VALUES(1,1,'Report fixture','REPORT',100) RETURNING product_id");
        var variant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@product",new{product});
        var args=new{product,variant,secondLocation};
        await c.ExecuteAsync("""
            INSERT INTO inventory_transactions(tenant_id,location_id,product_id,variant_id,transaction_type,quantity,reference_type,created_by,created_at) VALUES
            (1,1,@product,@variant,'PURCHASE',10,'PURCHASE',1,'2026-03-01 23:59:59'),
            (1,1,@product,@variant,'ADJUSTMENT_IN',3,'OPENING_STOCK',1,'2026-03-02 00:00:00'),
            (1,1,@product,@variant,'PURCHASE',5,'PURCHASE',1,'2026-03-02 10:00:00'),
            (1,1,@product,@variant,'SALE',2,'SALE',1,'2026-03-02 11:00:00'),
            (1,1,@product,@variant,'ADJUSTMENT_IN',2,'ADJUSTMENT',1,'2026-03-02 12:00:00'),
            (1,1,@product,@variant,'ADJUSTMENT_OUT',1,'ADJUSTMENT',1,'2026-03-02 13:00:00'),
            (1,1,@product,@variant,'CUSTOMER_RETURN',2,'RETURN',1,'2026-03-02 23:59:59'),
            (1,1,@product,@variant,'PURCHASE',99,'PURCHASE',1,'2026-03-03 00:00:00'),
            (1,@secondLocation,@product,@variant,'PURCHASE',7,'PURCHASE',1,'2026-03-02 10:00:00');
            """,args);
        var old=await c.ExecuteScalarAsync<long>("""
            INSERT INTO sales(tenant_id,location_id,created_by,invoice_number,sale_date,subtotal,total_amount,status)
            VALUES(1,1,1,'REPORT-OLD','2026-03-01 10:00:00',100,100,'COMPLETED') RETURNING sale_id
            """);
        var sale=await c.ExecuteScalarAsync<long>("""
            INSERT INTO sales(tenant_id,location_id,created_by,invoice_number,sale_date,subtotal,total_amount,status)
            VALUES(1,1,1,'REPORT-NEW','2026-03-02 11:00:00',200,200,'COMPLETED') RETURNING sale_id
            """);
        await c.ExecuteAsync("""
            UPDATE inventory_transactions SET reference_id=@sale WHERE product_id=@product AND transaction_type='SALE';
            INSERT INTO sale_items(tenant_id,location_id,sale_id,product_id,variant_id,quantity,selling_price,line_total) VALUES
            (1,1,@old,@product,@variant,1,100,100),(1,1,@sale,@product,@variant,2,100,200);
            INSERT INTO payments(tenant_id,location_id,sale_id,payment_mode,amount,created_by,payment_date) VALUES
            (1,1,@old,'CREDIT',100,1,'2026-03-01 10:00:00'),(1,1,@old,'CASH',40,1,'2026-03-02 09:00:00'),
            (1,1,@sale,'CASH',50,1,'2026-03-02 11:00:00'),(1,1,@sale,'CREDIT',150,1,'2026-03-02 11:00:00'),
            (1,1,@sale,'CASH',20,1,'2026-03-03 00:00:00');
            """,new{old,sale,product,variant});
        var reports=new ReportingRepository(factory,scope);var date=new DateTime(2026,3,2);
        var report=await reports.ActivityAsync(date,date,true,true);var stock=report.Stock.Single(r=>r.VariantId==variant);var financial=report.Financial.Single();
        Check(stock.Opening==10&&stock.OpeningAdded==3&&stock.Receipts==5&&stock.Sales==2&&stock.AdjustmentIn==2&&stock.AdjustmentOut==1&&stock.OtherIn==2&&stock.Closing==19&&stock.Reconciles,"stock date boundaries and opening/closing categories reconcile");
        Check(financial.SalesValue==200&&financial.InvoiceQuantity==2&&financial.Collections==90&&financial.CreditOutstanding==210&&financial.Reconciles,"period collections include older invoices and exclude credit promises/future payments");
        Check(report.Credit.Sum(r=>r.Outstanding)==210&&report.Credit.Count==2,"credit invoice list reconciles with outstanding summary");
        await c.ExecuteAsync("UPDATE inventory_transactions SET created_at='2026-03-03 11:00:00' WHERE product_id=@product AND transaction_type='SALE'",args);
        Check(!(await reports.ActivityAsync(date,date,false,true)).Financial.Single().Reconciles,"invoice check flags a movement posted outside invoice dates");
        await c.ExecuteAsync("UPDATE inventory_transactions SET created_at='2026-03-02 11:00:00' WHERE product_id=@product AND transaction_type='SALE'",args);
        var later=await reports.ActivityAsync(date,date.AddDays(1),false,true);
        Check(later.Financial.Single().CreditOutstanding==190&&later.Financial.Single().Collections==110&&later.Financial.Single().Reconciles,"later actual payments reduce credit without double counting original credit entries");
        Check((await reports.ActivityAsync(date,date,true,false)).Financial.Count==0&&(await reports.ActivityAsync(date,date,false,true)).Stock.Count==0,"repository skips unauthorized financial or stock queries");
        scope.LocationId=secondLocation;scope.StoreId=secondStore;
        report=await reports.ActivityAsync(date,date,true,true);Check(report.Stock.Single(r=>r.VariantId==variant).Closing==7&&!report.Financial.Any(),"individual store reporting hides other store invoices and movements");
        scope.LocationId=0;scope.StoreId=0;scope.Consolidated=true;
        report=await reports.ActivityAsync(date,date,true,true);Check(report.Stock.Where(r=>r.VariantId==variant).Sum(r=>r.Closing)==26&&report.Financial.Single().CreditOutstanding==210,"consolidated report sums authorized store balances");
        report=await reports.ActivityAsync(DateTime.Today.AddYears(-9),DateTime.Today.AddDays(1),true,false);
        Check(report.Stock.All(r=>r.Reconciles)&&report.Stock.Any(r=>r.TransferOut>0)&&report.Stock.Any(r=>r.TransferIn>0),"real transfer movements reconcile separately from receipts and sales");
        scope.LocationId=1;scope.StoreId=1;scope.Consolidated=false;
    }
}
