using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class CashInHandChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS cash: "+label);}
    static async Task Reject(Func<Task> f,string label){try{await f();}catch(Exception e)when(e is ValidationException or PostgresException or UnauthorizedAccessException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55441;Username=postgres;Database=";
        var db="cash_in_hand_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("DO $$BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime') THEN CREATE ROLE stockflow_runtime NOLOGIN; END IF; END$$");
        await admin.ExecuteAsync("CREATE DATABASE "+db);
        try
        {
            await using var c=new NpgsqlConnection(host+db);await c.OpenAsync();
            await c.ExecuteAsync(await File.ReadAllTextAsync("tests/StockFlow.MasterDataChecks/CashInHandFixture.sql"));
            await c.ExecuteAsync(await File.ReadAllTextAsync("database/migrations/087_cash_in_hand.sql"));
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db}}).Build();
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,CanReviewStores=true,IsOwner=true,BusinessProfileCode="ELECTRICAL"};
            var factory=new DbConnectionFactory(config,scope);var time=Options.Create(new ShopTimeOptions());var today=time.Value.Today(TimeProvider.System);var date=today.AddDays(-1);
            var repo=new CashInHandRepository(factory,scope,time,TimeProvider.System);
            CashAdjustmentRequest Movement(string kind,decimal amount)=>new(){RequestId=Guid.NewGuid(),LocationId=scope.LocationId,Kind=kind,Amount=amount,Reason="Cash integration test",ReasonCode=kind=="OWNER_IN"?"OWNER_CONTRIBUTION":kind=="OWNER_OUT"?"OWNER_WITHDRAWAL":null};
            Check((await repo.GetAsync(today)).TrackingStart is null,"migration does not assume or backfill opening cash");
            await Reject(()=>repo.AdjustAsync(Movement("OWNER_IN",100)),"adjustments require controlled opening");
            var opening=Movement("OPENING",5000);opening.StartDate=date;await repo.AdjustAsync(opening);
            await repo.AdjustAsync(opening);Check((await repo.GetAsync(date)).Balance==5000,"opening replay creates one record");
            var otherOpening=Movement("OPENING",7000);otherOpening.StartDate=date;await Reject(()=>repo.AdjustAsync(otherOpening),"second opening rejected");
            var expense1=Guid.NewGuid();var expense2=Guid.NewGuid();
            await c.ExecuteAsync("""
                INSERT INTO sales VALUES(1,1,1,'SAL-0241'),(1,1,2,'UPI-SALE'),(2,1,1,'OTHER-TENANT');
                INSERT INTO payments VALUES(1,1,1,1,'CASH',2000,@day::date+time '05:30'),(1,1,2,2,'UPI',3000,@day::date+time '06:30'),(2,1,3,1,'CASH',999999,@day::date+time '05:30');
                INSERT INTO expenses VALUES(1,@expense1,'Electricity Bill'),(1,@expense2,'Shop Cleaning');
                INSERT INTO expense_payments VALUES(1,1,@expense1,1,800,'CASH',@day,'EXP-0033'),(1,1,@expense2,2,200,'CASH',@day,'EXP-0034'),(1,1,@expense1,3,500,'UPI',@day,'UPI-EXP');
                """,new{day=date,expense1,expense2});
            var deposit=Movement("BANK_DEPOSIT",2000);
            await c.ExecuteAsync("SET app.tenant_id='1';SET app.user_id='1';SET app.store_id='1';SET app.location_id='1';SET app.business_profile_code='ELECTRICAL'");
            await c.ExecuteAsync("SELECT sf_cash_adjust(@RequestId,@body::jsonb,@today::date)",new{deposit.RequestId,body=JsonSerializer.Serialize(deposit,new JsonSerializerOptions(JsonSerializerDefaults.Web)),today});
            await c.ExecuteAsync(await File.ReadAllTextAsync("database/migrations/088_cash_entry_release_scope.sql"));
            var legacyManual=new CashAdjustmentRequest{RequestId=Guid.NewGuid(),LocationId=9,Kind="OWNER_IN",Amount=5,Reason="Legacy envelope funds"};
            await c.ExecuteAsync("""
                INSERT INTO store_locations(tenant_id,store_id,location_id) VALUES(1,1,9);
                INSERT INTO cash_adjustments(tenant_id,store_id,location_id,business_profile_code,adjustment_id,kind,amount,reason,start_date,request_payload,created_by)
                VALUES(1,1,9,'ELECTRICAL',@opening,'OPENING',100,'Historical opening',@date,'{}',1),
                (1,1,9,'ELECTRICAL',@id,'OWNER_IN',5,@reason,NULL,@payload::jsonb,1);
                """,new{opening=Guid.NewGuid(),date,id=legacyManual.RequestId,reason=legacyManual.Reason,payload=JsonSerializer.Serialize(legacyManual,new JsonSerializerOptions(JsonSerializerDefaults.Web))});
            await c.ExecuteAsync(await File.ReadAllTextAsync("database/migrations/089_cash_entry_reasons.sql"));
            scope.LocationId=9;await repo.AdjustAsync(legacyManual);
            Check((await repo.GetAsync(today)).Balance==105&&(await repo.GetAsync(today)).Movements.Any(m=>m.Description=="Legacy envelope funds"),"historical free-text reason and exact old request replay remain intact after reason guard");
            legacyManual.RequestId=Guid.NewGuid();await Reject(()=>repo.AdjustAsync(legacyManual),"new code-less manual entries cannot bypass reason validation");scope.LocationId=1;
            await repo.AdjustAsync(deposit);
            await Reject(()=>repo.AdjustAsync(Movement("BANK_DEPOSIT",10)),"new bank deposits are deferred, not disguised as adjustments");
            await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();await Reject(()=>runtime.ExecuteAsync("SELECT sf_cash_adjust(@id,@body::jsonb,@today::date)",new{id=Guid.NewGuid(),body=JsonSerializer.Serialize(Movement("BANK_WITHDRAWAL",10),new JsonSerializerOptions(JsonSerializerDefaults.Web)),today}),"SQL blocks new bank-only entries while retaining legacy history");Check(!await runtime.ExecuteScalarAsync<bool>("SELECT has_function_privilege(current_user,'sf_cash_adjust_day_start(uuid,jsonb,date)','EXECUTE')"),"private legacy implementation cannot bypass release policy");}
            var report=await repo.GetAsync(today);
            Check(report.Balance==4000,"legacy recorded example reconciles to 4000; live bank-transfer integration is deferred");
            Check(report.Paid==2000&&report.Net==-2000,"selected-day KPIs exclude opening and earlier payments");
            var previous=await repo.GetAsync(date);Check(previous.Received==2000&&previous.Paid==1000&&previous.Balance==6000,"historical as-of balance and daily movements");
            Check((await repo.GetAsync(date,"Electricity")).Movements.Single().Balance==4200,"search preserves unfiltered running balance");
            Check((await repo.GetAsync(today,type:"BANK_DEPOSIT")).Movements.Single().Amount==-2000,"bank deposit is an adjustment, never an expense");
            var concurrent=Movement("OWNER_IN",100);await Task.WhenAll(repo.AdjustAsync(concurrent),repo.AdjustAsync(concurrent));Check((await repo.GetAsync(today)).Balance==4100,"concurrent request retry posts once");
            concurrent.Amount=200;await Reject(()=>repo.AdjustAsync(concurrent),"changed retry payload rejected");
            var reversal=Movement("REVERSAL",2000);reversal.ReversesId=deposit.RequestId;await repo.AdjustAsync(reversal);Check((await repo.GetAsync(today)).Balance==6100,"adjustment reversal appends opposite movement");
            var duplicateReverse=Movement("REVERSAL",2000);duplicateReverse.ReversesId=deposit.RequestId;await Reject(()=>repo.AdjustAsync(duplicateReverse),"duplicate reversal rejected");
            await c.ExecuteAsync("""
                INSERT INTO payments VALUES(1,1,4,1,'CASH',50,@day::date+time '07:00');
                INSERT INTO payments VALUES(1,1,5,1,'UPI',100,@day::date+time '07:00'),(1,1,6,1,'CARD',100,@day::date+time '07:00'),(1,1,7,1,'BANK',100,@day::date+time '07:00'),(1,1,8,1,'CREDIT',100,@day::date+time '07:00');
                INSERT INTO supplier_payments VALUES(1,1,1,300,'CASH','Partial supplier settlement',@day::date+time '07:10'),(1,1,2,1000,'BANK','Digital supplier settlement',@day::date+time '07:11');
                INSERT INTO sale_returns VALUES(1,1,1,'RET-1',100,'CASH',@day::date+time '07:20'),(1,1,2,'RET-CREDIT',0,'CREDIT_ADJUSTMENT',@day::date+time '07:21');
                INSERT INTO service_payments VALUES(1,1,1,70,'CASH',@day::date+time '07:25');
                INSERT INTO expense_payments VALUES(1,1,@expense1,4,-800,'CASH',@day,'EXP-REVERSE');
                """,new{day=today,expense1});
            report=await repo.GetAsync(today);Check(report.Balance==6620,"collections, partial supplier cash, cash refund, repair cash, signed expense reversal counted exactly once");
            Check(report.Received==3020&&report.Paid==2400,"mixed payment modes on the same invoice count only their actual cash component");
            var closingRepo=new CashClosingRepository(factory,scope,time,TimeProvider.System);var closing=await closingRepo.GetAsync(today);
            Check(closing.Totals.TrackingOpeningCash==6000&&closing.Totals.NetCash==620,"cash closing reuses tracked carry-forward and same movements");
            var close=new CashClosingRequest{RequestId=Guid.NewGuid(),LocationId=1,Date=today,Fingerprint=closing.Totals.Fingerprint,OpeningCash=6000,CountedCash=6600,Reason="Drawer count difference"};
            var saved=await closingRepo.SaveAsync(close);Check(saved.ExpectedCash==6620&&saved.Difference==-20,"actual count reconciliation records variance without changing balance");
            Check((await closingRepo.SaveAsync(close)).ClosingId==saved.ClosingId&&(await repo.GetAsync(today)).Balance==6620,"closing retry and closing itself never move cash");
            close.RequestId=Guid.NewGuid();close.ExpectedVersion=1;close.CashAdded=1;await Reject(()=>closingRepo.SaveAsync(close),"closing cannot duplicate tracked adjustments");
            scope.StoreId=2;scope.LocationId=2;Check((await repo.GetAsync(today)).TrackingStart is null,"shop switch isolates cash context");
            await Reject(()=>repo.AdjustAsync(Movement("OWNER_IN",100)),"second shop cannot spend first shop opening");
            await c.ExecuteAsync("INSERT INTO cash_closings(tenant_id,location_id,business_date,version,snapshot) VALUES(1,2,@date,1,'{\"cashAdded\":50,\"cashRemoved\":0}')",new{date});
            var legacyOpening=Movement("OPENING",50);legacyOpening.StartDate=date;
            await Reject(()=>repo.AdjustAsync(legacyOpening),"opening cannot silently omit legacy closing-only cash movements");
            legacyOpening.RequestId=Guid.NewGuid();legacyOpening.StartDate=today;await repo.AdjustAsync(legacyOpening);
            Check((await repo.GetAsync(today)).Balance==50,"verified opening after legacy closing starts without rewriting it");
            scope.StoreId=1;scope.LocationId=1;scope.TenantId=2;Check((await repo.GetAsync(today)).TrackingStart is null,"other tenant cannot read cash tracking");
            scope.TenantId=1;scope.BusinessProfileCode=BusinessProfiles.Clothing;Check((await repo.GetAsync(today)).TrackingStart is null,"profile switch cannot reuse another profile opening");scope.BusinessProfileCode=BusinessProfiles.Electrical;
            scope.TenantId=1;scope.UserId=2;scope.CanReviewStores=false;await Reject(()=>repo.GetAsync(today),"employee denied by repository");
            await using(var runtime=factory.CreateConnection()) {await runtime.OpenAsync();Check(await runtime.ExecuteScalarAsync<long>("SELECT count(*) FROM cash_adjustments")==0,"RLS hides adjustments from unauthorized employee");await Reject(()=>runtime.ExecuteAsync("SELECT sf_cash_adjust(@id,@body::jsonb,@today::date)",new{id=Guid.NewGuid(),body=JsonSerializer.Serialize(Movement("OWNER_IN",100),new JsonSerializerOptions(JsonSerializerDefaults.Web)),today}),"database rejects direct unauthorized adjustment");}
            scope.UserId=1;scope.CanReviewStores=true;
            scope.UserId=3;await Reject(()=>repo.GetAsync(today),"database report authority is required even for an admin scope");await Reject(()=>repo.AdjustAsync(Movement("OWNER_IN",10)),"admin without report permission cannot adjust through database");scope.UserId=1;
            await c.ExecuteAsync("UPDATE store_locations SET is_active=false WHERE tenant_id=1 AND location_id=1");await Reject(()=>repo.GetAsync(today),"inactive location cash access is denied");await c.ExecuteAsync("UPDATE store_locations SET is_active=true WHERE tenant_id=1 AND location_id=1");
            await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();await Reject(()=>runtime.ExecuteAsync("UPDATE cash_adjustments SET amount=1"),"runtime cannot mutate cash history");await Reject(()=>runtime.ExecuteAsync("DELETE FROM cash_adjustments"),"runtime cannot delete cash history");}
            Check((await repo.GetAsync(today,"no-such-reference")).Total==0,"search has no fabricated entries");
            Check((await repo.GetAsync(today,"no-such-reference")).Page==0,"empty results have no active page");
            Check((await repo.GetAsync(today,direction:"IN")).Movements.All(x=>x.Amount>0)&& (await repo.GetAsync(today,direction:"OUT")).Movements.All(x=>x.Amount<0),"cash direction filters preserve source amounts");
            Check((await repo.GetAsync(today)).Movements.Any(x=>x.ReversesReference!=null)&& (await repo.GetAsync(today)).Movements.Any(x=>x.ReversedByReference!=null),"reversal links remain visible on both original and correction");
            var output="tests/bin/cash-in-hand-fixtures";Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(output+"/report.json",JsonSerializer.Serialize(await repo.GetAsync(today),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            for(var n=0;n<23;n++)await repo.AdjustAsync(Movement("OWNER_IN",1));
            var firstPage=await repo.GetAsync(today);var secondPage=await repo.GetAsync(today,page:2);
            Check(firstPage.Total>20&&firstPage.Movements.Count==20&&secondPage.Movements.Count>0&&!firstPage.Movements.Select(x=>x.Id).Intersect(secondPage.Movements.Select(x=>x.Id)).Any(),"server pagination has stable unique source events");
            await repo.AdjustAsync(Movement("OWNER_OUT",10000));
            var later=new CashClosingRepository(factory,scope,time,new FixedClock(new DateTimeOffset(today.AddDays(1).AddHours(12),TimeSpan.Zero)));
            var negative=await later.GetAsync(today.AddDays(1));
            Check(negative.Totals.TrackingOpeningCash==-3357,"negative cash remains visible and carries forward without an invented sufficiency policy");
            var negativeClosing=await later.SaveAsync(new(){RequestId=Guid.NewGuid(),LocationId=1,Date=today.AddDays(1),Fingerprint=negative.Totals.Fingerprint,OpeningCash=-3357,CountedCash=0,Reason="Investigate negative recorded balance"});
            Check(negativeClosing.ExpectedCash==-3357&&negativeClosing.Difference==3357,"negative tracked opening can be reconciled without rewriting cash");
            await CashCurrentOpeningChecks.Run(c,config,time,today);
            await CashEntryReasonChecks.Run(c,config,time,today);
            Check(true,"isolated PostgreSQL migration and real repositories passed; full legacy migration chain not exercised");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
        using var web=new Factory();
        var httpRepository=new HttpFixture();
        using var secured=web.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>services.AddSingleton<StockFlow.Application.Interfaces.ICashInHandRepository>(httpRepository)));
        using var client=secured.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var html=await client.GetStringAsync("/CashInHand");Directory.CreateDirectory("tests/bin/cash-in-hand-fixtures");await File.WriteAllTextAsync("tests/bin/cash-in-hand-fixtures/Index.html",html);
        Check(!html.Contains("Cash &amp; Bank")&&html.Contains("Current Cash Balance"),"real Razor page renders without obsolete Cash & Bank parent");
        var navigation=html[html.IndexOf("aria-label=\"Shop tasks\"",StringComparison.Ordinal)..];
        navigation=navigation[..navigation.IndexOf("</nav>",StringComparison.Ordinal)];
        Check(navigation.Contains("href=\"/CashInHand\" class=\"sf-nav-link active\" aria-current=\"page\""),"existing cash deep link is a directly active top-level sidebar item");
        Check(navigation.Split("href=\"/CashInHand\"").Length==2,"cash navigation link appears exactly once");
        var breadcrumb=html[html.IndexOf("id=\"app-breadcrumb\"",StringComparison.Ordinal)..];
        breadcrumb=breadcrumb[..breadcrumb.IndexOf("</nav>",StringComparison.Ordinal)];
        Check(breadcrumb.Contains(">Home</a>")&&breadcrumb.Contains("Cash in Hand")&&!breadcrumb.Contains("Cash &amp; Bank"),"breadcrumb keeps Home and current cash page without redundant parent");
        Check(html.Contains("Main store")&&html.Contains("Main stock"),"cash header identifies the current authorized shop and location");
        var branches=(FakeBranchRepository)secured.Services.GetRequiredService<StockFlow.Application.Interfaces.IBranchRepository>();
        branches.Locations.Add(new(){LocationId=2,StoreId=2,StoreName="Second shop",LocationName="Second counter",IsActive=true,LocationType="SALES_FLOOR"});
        client.DefaultRequestHeaders.Add("Test-Store","2");client.DefaultRequestHeaders.Add("Test-Location","2");
        var secondShop=await client.GetStringAsync("/CashInHand");
        Check(secondShop.Contains("Second shop")&&secondShop.Contains("Second counter")&&!secondShop.Contains("Main store"),"shop context reload replaces the prior shop name rather than retaining it");
        client.DefaultRequestHeaders.Remove("Test-Store");client.DefaultRequestHeaders.Remove("Test-Location");
        Check((await client.GetAsync("/api/cashinhand")).StatusCode==HttpStatusCode.OK,"authorized owner can read cash endpoint");
        Check((await client.GetFromJsonAsync<CashInHandReport>("/api/cashinhand"))!.Movements[0].SourceUrl!=null,"source reference is linked when source view is authorized");
        client.DefaultRequestHeaders.Add("Test-Deny","EXPENSES");
        Check((await client.GetFromJsonAsync<CashInHandReport>("/api/cashinhand"))!.Movements[0].SourceUrl==null,"cash report permission does not expose unauthorized source links");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        Check((await client.PostAsJsonAsync("/api/cashinhand",new CashAdjustmentRequest())).StatusCode==HttpStatusCode.BadRequest&&httpRepository.Writes==0,"cash POST requires anti-forgery protection before posting");
        ((FakeBranchRepository)secured.Services.GetRequiredService<StockFlow.Application.Interfaces.IBranchRepository>()).State.IsOwner=false;
        client.DefaultRequestHeaders.Remove("Test-Role");client.DefaultRequestHeaders.Add("Test-Role","Sales");
        var denied=await client.GetAsync("/CashInHand");
        Check(denied.StatusCode==HttpStatusCode.Forbidden || denied.StatusCode==HttpStatusCode.Redirect && denied.Headers.Location?.OriginalString=="/Account/AccessDenied","cash page denies employee through existing permission middleware");
        Check((await client.GetAsync("/api/cashinhand")).StatusCode==HttpStatusCode.Forbidden,"employee with report view still cannot read owner-only cash endpoint");
        client.DefaultRequestHeaders.Remove("Test-Role");client.DefaultRequestHeaders.Add("Test-Role","Admin");
        ((FakeBranchRepository)secured.Services.GetRequiredService<StockFlow.Application.Interfaces.IBranchRepository>()).State.IsOwner=true;
        client.DefaultRequestHeaders.Add("Test-Deny","REPORTS");Check((await client.GetAsync("/api/cashinhand")).StatusCode==HttpStatusCode.Forbidden,"owner still requires REPORTS permission");
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }
    private sealed class HttpFixture : StockFlow.Application.Interfaces.ICashInHandRepository
    {
        public int Writes { get; private set; }
        public Task<CashInHandReport> GetAsync(DateTime date,string? search=null,int page=1,string? type=null,string? direction=null)=>Task.FromResult(new CashInHandReport{Available=true,Date=date,Movements=[new(){SourceModule="EXPENSES",SourceUrl="/Expenses/Details?id=test"}]});
        public Task<Guid> AdjustAsync(CashAdjustmentRequest request){Writes++;return Task.FromResult(request.RequestId);}
        public Task ReviewOpeningExpenseAsync(CashOpeningReviewRequest request)=>Task.CompletedTask;
    }
}
