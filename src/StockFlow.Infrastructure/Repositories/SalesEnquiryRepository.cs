using Dapper;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class SalesEnquiryRepository(DbConnectionFactory factory, IOptions<ShopTimeOptions>? time = null, TimeProvider? clock = null) : ISalesEnquiryRepository
{
    public async Task<SalesPeriodSummary> GetSummaryAsync(SalesPeriod period, CancellationToken cancellation)
    {
        if (period.Period == "current") throw new ArgumentException("A sales period is required.");
        var dates=ShopDateRange.Resolve(period,time?.Value ?? new ShopTimeOptions(),clock ?? TimeProvider.System);
        await using var connection=factory.CreateConnection();
        // Legacy timestamps are stored in the DB session zone. Use absolute shop midnight boundaries.
        return await connection.QuerySingleAsync<SalesPeriodSummary>(new CommandDefinition("""
            WITH completed AS (
                SELECT s.sale_id,s.total_amount FROM sales s
                WHERE s.status='COMPLETED' AND s.sale_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                 AND s.sale_date<(@EndUtc AT TIME ZONE current_setting('TimeZone'))
            )
            SELECT @From::timestamp AS "From",@To::timestamp AS "To",
                (SELECT COALESCE(SUM(i.quantity::bigint),0)::bigint FROM sale_items i
                    JOIN completed s ON s.sale_id=i.sale_id) AS Quantity,
                (SELECT COUNT(*) FROM completed) AS InvoiceCount,
                (SELECT COALESCE(SUM(total_amount),0) FROM completed) AS Amount,
                (SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.sale_id=p.sale_id
                    WHERE s.status='COMPLETED' AND p.payment_mode<>'CREDIT'
                    AND p.payment_date>=(@StartUtc AT TIME ZONE current_setting('TimeZone'))
                    AND p.payment_date<(@EndUtc AT TIME ZONE current_setting('TimeZone'))) AS Collected
            """, new { dates.From,dates.To,dates.StartUtc,dates.EndUtc },cancellationToken:cancellation));
    }
    public async Task<TodaySoldQuantity> GetTodaySoldQuantityAsync()
    {
        var result=await GetSummaryAsync(new("today"),CancellationToken.None);
        return new(result.From,result.Quantity,result.InvoiceCount);
    }
}
