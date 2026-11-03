using System.Data;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Dashboard;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public DashboardRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private const string Movements = """
        WITH movement AS (
            SELECT it.*, CASE
                WHEN transaction_type IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN quantity::bigint
                WHEN transaction_type IN ('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -quantity::bigint
                ELSE 0::bigint END AS delta
            FROM inventory_transactions it
            WHERE EXISTS(SELECT 1 FROM products cp JOIN categories cc ON cc.category_id=cp.category_id WHERE cp.product_id=it.product_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))
        )
        """;
    private const string InventorySql = """
        SELECT (SELECT COUNT(*) FROM products p JOIN categories c ON c.category_id=p.category_id WHERE p.is_active AND COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')) AS TotalProducts,
            CASE WHEN @isAdmin THEN (SELECT COUNT(*) FROM suppliers WHERE is_active) END AS TotalSuppliers,
            (SELECT COUNT(*) FROM customers WHERE is_active) AS TotalCustomers,
            COALESCE(SUM(GREATEST(delta,0)),0)::bigint AS StockInflow,
            COALESCE(SUM(GREATEST(-delta,0)),0)::bigint AS StockOutflow,
            (SELECT COALESCE(SUM(s.current_stock),0)::bigint FROM vw_current_stock s JOIN products p ON p.product_id=s.product_id JOIN categories c ON c.category_id=p.category_id WHERE COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')) AS CurrentStock,
            COALESCE(SUM(GREATEST(delta,0)) FILTER (WHERE created_at>=CURRENT_DATE AND created_at<CURRENT_DATE+1),0)::bigint AS TodayInflow,
            COALESCE(SUM(GREATEST(-delta,0)) FILTER (WHERE created_at>=CURRENT_DATE AND created_at<CURRENT_DATE+1),0)::bigint AS TodayOutflow
        FROM movement
        """;
    private const string BusinessSql = """
        WITH customer_dues AS (
            SELECT s.payment_due_date,
                greatest(s.total_amount
                    -coalesce((SELECT sum(return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id),0)
                    -coalesce((SELECT sum(amount) FROM payments p WHERE p.sale_id=s.sale_id AND p.payment_mode<>'CREDIT'),0)
                    +coalesce((SELECT sum(refund_amount) FROM sale_returns r WHERE r.sale_id=s.sale_id),0),0) AS balance
            FROM sales s WHERE @canSeeSales AND s.status='COMPLETED' AND s.customer_id IS NOT NULL
        )
        SELECT CURRENT_DATE AS Today,
            CASE WHEN @canSeeSales THEN (SELECT coalesce(sum(balance),0) FROM customer_dues) END AS CustomerOutstandingAmount,
            CASE WHEN @canSeeSales THEN (SELECT coalesce(sum(balance),0) FROM customer_dues WHERE payment_due_date<CURRENT_DATE) END AS OverduePaymentAmount,
            CASE WHEN @canSeeSales THEN (SELECT count(*) FROM customer_dues WHERE balance>0 AND payment_due_date<CURRENT_DATE) END AS OverdueInvoiceCount,
            CASE WHEN @canSeeSales THEN (SELECT COALESCE(SUM(total_amount),0) FROM sales
                WHERE status='COMPLETED' AND sale_date>=CURRENT_DATE AND sale_date<CURRENT_DATE+1) END AS TodaySalesAmount,
            CASE WHEN @canSeeSales THEN (SELECT COALESCE(SUM(total_amount),0) FROM sales
                WHERE status='COMPLETED' AND sale_date>=date_trunc('month',CURRENT_DATE::timestamp)
                AND sale_date<date_trunc('month',CURRENT_DATE::timestamp)+INTERVAL '1 month') END AS MonthSalesAmount,
            CASE WHEN @canSeeSales THEN (SELECT COUNT(*) FROM sales
                WHERE status='COMPLETED' AND sale_date>=CURRENT_DATE AND sale_date<CURRENT_DATE+1) END AS TodayInvoiceCount,
            (SELECT COUNT(*) FROM products p JOIN product_variants v ON v.product_id=p.product_id JOIN vw_variant_current_stock s ON s.variant_id=v.variant_id
                WHERE p.is_active AND v.is_active AND s.current_stock<=sf_variant_minimum(v.variant_id,v.minimum_stock_level)
                AND EXISTS(SELECT 1 FROM categories cc WHERE cc.category_id=p.category_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))) AS LowStockCount,
            (SELECT COUNT(*) FROM service_requests WHERE status NOT IN ('DELIVERED','CANCELLED')) AS PendingServiceCount,
            (SELECT COUNT(*) FROM service_requests WHERE status='READY') AS ReadyServiceCount,
            (SELECT COUNT(*) FROM warranties w JOIN sale_items si ON si.sale_item_id=w.sale_item_id
                JOIN sales s ON s.sale_id=si.sale_id WHERE s.status='COMPLETED'
                AND w.warranty_status IN ('ACTIVE','CLAIMED')
                AND w.warranty_end_date BETWEEN CURRENT_DATE AND CURRENT_DATE+30) AS ExpiringWarrantyCount
        """;
    private const string CategoriesSql = """
        SELECT c.category_name AS CategoryName,COALESCE(SUM(s.current_stock),0)::bigint AS Quantity
        FROM products p JOIN categories c ON c.category_id=p.category_id
        JOIN product_variants v ON v.product_id=p.product_id JOIN vw_variant_current_stock s ON s.variant_id=v.variant_id WHERE p.is_active
        AND COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')
        GROUP BY c.category_id,c.category_name ORDER BY Quantity DESC,c.category_name
        """;
    private const string RecentSalesSql = """
        SELECT s.sale_id AS SaleId,s.invoice_number AS InvoiceNumber,
            CASE WHEN s.invoice_snapshot_version>0 THEN s.customer_name_snapshot ELSE c.customer_name END AS CustomerName,
            s.sale_date AS SaleDate,s.total_amount AS TotalAmount,
            COALESCE((SELECT string_agg(DISTINCT payment_mode,' + ' ORDER BY payment_mode) FROM payments WHERE sale_id=s.sale_id),'Not recorded') AS Payment
        FROM sales s LEFT JOIN customers c ON c.customer_id=s.customer_id
        WHERE s.status='COMPLETED' ORDER BY s.sale_date DESC,s.sale_id DESC LIMIT 5
        """;
    private const string RecentServicesSql = """
        SELECT sr.service_request_id AS ServiceRequestId,sr.service_number AS ServiceNumber,
            c.customer_name AS CustomerName,p.product_name AS ProductName,sr.status AS Status,
            sr.expected_delivery_date AS ExpectedDeliveryDate
        FROM service_requests sr JOIN customers c ON c.customer_id=sr.customer_id
        JOIN products p ON p.product_id=sr.product_id
        ORDER BY sr.received_date DESC,sr.service_request_id DESC LIMIT 5
        """;
    private const string LowStockSql = """
        SELECT COUNT(*) OVER() AS TotalCount,p.product_id AS ProductId,p.product_name || ' / ' || v.variant_name AS ProductName,c.category_name AS CategoryName,
            s.current_stock AS CurrentStock,sf_variant_minimum(v.variant_id,v.minimum_stock_level) AS MinimumStockLevel,
            GREATEST(sf_variant_minimum(v.variant_id,v.minimum_stock_level)::bigint-s.current_stock,0) AS Required
        FROM products p JOIN categories c ON c.category_id=p.category_id
        JOIN product_variants v ON v.product_id=p.product_id JOIN vw_variant_current_stock s ON s.variant_id=v.variant_id
        WHERE p.is_active AND v.is_active AND s.current_stock<=sf_variant_minimum(v.variant_id,v.minimum_stock_level)
        AND COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL')
        ORDER BY (sf_variant_minimum(v.variant_id,v.minimum_stock_level)::bigint-s.current_stock) DESC,p.product_name,p.product_id LIMIT @limit OFFSET @offset
        """;
    private const string ActivitySql = """
        SELECT COUNT(*) OVER() AS TotalCount,m.inventory_transaction_id AS InventoryTransactionId,m.created_at AS CreatedAt,
            p.product_name || ' / ' || v.variant_name AS ProductName,ABS(m.delta) AS Quantity,m.transaction_type AS TransactionType,
            CASE WHEN @Incoming AND @isAdmin THEN supplier.supplier_name
                 WHEN NOT @Incoming THEN COALESCE(customer.customer_name,sc.customer_name,wc_customer.customer_name) END AS PartyName,
            CASE WHEN @isAdmin AND receipt.receipt_id IS NOT NULL THEN receipt.receipt_number
                 WHEN @isAdmin AND pur.purchase_id IS NOT NULL THEN pur.purchase_number
                 ELSE COALESCE(s.invoice_number,sr.service_number,wc.claim_number) END AS ReferenceNumber,
            CASE WHEN @isAdmin AND receipt.receipt_id IS NOT NULL THEN '/StockInflow/Details/PURCHASE/'||receipt.receipt_id
                 WHEN @isAdmin AND pur.purchase_id IS NOT NULL THEN '/Purchases/Details/'||pur.purchase_id
                 WHEN s.sale_id IS NOT NULL THEN '/Sales/Invoice/'||s.sale_id
                 WHEN sr.service_request_id IS NOT NULL THEN '/Service/Details/'||sr.service_request_id
                 WHEN wc.warranty_id IS NOT NULL THEN '/Warranty/Details/'||wc.warranty_id END AS ReferenceUrl
        FROM movement m JOIN products p ON p.product_id=m.product_id JOIN product_variants v ON v.variant_id=m.variant_id
        LEFT JOIN purchase_receipts receipt ON receipt.receipt_id=m.receipt_id
        LEFT JOIN purchases pur ON m.reference_type='PURCHASE' AND pur.purchase_id=m.reference_id
        LEFT JOIN suppliers supplier ON supplier.supplier_id=pur.supplier_id
        LEFT JOIN sales s ON m.reference_type='SALE' AND s.sale_id=m.reference_id
        LEFT JOIN customers customer ON customer.customer_id=s.customer_id
        LEFT JOIN service_parts sp ON m.reference_type='SERVICE_PART' AND sp.service_part_id=m.reference_id
        LEFT JOIN service_requests sr ON sr.service_request_id=sp.service_request_id
        LEFT JOIN customers sc ON sc.customer_id=sr.customer_id
        LEFT JOIN warranty_claims wc ON m.reference_type='WARRANTY_CLAIM' AND wc.warranty_claim_id=m.reference_id
        LEFT JOIN warranties w ON w.warranty_id=wc.warranty_id
        LEFT JOIN sale_items wi ON wi.sale_item_id=w.sale_item_id
        LEFT JOIN sales ws ON ws.sale_id=wi.sale_id
        LEFT JOIN customers wc_customer ON wc_customer.customer_id=ws.customer_id
        WHERE ((@Incoming AND m.delta>0) OR (NOT @Incoming AND m.delta<0))
          AND (@Type::text IS NULL OR m.transaction_type=@Type)
          AND (@Term='' OR p.product_name ILIKE @Pattern OR v.variant_name ILIKE @Pattern OR v.attributes::text ILIKE @Pattern OR s.invoice_number ILIKE @Pattern
              OR sr.service_number ILIKE @Pattern OR wc.claim_number ILIKE @Pattern)
          AND (@From::date IS NULL OR m.created_at>=@From::date)
          AND (@To::date IS NULL OR m.created_at<@To::date+INTERVAL '1 day')
        ORDER BY m.created_at DESC,m.inventory_transaction_id DESC LIMIT @limit OFFSET @Offset
        """;
    private string ProfileActivitySql => factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? ActivitySql.Replace("v.attributes::text ILIKE @Pattern","sf_searchable_attributes(p.category_id,v.attributes) ILIKE @Pattern",StringComparison.Ordinal) : ActivitySql;
    // Group serialized entries within the same receipt/document and variant, before LIMIT.
    // Location and movement type remain distinct; undocumented adjustments remain individual.
    private const string GroupedMovements = Movements + """
        , grouped_movement AS (
            SELECT max(inventory_transaction_id) AS inventory_transaction_id,max(created_at) AS created_at,
                product_id,variant_id,transaction_type,reference_type,reference_id,receipt_id,
                sum(delta)::bigint AS delta
            FROM movement
            GROUP BY tenant_id,location_id,product_id,variant_id,transaction_type,reference_type,reference_id,receipt_id,
                CASE WHEN receipt_id IS NULL AND reference_id IS NULL THEN inventory_transaction_id END
        )
        """;
    // All panels rendered on initial load use the same database snapshot.
    public async Task<DashboardResponse> GetAsync(bool isAdmin, bool canSeeSales = true)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        // One command, preserving the same RLS scope, queries and repeatable-read snapshot.
        var sql = string.Join(";\n", new[] {
            Movements + InventorySql, BusinessSql, Movements + TrendSql, CategoriesSql,
            GroupedMovements + ProfileActivitySql.Replace("FROM movement m", "FROM grouped_movement m", StringComparison.Ordinal).Replace("@Incoming", "TRUE", StringComparison.Ordinal),
            GroupedMovements + ProfileActivitySql.Replace("FROM movement m", "FROM grouped_movement m", StringComparison.Ordinal).Replace("@Incoming", "FALSE", StringComparison.Ordinal),
            canSeeSales ? RecentSalesSql : null, RecentServicesSql, LowStockSql
        }.Where(statement => statement is not null));
        DashboardResponse data;
        using (var results = await c.QueryMultipleAsync(sql, new {
            isAdmin, canSeeSales, daily=false, count=6, offset=0, limit=5,
            Type=(string?)null, Term="", Pattern="%%", From=(DateTime?)null, To=(DateTime?)null
        }, t))
        {
            data = await results.ReadSingleAsync<DashboardResponse>();
            data.Business = await results.ReadSingleAsync<BusinessDashboardSummaryResponse>();
            data.Months = (await results.ReadAsync<MonthlyStock>()).ToList();
            data.Categories = (await results.ReadAsync<CategoryStock>()).ToList();
            data.RecentInflow = (await results.ReadAsync<RecentStock>()).ToList();
            data.RecentOutflow = (await results.ReadAsync<RecentStock>()).ToList();
            if (canSeeSales) data.RecentSales = (await results.ReadAsync<RecentSaleResponse>()).ToList();
            data.RecentServices = (await results.ReadAsync<RecentServiceResponse>()).ToList();
            data.LowStock = (await results.ReadAsync<LowStockResponse>()).ToList();
        }
        await t.CommitAsync();
        return data;
    }

    public async Task<DashboardInventorySummaryResponse> GetInventorySummaryAsync(bool isAdmin)
    {
        await using var c = factory.CreateConnection();
        return await c.QuerySingleAsync<DashboardInventorySummaryResponse>(Movements + InventorySql, new
        {
            isAdmin
        });
    }

    public async Task<BusinessDashboardSummaryResponse> GetBusinessSummaryAsync(bool canSeeSales)
    {
        await using var c = factory.CreateConnection();
        return await c.QuerySingleAsync<BusinessDashboardSummaryResponse>(BusinessSql, new
        {
            canSeeSales
        });
    }

    private static Task<IEnumerable<MonthlyStock>> Trend(NpgsqlConnection c, string period, NpgsqlTransaction? t = null)
    {
        var daily = period is "7days" or "30days";
        var count = period switch
        {
            "7days" => 7,
            "30days" => 30,
            "6months" => 6,
            "12months" => 12,
            _ => throw new System.ComponentModel.DataAnnotations.ValidationException("Invalid chart period.")
        };
        return c.QueryAsync<MonthlyStock>(Movements + TrendSql,new { daily, count },t);
    }

    private const string TrendSql = """
            , buckets AS (
                SELECT generate_series(
                    CASE WHEN @daily THEN CURRENT_DATE-(@count-1)*INTERVAL '1 day'
                         ELSE date_trunc('month',CURRENT_DATE::timestamp)-(@count-1)*INTERVAL '1 month' END,
                    CASE WHEN @daily THEN CURRENT_DATE::timestamp ELSE date_trunc('month',CURRENT_DATE::timestamp) END,
                    CASE WHEN @daily THEN INTERVAL '1 day' ELSE INTERVAL '1 month' END) AS start_date
            )
            SELECT b.start_date AS Month,to_char(b.start_date,CASE WHEN @daily THEN 'DD Mon' ELSE 'Mon YYYY' END) AS Period,
                COALESCE(SUM(GREATEST(m.delta,0)),0)::bigint AS Inflow,
                COALESCE(SUM(GREATEST(-m.delta,0)),0)::bigint AS Outflow
            FROM buckets b LEFT JOIN movement m ON m.created_at>=b.start_date
                AND m.created_at<b.start_date+CASE WHEN @daily THEN INTERVAL '1 day' ELSE INTERVAL '1 month' END
                AND m.created_at<CURRENT_DATE+1
            GROUP BY b.start_date ORDER BY b.start_date
            """;

    public async Task<IEnumerable<MonthlyStock>> GetTrendAsync(string period)
    {
        await using var c = factory.CreateConnection();
        return await Trend(c, period);
    }

    public async Task<IEnumerable<CategoryStock>> GetCategoryStockAsync()
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<CategoryStock>(CategoriesSql);
    }

    private Task<IEnumerable<RecentStock>> Activity(NpgsqlConnection c,
DashboardMovementSearch search,
bool isAdmin,
int limit,
NpgsqlTransaction? t = null)
    {
        return c.QueryAsync<RecentStock>(Movements + ProfileActivitySql,
new
{
    search.Incoming,
    isAdmin,
    search.Type,
    Term = search.Term ?? "",
    Pattern = "%" + search.Term + "%",
    search.From,
    search.To,
    search.Offset,
    limit
},
t);
    }

    public async Task<IEnumerable<RecentStock>> GetMovementsAsync(DashboardMovementSearch search, bool isAdmin, int limit = 100)
    {
        await using var c = factory.CreateConnection();
        return await Activity(c, search, isAdmin, limit);
    }

    public async Task<IEnumerable<RecentSaleResponse>> GetRecentSalesAsync()
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<RecentSaleResponse>(RecentSalesSql);
    }

    public async Task<IEnumerable<RecentServiceResponse>> GetRecentServicesAsync()
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<RecentServiceResponse>(RecentServicesSql);
    }

    public async Task<IEnumerable<LowStockResponse>> GetLowStockAsync(int offset, int limit = 100)
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<LowStockResponse>(LowStockSql, new
        {
            offset,
            limit
        });
    }

    public async Task<IEnumerable<GlobalSearchResult>> SearchAsync(string term)
    {
        await using var c = factory.CreateConnection();
        // Limit each group independently so a common product name cannot hide serials or invoices.
        var rows = await c.QueryAsync<SearchRow>("""
            (SELECT 'PRODUCT' AS Kind,p.product_id::bigint AS Id,p.product_name AS Title,
                concat_ws(' / ',p.sku,p.barcode) AS Detail FROM products p
                WHERE p.product_name ILIKE @pattern OR p.sku ILIKE @pattern OR p.barcode ILIKE @pattern OR EXISTS(SELECT 1 FROM product_variants v WHERE v.product_id=p.product_id AND (v.sku ILIKE @pattern OR
            """ + (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? "concat_ws(' ',sf_searchable_attributes(p.category_id,v.attributes),(SELECT string_agg(i.supplier_sku,' ') FROM variant_supplier_identifiers i WHERE i.variant_id=v.variant_id))" : "v.attributes::text") + """
             ILIKE @pattern)) ORDER BY p.product_name,p.product_id LIMIT 10)
            UNION ALL
            (SELECT 'SERIAL',pu.product_unit_id,COALESCE(pu.serial_number,pu.barcode),p.product_name||' / '||pu.unit_status
                FROM product_units pu JOIN products p ON p.product_id=pu.product_id
                WHERE pu.serial_number ILIKE @pattern OR pu.barcode ILIKE @pattern ORDER BY pu.product_unit_id DESC LIMIT 10)
            UNION ALL
            (SELECT 'CUSTOMER',c.customer_id::bigint,c.customer_name,c.mobile_number FROM customers c
                WHERE c.customer_name ILIKE @pattern OR c.mobile_number ILIKE @pattern ORDER BY c.customer_name,c.customer_id LIMIT 10)
            UNION ALL
            (SELECT 'INVOICE',s.sale_id,s.invoice_number,COALESCE(s.customer_name_snapshot,c.customer_name,'Walk-in')||' / '||s.status
                FROM sales s LEFT JOIN customers c ON c.customer_id=s.customer_id
                WHERE s.invoice_number ILIKE @pattern ORDER BY s.sale_date DESC,s.sale_id DESC LIMIT 10)
            UNION ALL
            (SELECT 'SERVICE',sr.service_request_id,sr.service_number,c.customer_name||' / '||sr.status
                FROM service_requests sr JOIN customers c ON c.customer_id=sr.customer_id
                WHERE sr.service_number ILIKE @pattern ORDER BY sr.received_date DESC,sr.service_request_id DESC LIMIT 10)
            """,
new
{
    pattern = "%" + term + "%"
});
        return rows.Select(r => new GlobalSearchResult
        {
            Kind = r.Kind,
            Title = r.Title,
            Detail = r.Detail,
            Url = r.Kind switch
            {
                "PRODUCT" => "/Products?search=" + Uri.EscapeDataString(r.Title),
                "SERIAL" => "/Scan?code=" + Uri.EscapeDataString(r.Title),
                "CUSTOMER" => "/Customers?search=" + Uri.EscapeDataString(r.Detail ?? r.Title),
                "INVOICE" => "/Sales/Invoice/" + r.Id,
                "SERVICE" => "/Service/Details/" + r.Id,
                _ => "/"
            }
        });
    }

    public async Task<IEnumerable<DashboardUser>> GetUsersAsync()
    {
        await using var c = factory.CreateConnection();
        return await c.QueryAsync<DashboardUser>("""
            SELECT u.user_id AS UserId,u.username AS Username,u.full_name AS FullName,
                r.role_name AS RoleName,(u.is_active AND r.is_active) AS IsActive
            FROM users u JOIN roles r ON r.role_id=u.role_id ORDER BY u.full_name,u.user_id
            """);
    }

    private sealed class SearchRow
    {
        public string Kind { get; set; } = "";
        public long Id
        {
            get; set;
        }
        public string Title { get; set; } = "";
        public string? Detail
        {
            get; set;
        }
    }
}
