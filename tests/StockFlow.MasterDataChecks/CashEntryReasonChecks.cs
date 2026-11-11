using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class CashEntryReasonChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS cash entry: "+label);}
    static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception e)when(e is ValidationException or PostgresException or UnauthorizedAccessException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
    public static async Task Run(NpgsqlConnection c,IConfiguration config,IOptions<ShopTimeOptions> time,DateTime today)
    {
        var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=2,LocationId=8,IsOwner=true,CanReviewStores=true,BusinessProfileCode="ELECTRICAL"};
        var factory=new DbConnectionFactory(config,scope);var repo=new CashInHandRepository(factory,scope,time,TimeProvider.System);
        await c.ExecuteAsync("INSERT INTO store_locations(tenant_id,store_id,location_id) VALUES(1,2,8)");
        CashAdjustmentRequest Entry(string kind,string? code,decimal value=10,string? note=null)=>new(){RequestId=Guid.NewGuid(),LocationId=8,Kind=kind,ReasonCode=code,Reason="Untrusted label",Amount=value,Note=note};
        await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN","OWNER_CONTRIBUTION")),"opening must exist before manual entries");
        await repo.AdjustAsync(new(){RequestId=Guid.NewGuid(),LocationId=8,Kind="OPENING",OpeningMode="CURRENT",Amount=5000});
        var add=Entry("OWNER_IN","OWNER_CONTRIBUTION",2000);var remove=Entry("OWNER_OUT","OWNER_WITHDRAWAL",1000);
        await repo.AdjustAsync(add);await repo.AdjustAsync(remove);await repo.AdjustAsync(add);await repo.AdjustAsync(remove);
        var report=await repo.GetAsync(today);
        Check(report.Balance==6000&&report.Received==2000&&report.Paid==1000&&report.Movements.Count==3,"5000 + 2000 owner contribution - 1000 withdrawal = 6000; duplicate retries post once");
        Check(await c.ExecuteScalarAsync<bool>("SELECT reason='Owner Contribution' AND request_payload->>'reasonCode'='OWNER_CONTRIBUTION' AND created_by=1 FROM cash_adjustments WHERE adjustment_id=@id",new{id=add.RequestId}),"server-derived label, stable reason code, actor and audit payload retained");
        var closing=await new CashClosingRepository(factory,scope,time,TimeProvider.System).GetAsync(today);
        Check(closing.Totals.SalesCash==0&&closing.Totals.ExpenseCash==0&&closing.Totals.SupplierCash==0&&closing.Totals.CashRefunds==0&&closing.Totals.AdjustmentCash==1000&&closing.Totals.NetCash==1000,"manual entries affect only adjustment cash, never sales/expense/supplier/refund totals");
        foreach(var amount in new[]{0m,-1m,0.001m,10000000000m})await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN","OWNER_CONTRIBUTION",amount)),"invalid positive currency amount rejected: "+amount);
        foreach(var code in new string?[]{null,"OWNER_WITHDRAWAL","CASH_BALANCE_CORRECTION","BANK_DEPOSIT","SALE","UNKNOWN"})await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN",code)),"missing, wrong-direction or unsupported reason rejected: "+code);
        await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN","OTHER_INFLOW")),"Other needs an explanation");
        await Reject(()=>repo.AdjustAsync(Entry("OWNER_OUT","OTHER_OUTFLOW",note:"!!")),"Other rejects meaningless explanations");
        await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN","OWNER_CONTRIBUTION",note:new string('x',251))),"note length enforced on server");
        await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN","OWNER_CONTRIBUTION",note:"Bad\0note")),"unsafe control characters rejected");
        var otherIn=Entry("OWNER_IN","OTHER_INFLOW",25,"  Drawer float returned  ");var otherOut=Entry("OWNER_OUT","OTHER_OUTFLOW",15,"Non-trading drawer cash removed");
        await Task.WhenAll(repo.AdjustAsync(otherIn),repo.AdjustAsync(otherOut));
        report=await repo.GetAsync(today);Check(report.Balance==6010&&report.Movements.Any(m=>m.Description.Contains("Other Cash Inflow · Drawer float returned")),"concurrent Other inflow/outflow retain signed cash and trimmed audited explanations");
        var invalid=Entry("OWNER_IN","OWNER_WITHDRAWAL");
        await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();await Reject(()=>runtime.ExecuteAsync("SELECT sf_cash_adjust(@id,@body::jsonb,@today::date)",new{id=invalid.RequestId,body=JsonSerializer.Serialize(invalid,new JsonSerializerOptions(JsonSerializerDefaults.Web)),today}),"database rejects reason bypass through direct runtime SQL");}
        scope.UserId=2;scope.CanReviewStores=false;await Reject(()=>repo.AdjustAsync(Entry("OWNER_IN","OWNER_CONTRIBUTION")),"unauthorized staff cannot create entries");scope.UserId=1;scope.CanReviewStores=true;
        var foreign=Entry("OWNER_IN","OWNER_CONTRIBUTION");foreign.LocationId=1;await Reject(()=>repo.AdjustAsync(foreign),"client cannot redirect posting to another cash location");
        Check((await repo.GetAsync(today)).Balance==6010,"failed validations and SQL bypass leave no partial cash records");
    }
}
