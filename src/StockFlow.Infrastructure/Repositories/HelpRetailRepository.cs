using Dapper;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

// Fixed, read-only operations. The request's validated scope and database RLS
// remain in effect; neither a question nor the model can broaden that scope.
public sealed class HelpRetailRepository(DbConnectionFactory factory, BusinessScope scope,
    IOptions<ShopTimeOptions> time, TimeProvider clock) : IHelpRetailRepository
{
    public async Task<IReadOnlyList<ShopAssistantTotal>> CompareShopsAsync(SalesPeriod period, CancellationToken cancellation)
    {
        if (scope.TenantId <= 0 || scope.UserId <= 0 || !scope.IsOwner || !scope.CanReviewStores ||
            !scope.Consolidated || scope.StoreId != 0 || scope.LocationId != 0)
            throw new UnauthorizedAccessException("Owner shop comparisons require the authorized all-shops context.");
        var dates = ShopDateRange.Resolve(period, time.Value, clock);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellation);
        await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY", transaction: transaction, cancellationToken: cancellation));
        var rows = await connection.QueryAsync<ShopAssistantTotal>(new CommandDefinition("""
            WITH sold AS (
                SELECT s.store_id, count(*) InvoiceCount, sum(s.total_amount) SalesAmount
                FROM sales s WHERE s.tenant_id=@TenantId AND s.status='COMPLETED'
                 AND s.sale_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                 AND s.sale_date<(@EndUtc AT TIME ZONE current_setting('TimeZone')) GROUP BY s.store_id
            ), collected AS (
                SELECT s.store_id, sum(p.amount) Collected FROM payments p JOIN sales s ON s.sale_id=p.sale_id
                WHERE s.tenant_id=@TenantId AND s.status='COMPLETED' AND p.payment_mode<>'CREDIT'
                 AND p.payment_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                 AND p.payment_date<(@EndUtc AT TIME ZONE current_setting('TimeZone')) GROUP BY s.store_id
            )
            SELECT st.store_id StoreId, st.store_name StoreName, coalesce(s.InvoiceCount,0) InvoiceCount,
                coalesce(s.SalesAmount,0) SalesAmount, coalesce(c.Collected,0) Collected
            FROM stores st LEFT JOIN sold s USING(store_id) LEFT JOIN collected c USING(store_id)
            WHERE st.tenant_id=@TenantId AND st.is_active
            ORDER BY coalesce(s.SalesAmount,0) DESC, st.store_id LIMIT 51
            """, new { scope.TenantId, dates.StartUtc, dates.EndUtc }, transaction, cancellationToken: cancellation));
        return rows.ToArray();
    }
}
