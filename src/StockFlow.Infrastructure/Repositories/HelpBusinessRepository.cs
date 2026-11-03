using System.Globalization;
using Dapper;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

// Approved read-only queries, using the same runtime role/RLS and ledger rules as the application.
public partial class HelpBusinessRepository(DbConnectionFactory factory, IOptions<ShopTimeOptions> time, TimeProvider clock) : IHelpBusinessRepository
{
    private static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("en-IN"));
    public async Task<BusinessValueResult> GetAsync(BusinessValueQuery query, CancellationToken cancellation)
    {
        if (OperationMetrics.Contains(query.Metric)) return await GetOperationAsync(query,cancellation);
        var dates = ShopDateRange.Resolve(query.Period, time.Value, clock);
        await using var c = factory.CreateConnection();
        await c.OpenAsync(cancellation);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellation);
        await c.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY", transaction:tx, cancellationToken:cancellation));
        var term = query.Term?.Trim();
        var args = new { dates.StartUtc, dates.EndUtc, Term=term };
        Task<decimal> Amount(string sql) => c.ExecuteScalarAsync<decimal>(new CommandDefinition(sql, args, tx, cancellationToken:cancellation));
        async Task<int[]> Matches(string sql) => (await c.QueryAsync<int>(new CommandDefinition(sql, args, tx, cancellationToken:cancellation))).ToArray();
        if (query.Metric is "customer-dues" or "supplier-balance")
        {
            int? party = null;
            if (term is not null)
            {
                var ids = await Matches(query.Metric == "customer-dues"
                    ? "SELECT customer_id FROM customers WHERE lower(customer_name)=lower(@Term) OR mobile_number=@Term LIMIT 2"
                    : "SELECT supplier_id FROM suppliers WHERE lower(supplier_name)=lower(@Term) OR supplier_code=@Term LIMIT 2");
                if (ids.Length != 1) return new("clarify", ids.Length == 0 ? "No matching account found. Enter its exact name or code." : "Several accounts have that name. Enter the customer mobile number or supplier code.");
                party = ids[0];
            }
            var sql = query.Metric == "customer-dues" ? """
                SELECT coalesce(sum(greatest(s.total_amount
                 -coalesce((SELECT sum(return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id),0)
                 -coalesce((SELECT sum(amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT'),0)
                 +coalesce((SELECT sum(refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id),0),0)),0)
                FROM sales s WHERE s.status='COMPLETED' AND s.customer_id IS NOT NULL AND (@Party::integer IS NULL OR s.customer_id=@Party)
                """ : """
                SELECT coalesce(sum(greatest(p.total_amount
                 -coalesce((SELECT sum(return_total) FROM purchase_returns r WHERE r.purchase_id=p.purchase_id),0)
                 -coalesce((SELECT sum(amount) FROM supplier_payments sp WHERE sp.purchase_id=p.purchase_id),0),0)),0)
                FROM purchases p WHERE p.status='COMPLETED' AND (@Party::integer IS NULL OR p.supplier_id=@Party)
                """;
            return new("answered", Money(await c.ExecuteScalarAsync<decimal>(new CommandDefinition(sql,new { Party=party },tx,cancellationToken:cancellation))));
        }
        if (query.Metric.StartsWith("product-", StringComparison.Ordinal))
        {
            if (term is null || term.Length < 2) throw new ArgumentException("A product or brand is required.");
            var brands = await Matches("SELECT brand_id FROM brands WHERE lower(brand_name)=lower(@Term) LIMIT 2");
            var products = await Matches("SELECT product_id FROM products WHERE lower(product_name)=lower(@Term) OR sku=@Term OR barcode=@Term LIMIT 2");
            var variants=(await c.QueryAsync<VariantMatch>(new CommandDefinition("""
                SELECT v.variant_id VariantId,v.product_id ProductId FROM product_variants v
                WHERE lower(v.sku)=lower(@Term) OR EXISTS(SELECT 1 FROM variant_barcodes b WHERE b.variant_id=v.variant_id AND lower(b.barcode)=lower(@Term)) LIMIT 2
                """,args,tx,cancellationToken:cancellation))).ToArray();
            if (brands.Length > 1 || products.Length > 1 || variants.Length > 1 || brands.Length > 0 && (products.Length > 0 || variants.Length > 0) ||
                variants.Length==1 && products.Length==1 && variants[0].ProductId!=products[0])
                return new("clarify", "Several product or brand records match. Enter a unique product name, item code or barcode.");
            if (brands.Length == 0 && products.Length == 0 && variants.Length==0) return new("clarify", "No matching product or brand found. Enter its exact name, item code or barcode.");
            var rows = await c.QuerySingleAsync<ProductSales>(new CommandDefinition("""
                SELECT coalesce(sum(i.quantity::bigint),0)::bigint Quantity, count(DISTINCT s.sale_id) InvoiceCount,
                 coalesce(sum(i.line_total),0) Amount FROM sale_items i JOIN sales s ON s.sale_id=i.sale_id
                 JOIN products p ON p.product_id=i.product_id
                WHERE s.status='COMPLETED' AND s.sale_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                 AND s.sale_date<(@EndUtc AT TIME ZONE current_setting('TimeZone'))
                 AND (@Product::integer IS NOT NULL AND p.product_id=@Product OR @Brand::integer IS NOT NULL AND p.brand_id=@Brand OR @Variant::bigint IS NOT NULL AND i.variant_id=@Variant)
                """, new { dates.StartUtc, dates.EndUtc, Product=variants.Length==0 && products.Length==1 ? products[0] : (int?)null,
                    Brand=brands.Length==1 ? brands[0] : (int?)null, Variant=variants.Length==1 ? variants[0].VariantId : (long?)null },tx,cancellationToken:cancellation));
            return new("answered", query.Metric switch { "product-quantity" => rows.Quantity.ToString("N0"), "product-count"=>rows.InvoiceCount.ToString("N0"), "product-amount"=>Money(rows.Amount), _=>throw new ArgumentException("Unsupported product metric.") });
        }
        if (query.Metric.StartsWith("return-", StringComparison.Ordinal) || query.Metric == "refunded")
        {
            var sql = query.Metric switch {
                "return-count" => "SELECT count(*) FROM sale_returns WHERE return_date>=@StartUtc AND return_date<@EndUtc",
                "return-quantity" => "SELECT coalesce(sum(i.quantity::bigint),0) FROM sale_return_items i JOIN sale_returns r ON r.sale_return_id=i.sale_return_id WHERE r.return_date>=@StartUtc AND r.return_date<@EndUtc",
                "return-amount" => "SELECT coalesce(sum(return_total),0) FROM sale_returns WHERE return_date>=@StartUtc AND return_date<@EndUtc",
                "refunded" => "SELECT coalesce(sum(refund_amount),0) FROM sale_returns WHERE return_date>=@StartUtc AND return_date<@EndUtc",
                _=>throw new ArgumentException("Unsupported return metric.")
            };
            var amount=await Amount(sql);
            return new("answered", query.Metric is "return-count" or "return-quantity" ? amount.ToString("N0") : Money(amount));
        }
        if (query.Metric == "payment-breakdown" || query.Metric.EndsWith("-collected", StringComparison.Ordinal))
        {
            var payments = (await c.QueryAsync<ModeTotal>(new CommandDefinition("""
                SELECT p.payment_mode Mode, sum(p.amount) Amount FROM payments p JOIN sales s ON s.sale_id=p.sale_id
                WHERE s.status='COMPLETED' AND p.payment_mode<>'CREDIT'
                 AND p.payment_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                 AND p.payment_date<(@EndUtc AT TIME ZONE current_setting('TimeZone'))
                GROUP BY p.payment_mode ORDER BY p.payment_mode
                """,args,tx,cancellationToken:cancellation))).ToArray();
            if (query.Metric == "payment-breakdown") return new("answered", payments.Length == 0 ? Money(0) : string.Join("; ",payments.Select(p=>$"{p.Mode}: {Money(p.Amount)}")));
            var mode=query.Metric[..^10].ToUpperInvariant();
            if (mode is not ("CASH" or "CARD" or "UPI" or "BANK" or "CHEQUE")) throw new ArgumentException("Unsupported payment mode.");
            return new("answered", Money(payments.Where(p=>p.Mode==mode).Sum(p=>p.Amount)));
        }
        throw new ArgumentException("Unsupported business metric.");
    }
    private record ProductSales(long Quantity,long InvoiceCount,decimal Amount);
    private record ModeTotal(string Mode,decimal Amount);
    private record VariantMatch(long VariantId,int ProductId);
}
