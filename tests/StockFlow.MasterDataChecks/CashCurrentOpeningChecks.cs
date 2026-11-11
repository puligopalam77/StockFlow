using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class CashCurrentOpeningChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS current opening: "+label);}
    static async Task Reject(Func<Task> f,string label){try{await f();}catch(Exception e)when(e is ValidationException or PostgresException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
    public static async Task Run(NpgsqlConnection c,IConfiguration config,IOptions<ShopTimeOptions> time,DateTime today)
    {
        var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=2,LocationId=3,IsOwner=true,CanReviewStores=true,BusinessProfileCode="ELECTRICAL"};
        var factory=new DbConnectionFactory(config,scope);var repo=new CashInHandRepository(factory,scope,time,TimeProvider.System);
        CashAdjustmentRequest Opening(decimal value)=>new(){RequestId=Guid.NewGuid(),LocationId=scope.LocationId,Kind="OPENING",OpeningMode="CURRENT",Amount=value,Reason="Physical drawer cash counted"};
        var expense=Guid.NewGuid();
        await c.ExecuteAsync("""
            INSERT INTO store_locations(tenant_id,store_id,location_id) VALUES(1,2,3),(1,2,4),(1,2,5),(1,2,6);
            INSERT INTO sales VALUES(1,3,300,'ALREADY-COUNTED');
            INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date) VALUES(1,3,300,300,'CASH',200,now());
            INSERT INTO expenses(tenant_id,expense_id,description,store_id) VALUES(1,@expense,'Opening-day payment timing',2);
            INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,reference) VALUES(1,3,@expense,300,50,'CASH',@today,'ALREADY-PAID');
            """,new{expense,today});
        var opening=Opening(1000);opening.Reason="";opening.Note="Cash already includes earlier sales and expenses";
        await repo.AdjustAsync(opening);await repo.AdjustAsync(opening);
        var report=await repo.GetAsync(today);
        Check(report.Balance==1000&&report.Received==0&&report.Paid==0&&report.TrackingStartedAt!=null,"current count excludes already-posted payments on the same date and stores an exact cutoff");
        Check(await c.ExecuteScalarAsync<bool>("SELECT reason=@reason AND request_payload->>'note'=@note AND created_by=1 AND tracking_started_at IS NOT NULL FROM cash_adjustments WHERE adjustment_id=@id",new{reason=CashInHandRules.OpeningReason,note=opening.Note,id=opening.RequestId}),"optional opening note stored separately from system audit reason with actor and backend timestamp");
        Check((await repo.GetAsync(today,opening.Note)).Movements.Single().Description.Contains(opening.Note),"opening note remains readable and searchable in transaction history");
        foreach(var invalid in new[]{-1m,0.001m,10000000000m})await Reject(()=>repo.AdjustAsync(Opening(invalid)),"invalid opening amount rejected without partial writes: "+invalid);
        await Reject(()=>repo.AdjustAsync(new(){RequestId=Guid.NewGuid(),LocationId=999,Kind="OPENING",OpeningMode="CURRENT",Amount=1}),"opening cannot initialize a client-selected unauthorized shop");
        var legacyPayload=System.Text.Json.JsonSerializer.Serialize(Opening(1));
        Check(!legacyPayload.Contains("Note"),"absent note is omitted so legacy request replay serialization remains unchanged");
        try{System.Text.Json.JsonSerializer.Deserialize<CashAdjustmentRequest>("{\"Kind\":\"OPENING\"}");throw new Exception("Missing amount accepted");}catch(System.Text.Json.JsonException){Check(true,"omitted JSON amount is rejected rather than treated as zero");}
        await c.ExecuteAsync("""
            INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date) VALUES(1,3,301,300,'CASH',200,now());
            INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,reference) VALUES
             (1,3,@expense,301,80,'CASH',@today,'NEEDS-TIMING'),(1,3,@expense,302,70,'CASH',@earlier,'BEFORE-TRACKING');
            """,new{expense,today,earlier=today.AddDays(-1)});
        report=await repo.GetAsync(today);
        Check(report.PendingReviewCount==1&&report.OpeningExpenses.Single().MovementId==301,"same-day payment requires owner timing; earlier-dated payment is excluded");
        var closing=new CashClosingRepository(factory,scope,time,TimeProvider.System);var day=await closing.GetAsync(today);
        await Reject(()=>closing.SaveAsync(new(){RequestId=Guid.NewGuid(),LocationId=3,Date=today,Fingerprint=day.Totals.Fingerprint,OpeningCash=1000,CountedCash=1200,Reason="Cannot close unreviewed timing"}),"unresolved timing cannot produce a confirmed cash closing");
        var review=new CashOpeningReviewRequest{RequestId=Guid.NewGuid(),LocationId=3,MovementId=301,Timing="BEFORE",Reason="Owner confirms this cash was paid before the count"};
        await repo.ReviewOpeningExpenseAsync(review);await repo.ReviewOpeningExpenseAsync(review);
        report=await repo.GetAsync(today);Check(report.PendingReviewCount==0&&report.Balance==1200,"BEFORE decision excludes payment already reflected in opening; replay has no duplicate effect");
        await c.ExecuteAsync("INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,reference) VALUES(1,3,@expense,303,100,'CASH',@today,'AFTER-COUNT')",new{expense,today});
        await repo.ReviewOpeningExpenseAsync(new(){RequestId=Guid.NewGuid(),LocationId=3,MovementId=303,Timing="AFTER",Reason="Cash paid after the opening count"});
        Check((await repo.GetAsync(today)).Balance==1100,"AFTER decision deducts actual cash once");
        var correction=new CashOpeningReviewRequest{RequestId=Guid.NewGuid(),LocationId=3,MovementId=301,ExpectedVersion=1,Timing="AFTER",Reason="Owner corrected timing after checking receipt"};
        await repo.ReviewOpeningExpenseAsync(correction);
        report=await repo.GetAsync(today);Check(report.Balance==1020&&report.PendingReviewCount==0,"timing corrections append a new version without overwriting opening");
        Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM cash_opening_expense_reviews WHERE movement_id=301")==2,"both timing decisions remain auditable");
        await Reject(()=>repo.ReviewOpeningExpenseAsync(new(){RequestId=Guid.NewGuid(),LocationId=3,MovementId=301,ExpectedVersion=1,Timing="BEFORE",Reason="Stale correction"}),"stale timing correction rejected");
        await Reject(()=>repo.ReviewOpeningExpenseAsync(new(){RequestId=Guid.NewGuid(),LocationId=3,MovementId=300,Timing="AFTER",Reason="Already counted"}),"pre-opening payment cannot be added a second time");
        day=await closing.GetAsync(today);Check(day.Totals.TrackingOpeningCash==1000&&day.Totals.NetCash==20,"cash closing agrees with current-time opening and reviewed payments");
        Check(report.Movements.Where(x=>x.Type=="EXPENSE").All(x=>x.DateOnly),"date-only source payments never claim an invented payment time");

        scope.LocationId=4;
        await using(var payment=factory.CreateConnection())
        {
            await payment.OpenAsync();await using var t=await payment.BeginTransactionAsync();
            await payment.ExecuteAsync("INSERT INTO sales VALUES(1,4,400,'CONCURRENT-BEFORE');INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date) VALUES(1,4,400,400,'CASH',200,now())",transaction:t);
            var waiting=repo.AdjustAsync(Opening(5000));
            Check(await Task.WhenAny(waiting,Task.Delay(200))!=waiting,"opening waits for an in-flight authoritative payment transaction");
            await t.CommitAsync();await waiting;
            Check((await repo.GetAsync(today)).Balance==5000,"payment committed before initialization is included in count, not added twice");
            await Reject(()=>repo.ReviewOpeningExpenseAsync(new(){RequestId=Guid.NewGuid(),LocationId=4,MovementId=301,Timing="AFTER",Reason="Foreign location review"}),"review cannot classify another location's cash payment");
        }
        scope.LocationId=5;
        await using(var payment=factory.CreateConnection())
        {
            await payment.OpenAsync();await using var t=await payment.BeginTransactionAsync();
            await payment.ExecuteScalarAsync<DateTime>("SELECT now()::timestamp",transaction:t);
            await repo.AdjustAsync(Opening(5000));
            await payment.ExecuteAsync("INSERT INTO sales VALUES(1,5,500,'CONCURRENT-AFTER');INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date) VALUES(1,5,500,500,'CASH',200,now())",transaction:t);await t.CommitAsync();
            Check((await repo.GetAsync(today)).Balance==5200,"post-opening payment counts once even if its transaction timestamp started earlier");
        }
        scope.LocationId=6;
        async Task<bool> TryOpen(CashAdjustmentRequest request){try{await repo.AdjustAsync(request);return true;}catch(ValidationException){return false;}}
        var races=await Task.WhenAll(TryOpen(Opening(0)),TryOpen(Opening(0)));
        Check(races.Count(x=>x)==1&&(await repo.GetAsync(today)).Balance==0,"concurrent zero-cash initialization succeeds once and displays a genuine zero balance");

        scope.LocationId=7;
        await c.ExecuteAsync("INSERT INTO store_locations(tenant_id,store_id,location_id) VALUES(1,2,7)");
        await repo.AdjustAsync(Opening(5000));
        var exampleExpense=Guid.NewGuid();
        await c.ExecuteAsync("""
            INSERT INTO sales VALUES(1,7,700,'CASH-EXAMPLE'),(1,7,701,'UPI-EXAMPLE'),(1,7,702,'UNPAID-NO-RECEIPT');
            INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date) VALUES
             (1,7,700,700,'CASH',2000,now()),(1,7,701,701,'UPI',1000,now());
            INSERT INTO expenses(tenant_id,expense_id,description,store_id) VALUES(1,@exampleExpense,'Example cash expense',2);
            INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,reference) VALUES(1,7,@exampleExpense,700,500,'CASH',@today,'EXAMPLE-EXPENSE');
            """,new{exampleExpense,today});
        await repo.ReviewOpeningExpenseAsync(new(){RequestId=Guid.NewGuid(),LocationId=7,MovementId=700,Timing="AFTER",Reason="Expense paid after this physical opening count"});
        var add=new CashAdjustmentRequest{RequestId=Guid.NewGuid(),LocationId=7,Kind="OWNER_IN",Amount=1000,ReasonCode="OWNER_CONTRIBUTION"};
        var remove=new CashAdjustmentRequest{RequestId=Guid.NewGuid(),LocationId=7,Kind="OWNER_OUT",Amount=500,ReasonCode="OWNER_WITHDRAWAL"};
        await repo.AdjustAsync(add);await repo.AdjustAsync(remove);await repo.AdjustAsync(add);await repo.AdjustAsync(remove);
        report=await repo.GetAsync(today);
        Check(report.Balance==7000&&report.Received==3000&&report.Paid==1000&&report.Net==2000,"requested 5000 + 2000 cash - 500 expense + 1000 owner in - 500 owner out = 7000; UPI and unpaid sale excluded");
        Check(report.Movements.Count==5&&report.Movements.Sum(x=>x.Amount)==7000,"authoritative history reconciles with KPI balance; retries do not duplicate movements");
        var again=await repo.GetAsync(today);
        Check(report.Movements.Select(x=>(x.Id,x.Balance)).SequenceEqual(again.Movements.Select(x=>(x.Id,x.Balance))),"identical source timestamps retain deterministic order and running balances");
        day=await closing.GetAsync(today);
        Check(day.Totals.TrackingOpeningCash==5000&&day.Totals.NetCash==2000,"cash closing shares the requested example's 7000 expected balance");

        // Isolated reproduction of the reported 50,000 opening and next-day zero table.
        await c.ExecuteAsync("INSERT INTO store_locations(tenant_id,store_id,location_id) VALUES(1,2,10),(1,2,11)");
        scope.LocationId=10;
        var fiftyThousand=Opening(50000);await repo.AdjustAsync(fiftyThousand);
        Check((await repo.GetAsync(today)).Balance==50000,"A: 50,000 physical opening persists and is included exactly once");
        var nextDay=new NextDayClock(TimeProvider.System.GetUtcNow().AddDays(1));
        var futureRepo=new CashInHandRepository(factory,scope,time,nextDay);
        var tomorrow=today.AddDays(1);var carried=await futureRepo.GetAsync(tomorrow);
        Check(carried.Balance==50000&&carried.Received==0&&carried.Paid==0&&carried.Net==0&&carried.Total==0,
            "D: next-day empty history carries 50,000; daily received/paid/net remain zero");
        CashAdjustmentRequest Entry(string kind,decimal amount)=>new(){RequestId=Guid.NewGuid(),LocationId=10,Kind=kind,Amount=amount,ReasonCode=kind=="OWNER_IN"?"OWNER_CONTRIBUTION":"OWNER_WITHDRAWAL"};
        await repo.AdjustAsync(Entry("OWNER_OUT",5000));
        Check((await repo.GetAsync(today)).Balance==45000,"B: 50,000 opening minus 5,000 withdrawal = 45,000");
        await repo.AdjustAsync(Entry("OWNER_IN",2000));
        Check((await repo.GetAsync(today)).Balance==47000,"C: 50,000 opening plus 2,000 contribution minus 5,000 withdrawal = 47,000");
        var reopened=new CashInHandRepository(new DbConnectionFactory(config,scope),scope,time,nextDay);
        Check((await reopened.GetAsync(tomorrow)).Balance==47000,"F: fresh repository retains authoritative carried-forward balance");
        await repo.AdjustAsync(Entry("OWNER_OUT",47000));
        carried=await futureRepo.GetAsync(tomorrow);
        Check(carried.Balance==0&&carried.Total==0&&carried.Received==0&&carried.Paid==0,
            "reported zero is valid when posted withdrawals offset opening on the previous day");
        Check(await c.ExecuteScalarAsync<decimal>("SELECT amount FROM cash_adjustments WHERE adjustment_id=@id",new{id=fiftyThousand.RequestId})==50000,
            "withdrawals and date changes never overwrite the 50,000 opening record");
        scope.LocationId=11;await repo.AdjustAsync(Opening(123));
        Check((await repo.GetAsync(today)).Balance==123,"E: separate location retains its own independent opening");
        scope.LocationId=10;
        Check((await repo.GetAsync(today)).Balance==0,"E: switching back restores original location balance without mixing cash");
    }

    private sealed class NextDayClock(DateTimeOffset now):TimeProvider
    {
        public override DateTimeOffset GetUtcNow()=>now;
    }
}
