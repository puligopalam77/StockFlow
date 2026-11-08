using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class ExpenseReportingChecks
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    public static async Task Run(NpgsqlConnection setup,IConfiguration config,IExpenseRepository repo,HttpClient client,Guid payee,DateTime today)
    {
        var from=new DateTime(today.Year,today.Month,1);var oldDay=from.AddDays(-1);var cutoff=today.AddDays(-1);
        var baseline=await repo.ReportAsync(from,today);var earlier=await repo.ReportAsync(today.AddMonths(-2),cutoff);
        var cat=await repo.MasterAsync("CATEGORY",Guid.NewGuid(),"Report reconciliation category");
        var rule=await setup.ExecuteScalarAsync<long>("SELECT rule_id FROM gst_tax_rules WHERE rule_name='Synthetic expense 18' AND store_id=1");
        JsonElement Element(object value)=>JsonSerializer.SerializeToElement(value);
        object Details(decimal amount,DateTime day,bool tax=false)=>new{categoryId=cat,payeeId=payee,incurredDate=day.ToString("yyyy-MM-dd"),amount,description="Report lifecycle fixture",gst=tax?(object)new{ruleId=rule,itcStatus="NOT_ASSESSED"}:null};
        async Task<Guid> Record(decimal amount,DateTime day,decimal? paid=null,bool tax=false)
        {
            var id=Guid.NewGuid();await repo.RecordAsync(id,new(Guid.NewGuid(),0,Element(new{expense=Details(amount,day,tax),payment=paid is {} value?(object)new{amount=value,paymentMode="BANK_TRANSFER",paidDate=today.ToString("yyyy-MM-dd"),confirmed=true}:null})));return id;
        }
        async Task Write(Guid id,string action,object body){var current=await repo.GetAsync(id);await repo.WriteAsync(id,action,new(Guid.NewGuid(),current.GetProperty("version").GetInt64(),Element(body)));}
        var older=await Record(100,oldDay,10);var inclusive=await Record(1180,today,500,true);
        await Write(inclusive,"PAY",new{amount=100,paymentMode="UPI",paidDate=today.ToString("yyyy-MM-dd")});
        await Write(inclusive,"PAY",new{amount=80,paymentMode="BANK_TRANSFER",paidDate=today.ToString("yyyy-MM-dd")});
        var other=await Record(300,today,100);await Write(other,"REFUND",new{amount=25,paymentMode="CASH",paidDate=today.ToString("yyyy-MM-dd"),reason="Synthetic refund"});
        var draft=Guid.NewGuid();await ExpenseRecordingChecks.SeedLegacyAsync(setup,draft,new(Guid.NewGuid(),0,Element(Details(700,today))));
        var cancelled=await Record(90,today);await Write(cancelled,"CANCEL",new{reason="Synthetic cancelled expense"});
        var report=await repo.ReportAsync(from,today);var monthly=report.GetProperty("monthly").EnumerateArray().Single(m=>m.GetProperty("month").GetString()==today.ToString("yyyy-MM"));
        Check(report.GetProperty("incurred").GetDecimal()-baseline.GetProperty("incurred").GetDecimal()==1480m&&monthly.GetProperty("total").GetDecimal()==report.GetProperty("incurred").GetDecimal(),"Monthly totals count each recorded inclusive expense once and exclude drafts/cancellations");
        var grouped=report.GetProperty("categories").EnumerateArray().Single(g=>g.GetProperty("category_id").GetGuid()==cat);
        Check(grouped.GetProperty("total").GetDecimal()==1480m&&grouped.GetProperty("count").GetInt32()==2,"Category report includes gross GST-inclusive costs without double-counting partial payments");
        Check(report.GetProperty("paid").GetDecimal()-baseline.GetProperty("paid").GetDecimal()==765m,"Payments in period remain distinct from expense totals and include signed refunds");
        var due=report.GetProperty("outstanding").EnumerateArray().Where(e=>e.GetProperty("category_id").GetGuid()==cat).ToArray();
        Check(due.Length==3&&due.Sum(e=>e.GetProperty("amount").GetDecimal()-e.GetProperty("paid").GetDecimal())==815m&&report.GetProperty("outstandingAtCutoff").GetDecimal()-baseline.GetProperty("outstandingAtCutoff").GetDecimal()==815m,"Outstanding report reconciles net recorded payments and includes older unpaid expenses");
        Check(due.Any(e=>e.GetProperty("expense_id").GetGuid()==older)&&!due.Any(e=>e.GetProperty("expense_id").GetGuid()==draft||e.GetProperty("expense_id").GetGuid()==cancelled),"Outstanding rows include earlier expense dates and exclude drafts/cancelled records");
        var historical=await repo.ReportAsync(today.AddMonths(-2),cutoff);
        Check(historical.GetProperty("outstandingAtCutoff").GetDecimal()-earlier.GetProperty("outstandingAtCutoff").GetDecimal()==100m,"Cutoff balances exclude later payments rather than using all-time paid amounts");
        var multi=await repo.ReportAsync(from.AddMonths(-1),today);
        Check(multi.GetProperty("monthly").EnumerateArray().Any(m=>m.GetProperty("month").GetString()==oldDay.ToString("yyyy-MM"))&&multi.GetProperty("monthly").EnumerateArray().Sum(m=>m.GetProperty("total").GetDecimal())==multi.GetProperty("incurred").GetDecimal(),"Multi-month expense buckets reconcile to the recorded period total");
        var foreign=await setup.QuerySingleAsync<(long Store,long Location)>("SELECT store_id,location_id FROM store_locations WHERE store_id<>1 AND is_active AND location_type='SALES_FLOOR' ORDER BY store_id LIMIT 1");
        var foreignScope=new BusinessScope{TenantId=1,UserId=1,StoreId=foreign.Store,LocationId=foreign.Location,IsOwner=true,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};
        var foreignRepo=new ExpenseRepository(new DbConnectionFactory(config,foreignScope));
        Check(!(await foreignRepo.OptionsAsync()).GetProperty("taxRules").EnumerateArray().Any(r=>r.GetProperty("id").GetInt64()==rule),"Expense GST choices do not expose another store's configured rules");
        try{await foreignRepo.TaxPreviewAsync(new(1180m,today,Element(new{ruleId=rule})));throw new Exception("Foreign GST rate accepted");}catch(ValidationException){Check(true,"A forged foreign-store GST rule is rejected by the calculation backend");}
        var foreignReport=await foreignRepo.ReportAsync(from,today);Check(!foreignReport.GetProperty("categories").EnumerateArray().Any(g=>g.GetProperty("category_id").GetGuid()==cat)&&!foreignReport.GetProperty("outstanding").EnumerateArray().Any(e=>e.GetProperty("expense_id").GetGuid()==inclusive),"Monthly/category/outstanding reports retain store-level isolation");
        var http=await client.GetFromJsonAsync<JsonElement>($"/api/expenses/report?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Check(http.GetProperty("categories").EnumerateArray().Single(g=>g.GetProperty("category_id").GetGuid()==cat).GetProperty("total").GetDecimal()==1480m,"Existing authenticated report endpoint serves the new grouped reports");
        Directory.CreateDirectory("tests/bin/expense-modern-fixtures");await File.WriteAllTextAsync("tests/bin/expense-modern-fixtures/report.json",http.GetRawText());
    }
}
