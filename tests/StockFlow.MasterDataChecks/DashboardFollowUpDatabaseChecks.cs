using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
static class DashboardFollowUpDatabaseChecks
{
    public static async Task Run(NpgsqlConnection c,IConfiguration config)
    {
        var factory=new DbConnectionFactory(config,new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1});
        var dashboard=new DashboardRepository(factory);
        var before=await dashboard.GetBusinessSummaryAsync(true);
        var overdue=await c.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,created_by,status,customer_id,subtotal,total_amount,payment_due_date) VALUES('INV-DASH-DUE',1,'COMPLETED',1,100,100,CURRENT_DATE-1) RETURNING sale_id");
        await c.ExecuteAsync("INSERT INTO payments(sale_id,payment_mode,amount,created_by) VALUES(@overdue,'CASH',30,1); INSERT INTO sale_returns(sale_id,return_number,return_total,refund_mode,refund_amount,notes,request_id,request_payload,created_by) VALUES(@overdue,'RET-DASH',20,'CASH',5,'Test',gen_random_uuid(),'{}',1)",new{overdue});
        await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status,customer_id,subtotal,total_amount,payment_due_date) VALUES('INV-DASH-UNKNOWN',1,'COMPLETED',1,40,40,NULL),('INV-DASH-TODAY',1,'COMPLETED',1,60,60,CURRENT_DATE),('INV-DASH-DRAFT',1,'DRAFT',1,80,80,CURRENT_DATE-1)");
        var after=await dashboard.GetBusinessSummaryAsync(true);
        Check(after.CustomerOutstandingAmount-before.CustomerOutstandingAmount==155 && after.OverduePaymentAmount-before.OverduePaymentAmount==55
            && after.OverdueInvoiceCount-before.OverdueInvoiceCount==1,"dashboard dues account for returns/refunds/payments; missing and today's due dates are not overdue");
        var sales=new SalesRepository(factory);
        var unpaid=await sales.GetListAsync("INV-DASH",null,0,25,null,null,"OUTSTANDING");
        var pastDue=await sales.GetListAsync("INV-DASH",null,0,25,null,null,"OVERDUE");
        Check(unpaid.Rows.Count==3 && pastDue.Rows.Count==1 && pastDue.Rows[0].OutstandingAmount==55,"dashboard due links match unpaid/overdue invoice rows and amounts");
        var receipt=await c.ExecuteScalarAsync<long>("INSERT INTO purchase_receipts(purchase_id,received_date,created_by) VALUES(1,CURRENT_DATE,1) RETURNING receipt_id");
        var second=await c.ExecuteScalarAsync<long>("INSERT INTO purchase_receipts(purchase_id,received_date,created_by) VALUES(1,CURRENT_DATE,1) RETURNING receipt_id");
        await c.ExecuteAsync("INSERT INTO inventory_transactions(product_id,variant_id,receipt_id,transaction_type,quantity,reference_type,reference_id,created_by) SELECT 1,1,@receipt,'PURCHASE',1,'PURCHASE',1,1 FROM generate_series(1,8); INSERT INTO inventory_transactions(product_id,variant_id,receipt_id,transaction_type,quantity,reference_type,reference_id,created_by) VALUES(1,1,@second,'PURCHASE',3,'PURCHASE',1,1)",new{receipt,second});
        var page=await dashboard.GetAsync(true,true);
        Check(page.RecentInflow.Any(x=>x.Quantity==8 && x.ReferenceUrl=="/StockInflow/Details/PURCHASE/"+receipt)
            && page.RecentInflow.Any(x=>x.Quantity==3 && x.ReferenceUrl=="/StockInflow/Details/PURCHASE/"+second),"serialized movements group before limit and separate partial receipts remain distinct");
        var raw=await dashboard.GetMovementsAsync(new(){Incoming=true},true,100);
        Check(raw.Count(x=>x.ReferenceUrl=="/StockInflow/Details/PURCHASE/"+receipt)==8,"detailed movement history preserves individual transactions");
        var hidden=await dashboard.GetBusinessSummaryAsync(false);
        Check(hidden.CustomerOutstandingAmount is null && hidden.OverduePaymentAmount is null,"financial permission suppresses database dues totals");
    }
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
}
