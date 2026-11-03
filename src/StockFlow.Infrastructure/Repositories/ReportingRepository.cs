using System.ComponentModel.DataAnnotations;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Reports;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class ReportingRepository : IReportingRepository
{
    private readonly DbConnectionFactory factory;
    private readonly BusinessScope scope;
    // ASP.NET supplies these services when it creates this class.
    public ReportingRepository(DbConnectionFactory factory, BusinessScope scope)
    {
        this.factory = factory;
        this.scope = scope;
    }

    public Task<ActivityReport> ActivityAsync(DateTime from, DateTime to, bool stock, bool sales) => ActivityCoreAsync(from, to, stock, sales, false);

    public async Task<List<FinancialPeriodRow>> FinancialAsync(DateTime from, DateTime to) =>
        (await ActivityCoreAsync(from, to, false, true, true)).Financial;

    private async Task<ActivityReport> ActivityCoreAsync(DateTime from, DateTime to, bool stock, bool sales, bool financialOnly)
    {
        if (from.Date > to.Date || to.Date == DateTime.MaxValue.Date || (to.Date - from.Date).TotalDays > 3660)
        {
            throw new ValidationException("Choose a valid date range of no more than ten years.");
        }

        var result = new ActivityReport
        {
            From = from.Date,
            To = to.Date,
            Scope = scope.LocationName,
            CanSeeStock = stock,
            CanSeeSales = sales
        };
        var args = new
        {
            Start = from.Date,
            End = to.Date.AddDays(1)
        };
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await c.ExecuteAsync("SET TRANSACTION READ ONLY", transaction: tx);
        if (stock)
        {
            result.Stock = (await c.QueryAsync<StockPeriodRow>("""
            WITH movements AS (
              SELECT it.*,CASE WHEN transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN quantity::bigint
                WHEN transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -quantity::bigint ELSE 0 END signed
              FROM inventory_transactions it WHERE created_at<@End
            )
            SELECT m.location_id LocationId,m.variant_id VariantId,coalesce(s.store_name,'Business / transit') StoreName,l.location_name LocationName,
              coalesce(p.product_name||' / '||v.variant_name,'Variant #'||m.variant_id||' (ownership review pending)') ProductName,
              coalesce(sum(signed) FILTER(WHERE m.created_at<@Start),0) Opening,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='ADJUSTMENT_IN' AND reference_type='OPENING_STOCK'),0) OpeningAdded,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='PURCHASE'),0) Receipts,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='SALE'),0) Sales,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='ADJUSTMENT_IN' AND reference_type IS DISTINCT FROM 'OPENING_STOCK'),0) AdjustmentIn,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type IN('ADJUSTMENT_OUT','DAMAGED')),0) AdjustmentOut,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='TRANSFER_IN'),0) TransferIn,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='TRANSFER_OUT'),0) TransferOut,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type='TRANSFER_LOSS'),0) TransferLoss,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type IN('CUSTOMER_RETURN','SERVICE_IN')),0) OtherIn,
              coalesce(sum(quantity::bigint) FILTER(WHERE m.created_at>=@Start AND transaction_type IN('SUPPLIER_RETURN','SERVICE_OUT','WARRANTY_REPLACEMENT')),0) OtherOut,
              sum(signed) Closing,count(*) FILTER(WHERE quantity<=0 OR transaction_type NOT IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN','SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS')) InvalidMovements
            FROM movements m JOIN store_locations l ON l.location_id=m.location_id LEFT JOIN product_variants v ON v.variant_id=m.variant_id
            LEFT JOIN products p ON p.product_id=v.product_id LEFT JOIN stores s ON s.store_id=l.store_id
            GROUP BY m.location_id,m.variant_id,s.store_name,l.location_name,p.product_name,v.variant_name
            ORDER BY StoreName,LocationName,ProductName,m.variant_id
            """,
args,
tx)).ToList();
        }

        if (sales)
        {
            result.Financial = (await c.QueryAsync<FinancialPeriodRow>("""
                WITH sold AS (
                 SELECT s.location_id,count(*) invoice_count,coalesce(sum(s.total_amount),0) sales_value,
                  coalesce(sum((SELECT sum(i.quantity) FROM sale_items i WHERE i.sale_id=s.sale_id)),0)::bigint invoice_qty,
                  coalesce(sum((SELECT sum(i.quantity) FROM sale_items i WHERE i.sale_id=s.sale_id AND coalesce((to_jsonb(i)->>'is_manual')::boolean,false))),0)::bigint nonstock_qty,
                  coalesce(sum((SELECT sum(i.line_total) FROM sale_items i WHERE i.sale_id=s.sale_id)),0) item_value
                 FROM sales s WHERE s.status='COMPLETED' AND s.sale_date>=@Start AND s.sale_date<@End GROUP BY s.location_id
                ), returned AS (
                 SELECT r.location_id,coalesce(sum(r.return_total),0) return_value,coalesce(sum((SELECT sum(i.quantity) FROM sale_return_items i WHERE i.sale_return_id=r.sale_return_id)),0)::bigint return_qty,
                  coalesce(sum((SELECT sum(i.quantity) FROM sale_return_items i WHERE i.sale_return_id=r.sale_return_id AND i.product_id IS NULL)),0)::bigint nonstock_qty
                 FROM sale_returns r
                 WHERE r.return_date>=@Start AND r.return_date<@End GROUP BY r.location_id
                ), ledger AS (
                 SELECT location_id,coalesce(sum(CASE WHEN transaction_type='SALE' THEN quantity WHEN transaction_type='CUSTOMER_RETURN' AND reference_type='SALE_RETURN' THEN -quantity ELSE 0 END),0)::bigint qty
                 FROM inventory_transactions WHERE created_at>=@Start AND created_at<@End AND (transaction_type='SALE' OR transaction_type='CUSTOMER_RETURN' AND reference_type='SALE_RETURN') GROUP BY location_id
                ), receipts AS (
                 SELECT location_id,sum(amount) amount FROM (
                  SELECT s.location_id,p.amount FROM payments p JOIN sales s ON s.sale_id=p.sale_id WHERE s.status='COMPLETED' AND p.payment_mode<>'CREDIT' AND p.payment_date>=@Start AND p.payment_date<@End
                  UNION ALL SELECT location_id,-refund_amount FROM sale_returns WHERE refund_amount>0 AND return_date>=@Start AND return_date<@End
                 ) x GROUP BY location_id
                ), outstanding AS (
                 SELECT s.location_id,sum(greatest(s.total_amount
                  -coalesce((SELECT sum(r.return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date<@End),0)
                  -coalesce((SELECT sum(p.amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT' AND p.payment_date<@End),0)
                  +coalesce((SELECT sum(r.refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date<@End),0),0)) amount
                 FROM sales s WHERE s.status='COMPLETED' AND s.sale_date<@End GROUP BY s.location_id
                ), locations AS (SELECT location_id FROM sold UNION SELECT location_id FROM returned UNION SELECT location_id FROM receipts UNION SELECT location_id FROM outstanding)
                SELECT x.location_id LocationId,coalesce(a.invoice_count,0) InvoiceCount,coalesce(a.invoice_qty,0)-coalesce(rt.return_qty,0) InvoiceQuantity,
                 coalesce(g.qty,0) LedgerSalesQuantity,coalesce(a.nonstock_qty,0)-coalesce(rt.nonstock_qty,0) NonStockQuantity,coalesce(a.sales_value,0)-coalesce(rt.return_value,0) SalesValue,
                 0 InvoiceCollected,0 InvoiceCredit,coalesce(a.item_value,0)-coalesce(rt.return_value,0) InvoiceItemValue,
                 coalesce(o.amount,0) CreditOutstanding,coalesce(r.amount,0) Collections,coalesce(st.store_name,'Business') StoreName,l.location_name LocationName
                FROM locations x JOIN store_locations l ON l.location_id=x.location_id LEFT JOIN stores st ON st.store_id=l.store_id
                 LEFT JOIN sold a ON a.location_id=x.location_id LEFT JOIN returned rt ON rt.location_id=x.location_id LEFT JOIN ledger g ON g.location_id=x.location_id
                 LEFT JOIN receipts r ON r.location_id=x.location_id LEFT JOIN outstanding o ON o.location_id=x.location_id ORDER BY StoreName,LocationName
                """,
args,
tx)).ToList();
            if (financialOnly)
            {
                await tx.CommitAsync();
                return result;
            }
            result.Credit = (await c.QueryAsync<CreditPeriodRow>("""
                SELECT s.sale_id SaleId,s.invoice_number InvoiceNumber,coalesce(s.customer_name_snapshot,c.customer_name,'Walk-in') CustomerName,
                  l.location_name LocationName,s.sale_date SaleDate,
                  greatest(s.total_amount-coalesce((SELECT sum(r.return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date<@End),0)
                   -coalesce((SELECT sum(p.amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT' AND p.payment_date<@End),0)
                   +coalesce((SELECT sum(r.refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date<@End),0),0) Outstanding
                FROM sales s JOIN store_locations l ON l.location_id=s.location_id LEFT JOIN customers c ON c.customer_id=s.customer_id
                WHERE s.status='COMPLETED' AND s.sale_date<@End
                 AND s.total_amount-coalesce((SELECT sum(r.return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date<@End),0)
                  -coalesce((SELECT sum(p.amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT' AND p.payment_date<@End),0)
                  +coalesce((SELECT sum(r.refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id AND r.return_date<@End),0)>0
                ORDER BY s.sale_date,s.sale_id
                """,
args,
tx)).ToList();
        }

        if (stock)
        {
            result.CurrentStock = (await c.QueryAsync<ShopStockRow>("""
                SELECT p.product_name||' / '||v.variant_name ProductName,s.current_stock::bigint CurrentStock,
                 sf_variant_minimum(v.variant_id,v.minimum_stock_level)::integer MinimumStock
                FROM product_variants v JOIN products p ON p.product_id=v.product_id JOIN vw_variant_current_stock s ON s.variant_id=v.variant_id
                WHERE p.is_active AND v.is_active
                 AND EXISTS(SELECT 1 FROM categories cc WHERE cc.category_id=p.category_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')) ORDER BY ProductName
                """, transaction:tx)).ToList();
        }
        if (sales)
        {
            result.Trend = (await c.QueryAsync<ShopTrendRow>("""
                WITH values_by_day AS (
                 SELECT sale_date::date bucket_date,total_amount sale,0::numeric collection FROM sales WHERE status='COMPLETED' AND sale_date>=@Start AND sale_date<@End
                 UNION ALL SELECT return_date::date,-return_total,-refund_amount FROM sale_returns WHERE return_date>=@Start AND return_date<@End
                 UNION ALL SELECT p.payment_date::date,0,p.amount FROM payments p JOIN sales s ON s.sale_id=p.sale_id
                  WHERE s.status='COMPLETED' AND p.payment_mode<>'CREDIT' AND p.payment_date>=@Start AND p.payment_date<@End
                ) SELECT bucket_date::timestamp AS "Day",sum(sale) Sales,sum(collection) Collections FROM values_by_day GROUP BY bucket_date ORDER BY bucket_date
                """,args,tx)).ToList();
            result.TopProducts = (await c.QueryAsync<ShopProductRow>("""
                WITH sold AS (
                 SELECT i.variant_id,sum(i.quantity)::bigint qty,sum(i.line_total) amount FROM sale_items i JOIN sales s ON s.sale_id=i.sale_id
                 WHERE s.status='COMPLETED' AND s.sale_date>=@Start AND s.sale_date<@End GROUP BY i.variant_id
                ), returned AS (
                 SELECT i.variant_id,sum(i.quantity)::bigint qty,sum(i.amount) amount FROM sale_return_items i JOIN sale_returns r ON r.sale_return_id=i.sale_return_id
                 WHERE r.return_date>=@Start AND r.return_date<@End GROUP BY i.variant_id
                ) SELECT coalesce(p.product_name||' / '||v.variant_name,'Variant #'||coalesce(s.variant_id,r.variant_id)||' (ownership review pending)') ProductName,coalesce(s.qty,0)-coalesce(r.qty,0) Quantity,
                 coalesce(s.amount,0)-coalesce(r.amount,0) Amount FROM sold s FULL JOIN returned r ON r.variant_id=s.variant_id
                 LEFT JOIN product_variants v ON v.variant_id=coalesce(s.variant_id,r.variant_id) LEFT JOIN products p ON p.product_id=v.product_id
                 ORDER BY Amount DESC,ProductName LIMIT 5
                """,args,tx)).ToList();
        }
        result.Recent = (await c.QueryAsync<ShopActivityRow>("""
            SELECT * FROM (
             SELECT 'Sale'::text Type,s.invoice_number Reference,coalesce((SELECT sum(quantity)::bigint FROM sale_items WHERE sale_id=s.sale_id),0) Quantity,
              s.total_amount Amount,s.sale_date AS "Date",s.sale_id Id FROM sales s WHERE @Sales AND s.status='COMPLETED' AND s.sale_date>=@Start AND s.sale_date<@End
             UNION ALL SELECT 'Receive',r.receipt_number,coalesce((SELECT sum(quantity)::bigint FROM purchase_receipt_items WHERE receipt_id=r.receipt_id),0),
              NULL::numeric,r.received_date,r.receipt_id FROM purchase_receipts r WHERE @Stock AND r.received_date>=@Start AND r.received_date<@End
            ) activity ORDER BY "Date" DESC,Id DESC,Type LIMIT 5
            """,new {args.Start,args.End,Sales=sales,Stock=stock},tx)).ToList();
        await tx.CommitAsync();
        return result;
    }
}
