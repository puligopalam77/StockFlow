using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class CashClosingRepository : ICashClosingRepository
{
    private readonly DbConnectionFactory factory;
    private readonly BusinessScope scope;
    private readonly ShopTimeOptions time;
    private readonly TimeProvider clock;

    public CashClosingRepository(DbConnectionFactory factory, BusinessScope scope, IOptions<ShopTimeOptions> time, TimeProvider clock)
    {
        this.factory = factory;
        this.scope = scope;
        this.time = time.Value;
        this.clock = clock;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Sales payments are legacy timestamp-without-zone values, written in the database's
    // session time zone. Newer payment/refund tables already contain absolute timestamps.
    private const string TotalsSql = """
        WITH movements AS (
          SELECT 'SALE' source,p.payment_id id,p.amount,
            p.payment_date AT TIME ZONE current_setting('TimeZone') occurred,p.sale_id document_id
          FROM payments p WHERE p.location_id=@Location AND p.payment_mode='CASH'
          UNION ALL SELECT 'SERVICE',service_payment_id,amount,payment_date,service_payment_id FROM service_payments
            WHERE location_id=@Location AND payment_mode='CASH'
          UNION ALL SELECT 'REFUND',sale_return_id,refund_amount,created_at,sale_return_id FROM sale_returns
            WHERE location_id=@Location AND refund_mode='CASH' AND refund_amount>0
          UNION ALL SELECT 'SUPPLIER',supplier_payment_id,amount,payment_date,supplier_payment_id FROM supplier_payments
            WHERE location_id=@Location AND payment_mode='CASH'
          /*EXPENSE_CASH*/
        ), day AS (
          SELECT * FROM movements WHERE occurred>=@Start AND occurred<@End
        )
        SELECT coalesce(sum(amount) FILTER(WHERE source='SALE'),0) SalesCash,
          coalesce(sum(amount) FILTER(WHERE source='SERVICE'),0) ServiceCash,
          coalesce(sum(amount) FILTER(WHERE source='REFUND'),0) CashRefunds,
          coalesce(sum(amount) FILTER(WHERE source='SUPPLIER'),0) SupplierCash,
          coalesce(sum(amount) FILTER(WHERE source='EXPENSE'),0) ExpenseCash,
          count(*) MovementCount,
          count(DISTINCT document_id) FILTER(WHERE source='SALE') SalesCount,
          count(*) FILTER(WHERE source='SERVICE') ServiceCount,
          count(*) FILTER(WHERE source='REFUND') RefundCount,
          count(*) FILTER(WHERE source='SUPPLIER') SupplierCount,
          count(*) FILTER(WHERE source='EXPENSE') ExpenseCount,
          coalesce(jsonb_agg(jsonb_build_array(source,id,amount,occurred) ORDER BY source,id)::text,'[]') Fingerprint
        FROM day
        """;

    private object DayParameters(DateTime date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZoneId);
        var start = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        return new
        {
            Location = scope.LocationId,
            ShopTimeZone = time.TimeZoneId,
            Start = TimeZoneInfo.ConvertTimeToUtc(start, zone),
            End = TimeZoneInfo.ConvertTimeToUtc(start.AddDays(1), zone)
        };
    }

    private async Task<CashTotals> Totals(IDbConnection connection, IDbTransaction transaction, DateTime date)
    {
        var hasExpenses=await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.expense_payments') IS NOT NULL",transaction:transaction);
        // Cash Closing retains its REPORTS authority even when expense detail VIEW
        // is separately denied. The narrow function returns only cash aggregates.
        var sql=TotalsSql.Replace("/*EXPENSE_CASH*/",hasExpenses?"UNION ALL SELECT 'EXPENSE',id,amount,occurred,id FROM public.sf_expense_cash_movements(@Location,@Start,@End,@ShopTimeZone)":"");
        var totals = await connection.QuerySingleAsync<CashTotals>(sql, DayParameters(date), transaction);
        totals.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            scope.ProfileDataVisibilityAvailable ? scope.BusinessProfileCode + ":" + scope.BusinessProfileVersion + ":" + totals.Fingerprint : totals.Fingerprint)));
        return totals;
    }

    public async Task<CashDayReport> GetAsync(DateTime date)
    {
        factory.RequireLocation();
        if (date == default || date.Date > time.Today(clock))
            throw new ValidationException("Choose today or an earlier date.");
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY", transaction: transaction);
        var payloads = await connection.QueryAsync<string>("SELECT snapshot::text FROM cash_closings WHERE location_id=@Location AND business_date=@Date ORDER BY version DESC", new
        {
            Location = scope.LocationId,
            Date = date.Date
        }, transaction);
        var report = new CashDayReport
        {
            Date = date.Date,
            Location = scope.LocationName,
            TimeZone = time.TimeZoneId,
            Totals = await Totals(connection, transaction, date),
            Closings = payloads.Select(payload => JsonSerializer.Deserialize<CashClosingRecord>(payload, Json)!).ToList()
        };
        await transaction.CommitAsync();
        return report;
    }

    public async Task<CashClosingRecord> SaveAsync(CashClosingRequest request)
    {
        factory.RequireLocation(request.LocationId);
        CashClosingRules.Validate(request, time.Today(clock));
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        // Serialize closings for this location, while ordinary shop payments remain available.
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@Key,0))", new
        {
            Key = $"cash:{scope.TenantId}:{scope.LocationId}"
        }, transaction);
        var payload = JsonSerializer.Serialize(request, Json);
        var saved = await connection.QuerySingleOrDefaultAsync<SavedClosing>("SELECT request_payload::text Payload,snapshot::text Snapshot FROM cash_closings WHERE request_id=@RequestId", new
        {
            request.RequestId
        }, transaction);
        if (saved is not null)
        {
            var same = await connection.ExecuteScalarAsync<bool>("SELECT @Payload::jsonb=@Saved::jsonb", new
            {
                Payload = payload,
                Saved = saved.Payload
            }, transaction);
            if (!same)
                throw new ValidationException("This request ID was already used for different closing details.");
            await transaction.CommitAsync();
            return JsonSerializer.Deserialize<CashClosingRecord>(saved.Snapshot, Json)!;
        }
        var version = await connection.ExecuteScalarAsync<int>("SELECT coalesce(max(version),0) FROM cash_closings WHERE location_id=@Location AND business_date=@Date", new
        {
            Location = scope.LocationId,
            Date = request.Date
        }, transaction);
        var totals = await Totals(connection, transaction, request.Date);
        if (version != request.ExpectedVersion || totals.Fingerprint != request.Fingerprint)
            throw new ValidationException("Cash activity or the previous closing changed. Refresh, recount and try again.");
        var expected = CashClosingRules.Expected(request, totals);
        CashClosingRules.CheckReason(request, expected);
        var record = JsonSerializer.Deserialize<CashClosingRecord>(payload, Json)!;
        record.ClosingId = await connection.ExecuteScalarAsync<long>("SELECT nextval('cash_closings_closing_id_seq')", transaction: transaction);
        record.Version = version + 1;
        record.Totals = totals;
        record.ExpectedCash = expected;
        record.Difference = request.CountedCash - expected;
        record.ClosedBy = scope.UserId;
        record.ClosedAt = clock.GetUtcNow();
        await connection.ExecuteAsync("""
            INSERT INTO cash_closings(closing_id,location_id,business_date,version,request_id,request_payload,snapshot,closed_by)
            VALUES(@Id,@Location,@Date,@Version,@RequestId,@Payload::jsonb,@Snapshot::jsonb,@User)
            """, new
        {
            Id = record.ClosingId,
            Location = scope.LocationId,
            request.Date,
            record.Version,
            request.RequestId,
            Payload = payload,
            Snapshot = JsonSerializer.Serialize(record, Json),
            User = scope.UserId
        }, transaction);
        await transaction.CommitAsync();
        return record;
    }

    private sealed class SavedClosing
    {
        public string Payload { get; set; } = "";
        public string Snapshot { get; set; } = "";
    }
}
