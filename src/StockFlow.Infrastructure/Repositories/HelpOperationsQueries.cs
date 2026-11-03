using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;

namespace StockFlow.Infrastructure.Repositories;

public partial class HelpBusinessRepository
{
    internal const string OverdueCustomerSql="""
                    WITH balances AS (
                     SELECT s.customer_id,s.payment_due_date,greatest(s.total_amount
                      -coalesce((SELECT sum(return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id),0)
                      -coalesce((SELECT sum(amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT'),0)
                      +coalesce((SELECT sum(refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id),0),0) amount
                     FROM sales s WHERE s.status='COMPLETED' AND s.customer_id IS NOT NULL)
                    SELECT c.customer_id Id,c.customer_name Name,sum(b.amount) Amount,min(b.payment_due_date)::timestamp Due
                    FROM balances b JOIN customers c USING(customer_id) WHERE b.amount>0 AND b.payment_due_date<@Today::date
                    GROUP BY c.customer_id,c.customer_name ORDER BY min(b.payment_due_date),c.customer_id LIMIT 11
                    """;
    internal const string UnknownDueDatesSql="""
                    SELECT count(*) FROM sales s WHERE s.status='COMPLETED' AND s.customer_id IS NOT NULL AND s.payment_due_date IS NULL
                     AND s.total_amount-coalesce((SELECT sum(return_total-refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id),0)
                      -coalesce((SELECT sum(amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT'),0)>0
                    """;
    private static readonly string[] OperationMetrics = ["pending-purchases", "reorder", "overdue-customers", "overdue-repairs", "ready-repair-list", "repair-deliveries", "pending-transfers", "warranty-detail", "top-products", "slow-products", "sales-comparison"];

