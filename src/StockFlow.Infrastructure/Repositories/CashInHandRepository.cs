using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class CashInHandRepository(DbConnectionFactory factory, BusinessScope scope, IOptions<ShopTimeOptions> options, TimeProvider clock) : ICashInHandRepository
{
    private readonly ShopTimeOptions time=options.Value;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    private void Authorize()
    {
        factory.RequireLocation();
        if(!scope.CanReviewStores) throw new UnauthorizedAccessException("Only the shop owner or administrator can review or adjust cash.");
    }

    public async Task<CashInHandReport> GetAsync(DateTime date, string? search=null, int page=1, string? type=null, string? direction=null)
    {
        Authorize();
        if(date==default || date.Date>time.Today(clock) || date.Year<2000 || page<1 || page>1000000 || (search?.Length??0)>150 || direction is not (null or "" or "IN" or "OUT"))
            throw new ValidationException("Choose a valid date, search and page.");
        var report=new CashInHandReport{Date=date.Date,Today=time.Today(clock),TimeZone=time.TimeZoneId,Page=page};
        await using var c=factory.CreateConnection(); await c.OpenAsync();
        await using var t=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await c.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:t);
        report.Available=await CashReadModel.Available(c,t);
        if(!report.Available) return report;
        if(!await c.ExecuteScalarAsync<bool>("SELECT coalesce(sf_cash_allowed(),false)",transaction:t))
            throw new UnauthorizedAccessException("Cash access is not permitted for the active shop.");
        var parameters=CashReadModel.Parameters(scope,time,date,search,page,type,direction);
        var summary=await CashReadModel.Read(c,t,parameters);
        var sql=await CashReadModel.Sql(c,t);
        report.TrackingStart=summary.TrackingStart;report.OpeningCash=summary.OpeningCash;report.Balance=summary.Balance;
        report.TrackingStartedAt=summary.TrackingStartedAt;report.PendingReviewCount=summary.PendingReviewCount;
        report.OpeningExpenses=(await c.QueryAsync<CashOpeningExpense>(sql+"SELECT source_number MovementId,reference,description,amount,timing,review_version Version FROM review_candidates ORDER BY timing IS NULL DESC,source_number DESC LIMIT 20",parameters,t)).ToList();
        report.Received=summary.Received;report.Paid=summary.Paid;
        if(summary.TrackingStart is null || date.Date<summary.TrackingStart.Value.Date || summary.PendingReviewCount>0)
            report.Balance=report.Received=report.Paid=null;
        const string filter="occurred>=@Start AND (@Search='' OR position(lower(@Search) in lower(reference||' '||type||' '||description))>0) AND (@Type='' OR type=@Type) AND (@Direction='' OR @Direction='IN' AND amount>0 OR @Direction='OUT' AND amount<0)";
        report.Total=await c.ExecuteScalarAsync<long>(sql+"SELECT count(*) FROM tracked WHERE "+filter,parameters,t);
        report.Page=report.Total==0?0:(int)Math.Min(page,(report.Total+19)/20);
        parameters=CashReadModel.Parameters(scope,time,date,search,Math.Max(1,report.Page),type,direction);
        report.Movements=(await c.QueryAsync<CashMovement>(sql+"""
            , balanced AS (SELECT *,sum(amount) OVER(ORDER BY occurred,CASE WHEN type='OPENING' THEN 0 ELSE 1 END,id ROWS UNBOUNDED PRECEDING) balance FROM tracked)
            SELECT id,occurred,reference,type,description,amount,balance,adjustment_id AdjustmentId,can_reverse CanReverse,
              source_module SourceModule,source_url SourceUrl,date_only DateOnly,
              (SELECT 'ADJ-'||lpad(a.movement_number::text,greatest(4,length(a.movement_number::text)),'0') FROM cash_adjustments a JOIN cash_adjustments r ON r.reverses_id=a.adjustment_id WHERE r.adjustment_id=balanced.adjustment_id) ReversesReference,
              (SELECT 'ADJ-'||lpad(r.movement_number::text,greatest(4,length(r.movement_number::text)),'0') FROM cash_adjustments r WHERE r.reverses_id=balanced.adjustment_id) ReversedByReference
            FROM balanced WHERE
            """+" "+filter+" ORDER BY occurred DESC,CASE WHEN type='OPENING' THEN 0 ELSE 1 END DESC,id DESC LIMIT 20 OFFSET @Offset",parameters,t)).ToList();
        if(report.PendingReviewCount>0)foreach(var movement in report.Movements)movement.Balance=null;
        var closing=await c.QuerySingleOrDefaultAsync<string>("SELECT snapshot::text FROM cash_closings WHERE location_id=@Location AND business_date=@Date ORDER BY version DESC LIMIT 1",new{Location=scope.LocationId,Date=date.Date},t);
        if(closing is not null) report.Closing=JsonSerializer.Deserialize<CashClosingRecord>(closing,Json);
        await t.CommitAsync();return report;
    }

    public async Task<Guid> AdjustAsync(CashAdjustmentRequest request)
    {
        Authorize();factory.RequireLocation(request.LocationId);
        var legacyBankReplay=request.Kind is "BANK_DEPOSIT" or "BANK_WITHDRAWAL";
        var legacyManualReplay=request.Kind is ("OWNER_IN" or "OWNER_OUT") && request.ReasonCode is null;
        CashInHandRules.Validate(request,time.Today(clock),allowLegacyBankReplay:legacyBankReplay,allowLegacyManualReplay:legacyManualReplay);
        if(request.BusinessProfileCode is not null && request.BusinessProfileCode!=scope.BusinessProfileCode)
            throw new ValidationException("Shop business profile changed. Reload cash tracking before posting.");
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c,t);
        if(!await CashReadModel.Available(c,t)) throw new ValidationException("Cash tracking is not available yet. Ask your administrator to enable it.");
        if(request.OpeningMode=="CURRENT" && !await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.cash_opening_expense_reviews') IS NOT NULL",transaction:t))
            throw new ValidationException("The cash tracking update must be installed before counting the current drawer opening.");
        if(legacyBankReplay||legacyManualReplay)
        {
            // Confirmation only, including when the release-scope migration is not installed.
            var exists=await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM cash_adjustments WHERE adjustment_id=@RequestId AND location_id=@Location AND created_by=@Actor AND request_payload=@Payload::jsonb)",new{request.RequestId,Location=scope.LocationId,Actor=scope.UserId,Payload=JsonSerializer.Serialize(request,Json)},t);
            if(!exists)throw new ValidationException(legacyBankReplay?"Bank transactions and transfers are deferred. Cash Entry supports owner contributions or withdrawals.":"Choose a valid reason for the selected cash entry type. Historical free-text requests can only be confirmed, not newly posted.");
            await t.CommitAsync();return request.RequestId;
        }
        try
        {
            var id=await c.ExecuteScalarAsync<Guid>("SELECT sf_cash_adjust(@RequestId,@Body::jsonb,@Today::date)",new{request.RequestId,Body=JsonSerializer.Serialize(request,Json),Today=time.Today(clock)},t);
            await t.CommitAsync();return id;
        }
        catch(PostgresException e) when(e.SqlState is "23514" or "23505")
        { throw new ValidationException(e.SqlState=="23505"?"Opening or reversal already recorded. Refresh cash activity.":e.MessageText); }
    }

    public async Task ReviewOpeningExpenseAsync(CashOpeningReviewRequest request)
    {
        Authorize();factory.RequireLocation(request.LocationId);request.Reason=request.Reason?.Trim()??"";
        if(request.RequestId==Guid.Empty || request.MovementId<=0 || request.ExpectedVersion<0 || request.Timing is not("BEFORE" or "AFTER") || request.Reason.Length is <3 or >1000)
            throw new ValidationException("Choose whether the cash payment happened before or after opening and record a reason.");
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,t);
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.cash_opening_expense_reviews') IS NOT NULL",transaction:t))throw new ValidationException("The cash tracking update is not installed.");
        try
        {
            await c.ExecuteAsync("SELECT sf_cash_review_expense(@RequestId,@MovementId,@ExpectedVersion,@Timing,@Reason)",request,t);
            await t.CommitAsync();
        }
        catch(PostgresException e) when(e.SqlState is "23514" or "23505") {throw new ValidationException(e.MessageText);}
    }
}