    private async Task<BusinessValueResult> GetOperationAsync(BusinessValueQuery query, CancellationToken cancellation)
    {
        var dates=ShopDateRange.Resolve(query.Period,time.Value,clock);
        var today=time.Value.Today(clock);
        await using var c=factory.CreateConnection();
        await c.OpenAsync(cancellation);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,cancellation);
        await c.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY",transaction:tx,cancellationToken:cancellation));
        var args=new { Today=today, Term=query.Term?.Trim(), dates.StartUtc, dates.EndUtc };
        async Task<BusinessValueResult> List(string sql,string heading,string empty)
        {
            var rows=(await c.QueryAsync<string>(new CommandDefinition(sql,args,tx,cancellationToken:cancellation))).ToArray();
            return new("answered",rows.Length==0 ? empty : heading+"\n"+string.Join("\n",rows.Take(10).Select((row,index)=>$"{index+1}. {row}"))+(rows.Length>10 ? "\nShowing the first 10; more matching records exist." : ""));
        }
        switch(query.Metric)
        {
            case "pending-purchases":
                return await List("""
                    SELECT p.purchase_number||' — '||coalesce(s.supplier_name,'Supplier')||' — '||p.status||' — '||
                     sum(i.quantity-i.received_quantity-i.damaged_quantity)::text||' units awaiting receipt'
                    FROM purchases p JOIN purchase_items i USING(purchase_id) LEFT JOIN suppliers s USING(supplier_id)
                    WHERE p.status IN ('DRAFT','PARTIALLY_RECEIVED')
                    GROUP BY p.purchase_id,p.purchase_number,s.supplier_name,p.status,p.purchase_date
                    HAVING sum(i.quantity-i.received_quantity-i.damaged_quantity)>0
                    ORDER BY p.purchase_date,p.purchase_id LIMIT 11
                    ""","Pending purchase deliveries (including draft orders):","No purchase orders are waiting for stock.");
            case "reorder":
                var inconsistent=await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                    SELECT EXISTS(SELECT 1 FROM product_variants v JOIN products p USING(product_id)
                     JOIN vw_variant_current_stock s USING(variant_id) WHERE p.is_active AND v.is_active AND v.is_serial_tracked
                     AND s.current_stock<>(SELECT count(*) FROM product_units u WHERE u.variant_id=v.variant_id AND u.unit_status='IN_STOCK'))
                    """,transaction:tx,cancellationToken:cancellation));
                if(inconsistent) return new("clarify","Serial-unit availability and stock records disagree. Reconcile inventory before relying on reorder quantities.");
                return await List("""
                    SELECT p.product_name||' / '||v.variant_name||' ('||v.sku||') — stock '||s.current_stock::text||
                     ', minimum '||sf_variant_minimum(v.variant_id,v.minimum_stock_level)::text||
                     ', suggested purchase '||greatest(sf_variant_minimum(v.variant_id,v.minimum_stock_level)+1-s.current_stock,0)::text||' units'
                    FROM products p JOIN product_variants v USING(product_id) JOIN vw_variant_current_stock s USING(variant_id)
                    WHERE p.is_active AND v.is_active AND s.current_stock<=sf_variant_minimum(v.variant_id,v.minimum_stock_level)
                    ORDER BY s.current_stock-sf_variant_minimum(v.variant_id,v.minimum_stock_level),p.product_name,v.variant_id LIMIT 11
                    ""","Reorder suggestions to bring stock above minimum; review pending orders before purchasing:","No active products are at or below minimum stock.");
            case "top-products":
                return await List("""
                    SELECT p.product_name||' ('||p.sku||') — '||sum(i.quantity::bigint)::text||' units sold'
                    FROM sale_items i JOIN sales s USING(sale_id) JOIN products p USING(product_id)
                    WHERE s.status='COMPLETED' AND s.sale_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                     AND s.sale_date<(@EndUtc AT TIME ZONE current_setting('TimeZone'))
                    GROUP BY p.product_id,p.product_name,p.sku ORDER BY sum(i.quantity::bigint) DESC,p.product_id LIMIT 11
                    """,$"Top-selling products, {dates.From:dd MMM yyyy}–{dates.To:dd MMM yyyy} (completed sale units, before returns):","No completed product sales in that period.");
            case "slow-products":
                return await List("""
                    WITH sold AS (
                     SELECT i.variant_id,sum(i.quantity::bigint) quantity FROM sale_items i JOIN sales s USING(sale_id)
                     WHERE s.status='COMPLETED' AND s.sale_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                     AND s.sale_date<(@EndUtc AT TIME ZONE current_setting('TimeZone')) GROUP BY i.variant_id)
                    SELECT p.product_name||' / '||v.variant_name||' ('||v.sku||') — '||coalesce(sold.quantity,0)::text||
                     ' units sold; stock '||stock.current_stock::text
                    FROM products p JOIN product_variants v USING(product_id) JOIN vw_variant_current_stock stock USING(variant_id)
                    LEFT JOIN sold USING(variant_id)
                    WHERE p.is_active AND v.is_active AND stock.current_stock>0
                     AND v.created_at<(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                    ORDER BY coalesce(sold.quantity,0),v.variant_id LIMIT 11
                    """,$"Slow-moving items, {dates.From:dd MMM yyyy}–{dates.To:dd MMM yyyy}: fewest completed sale units among active items currently in stock and created before the period. Returns are excluded.","No eligible in-stock items for that period.");
            case "overdue-repairs":
            case "ready-repair-list":
            case "repair-deliveries":
                var filter=query.Metric switch { "overdue-repairs"=>"sr.expected_delivery_date<@Today::date", "ready-repair-list"=>"sr.status='READY'", _=>"(sr.expected_delivery_date<@Today::date OR sr.status='READY')" };
                return await List("""
                    SELECT sr.service_number||' — '||c.customer_name||' — '||sr.status||
                     CASE WHEN sr.expected_delivery_date IS NULL THEN '; delivery date not set' ELSE '; due '||to_char(sr.expected_delivery_date,'DD Mon YYYY') END
                    FROM service_requests sr JOIN customers c USING(customer_id)
                    WHERE sr.status NOT IN ('DELIVERED','CANCELLED') AND
                    """+" "+filter+" ORDER BY sr.expected_delivery_date NULLS LAST,sr.service_request_id LIMIT 11","Repair deliveries:","No matching pending repair deliveries.");
            case "pending-transfers":
                return await List("""
                    SELECT h.transfer_number||' — '||h.status||' — '||
                     coalesce((SELECT location_name FROM store_locations WHERE location_id=h.from_location_id),'Source location')||' → '||
                     coalesce((SELECT location_name FROM store_locations WHERE location_id=h.to_location_id),'Destination location')||' — '||
                     sum(i.dispatched_quantity-i.received_quantity-i.damaged_quantity-i.lost_quantity)::text||' units awaiting receipt'
                    FROM stock_transfers h JOIN stock_transfer_items i USING(tenant_id,transfer_id)
                    WHERE h.status IN ('DISPATCHED','PARTIALLY_RECEIVED')
                     AND (sf_location()=0 OR sf_location() IN(h.from_location_id,h.to_location_id))
                     AND (sf_store()=0 OR EXISTS(SELECT 1 FROM store_locations l WHERE l.store_id=sf_store() AND l.location_id IN(h.from_location_id,h.to_location_id)))
                    GROUP BY h.transfer_id,h.transfer_number,h.status,h.from_location_id,h.to_location_id,h.dispatched_at
                    HAVING sum(i.dispatched_quantity-i.received_quantity-i.damaged_quantity-i.lost_quantity)>0
                    ORDER BY h.dispatched_at,h.transfer_id LIMIT 11
                    ""","Transfers pending receipt:","No dispatched transfers are waiting for receipt.");
            case "warranty-detail":
                if(string.IsNullOrWhiteSpace(query.Term)) return new("clarify","What is the exact serial number?");
                var units=(await c.QueryAsync<long>(new CommandDefinition("SELECT product_unit_id FROM product_units WHERE lower(btrim(serial_number))=lower(@Term) LIMIT 2",args,tx,cancellationToken:cancellation))).ToArray();
                if(units.Length>1) return new("clarify","Several units have that serial number. Use a unique serial number.");
                var warranties=(await c.QueryAsync<WarrantyDetail>(new CommandDefinition("""
                    SELECT w.warranty_end_date::timestamp Ends,w.warranty_start_date::timestamp Starts,w.warranty_status Status,s.status SaleStatus,u.product_unit_id UnitId
                    FROM warranties w JOIN sale_items i USING(sale_item_id) JOIN sales s USING(sale_id)
                    JOIN product_units u ON u.product_unit_id=w.product_unit_id
                    WHERE lower(btrim(u.serial_number))=lower(@Term) ORDER BY w.warranty_id DESC LIMIT 2
                    """,args,tx,cancellationToken:cancellation))).ToArray();
                if(warranties.Length==0) return new("answered","No warranty record found for that serial number within your access.");
                if(warranties.Select(w=>w.UnitId).Distinct().Count()>1) return new("clarify","Several units have that serial number. Use a unique serial number.");
                var w=warranties[0];
                var status=w.SaleStatus=="CANCELLED" ? "CANCELLED" : w.Status is "ACTIVE" or "CLAIMED" && w.Ends<today ? "EXPIRED" : w.Status;
                return new("answered",$"Warranty expires {w.Ends:dd MMM yyyy}. Status: {status}. Starts {w.Starts:dd MMM yyyy}.");
            case "overdue-customers":
                var installed=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='sales' AND column_name='payment_due_date')",transaction:tx,cancellationToken:cancellation));
                if(!installed) return new("unavailable","Payment due dates are not installed yet. Ask your administrator to run database migration 037; overdue balances cannot be calculated without recorded due dates.");
                var dues=(await c.QueryAsync<OverdueCustomer>(new CommandDefinition(OverdueCustomerSql,args,tx,cancellationToken:cancellation))).ToArray();
                var unknown=await c.ExecuteScalarAsync<long>(new CommandDefinition(UnknownDueDatesSql,transaction:tx,cancellationToken:cancellation));
                return new("answered",(dues.Length==0 ? "No recorded overdue customer balances." : "Overdue customer balances:\n"+string.Join("\n",dues.Take(10).Select((d,index)=>$"{index+1}. {d.Name} (#{d.Id}) — {Money(d.Amount)}; oldest due {d.Due:dd MMM yyyy}"))+(dues.Length>10 ? "\nShowing the first 10; more matching records exist." : ""))+(unknown>0 ? $"\n{unknown} unpaid invoices have no due date and cannot be classified as overdue." : ""));
            case "sales-comparison":
                if(query.Period.Period is not ("this-week" or "this-month") || query.Term is not null) throw new ArgumentException("Choose this week versus last week, or this month versus last month.");
                var previous=ShopDateRange.Resolve(new(query.Period.Period=="this-week" ? "last-week" : "last-month"),time.Value,clock);
                const string totalSql="""
                    SELECT coalesce(sum(total_amount),0) FROM sales WHERE status='COMPLETED'
                     AND sale_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                     AND sale_date<(@EndUtc AT TIME ZONE current_setting('TimeZone'))
                    """;
                var current=await c.ExecuteScalarAsync<decimal>(new CommandDefinition(totalSql,args,tx,cancellationToken:cancellation));
                var prior=await c.ExecuteScalarAsync<decimal>(new CommandDefinition(totalSql,new { previous.StartUtc,previous.EndUtc },tx,cancellationToken:cancellation));
                var change=prior==0 ? "Percentage change unavailable: the previous period was zero." : $"Change: {(current-prior)/prior:+0.0%;-0.0%;0.0%}.";
                return new("answered",$"{dates.From:dd MMM}–{dates.To:dd MMM yyyy} (to date): {Money(current)}.\n{previous.From:dd MMM}–{previous.To:dd MMM yyyy} (full previous period): {Money(prior)}.\nDifference: {Money(current-prior)}. {change}\nCompleted invoice totals before returns.");
            default: throw new ArgumentException("Unsupported operational enquiry.");
        }
    }
    private sealed record WarrantyDetail(DateTime Ends,DateTime Starts,string Status,string SaleStatus,long UnitId);
    private sealed record OverdueCustomer(int Id,string Name,decimal Amount,DateTime Due);
}
