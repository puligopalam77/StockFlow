using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class ExpenseGstChecks
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    static async Task Reject(Func<Task> action,string name){try{await action();}catch(Exception e)when(e is PostgresException or ValidationException or UnauthorizedAccessException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    public static async Task Run(NpgsqlConnection setup,DbConnectionFactory factory,IExpenseRepository repo,HttpClient client,Guid payee,DateTime today,Guid legacy,string legacySnapshot)
    {
        Check(await setup.ExecuteScalarAsync<string>("SELECT (to_jsonb(e)-'gst_details')::text FROM expenses e WHERE expense_id=@legacy",new{legacy})==legacySnapshot&&
              await setup.ExecuteScalarAsync<bool>("SELECT gst_details IS NULL FROM expenses WHERE expense_id=@legacy",new{legacy}),"GST migration leaves historical expense amounts, status, version and tax classification unchanged");
        var stock=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");var taxDocuments=await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents");
        var gst=new GstRepository(factory);var category=await repo.MasterAsync("CATEGORY",Guid.NewGuid(),"GST reference checks");var day=today.ToString("yyyy-MM-dd");
        Check((await repo.OptionsAsync()).GetProperty("taxRules").GetArrayLength()==0,"Missing GST configuration returns an empty shared rate list without manufacturing rates");
        async Task<long> Rule(string name,decimal rate,string treatment="TAXABLE",bool review=true,decimal cess=0,bool reverse=false,string status="ACTIVE",DateTime? from=null,DateTime? to=null)
        {
            var rule=new GstTaxRule{RuleName=name,Description="Synthetic expense classification",Status=status,Tax=new(){Classification="SERVICE",HsnSac="998719",TaxRate=rate,Treatment=treatment,CessRate=cess,ReverseCharge=reverse,EffectiveFrom=from??today.AddYears(-1),EffectiveTo=to,ReviewReference="Synthetic rate review"}};
            var result=await gst.SaveRuleAsync(rule);if(review)await gst.ReviewRuleAsync(result);return result;
        }
        var normal=await Rule("Synthetic expense 18",18);var five=await Rule("Synthetic expense 5",5);var special=await Rule("Synthetic expense special",0.25m);var twelve=await Rule("Synthetic expense 12",12);
        var nil=await Rule("Synthetic expense nil",0,"NIL");var exempt=await Rule("Synthetic expense exempt",0,"EXEMPT");var zero=await Rule("Synthetic expense zero",0,"ZERO_RATED");
        var unreviewed=await Rule("Synthetic expense draft",18,review:false);var cess=await Rule("Synthetic expense cess",18,cess:1);var reverse=await Rule("Synthetic expense RCM",18,reverse:true);
        var inactive=await Rule("Synthetic expense inactive",18,status:"INACTIVE");var future=await Rule("Synthetic expense future",18,from:today.AddDays(1));var ended=await Rule("Synthetic expense ended",18,to:today.AddDays(-1));
        var catalog=await gst.TaxRulesAsync(new(){ReviewedOnly=true,CurrentOnly=true,PageSize=100});
        var shared=(await repo.OptionsAsync()).GetProperty("taxRules");
        Check(catalog.Rows.Any(r=>r.RuleId==normal)&&shared.EnumerateArray().Any(r=>r.GetProperty("id").GetInt64()==normal)&&!shared.EnumerateArray().Any(r=>r.GetProperty("id").GetInt64()==inactive),"Expense options reuse the exact existing GST Settings rule identity and exclude inactive rates");
        object Tax(long rule,string? gstin=null,string? invoice=null,string status="NOT_ASSESSED")=>new{ruleId=rule,supplierGstin=gstin,invoiceNumber=invoice,itcStatus=status};
        JsonElement Element(object value)=>JsonSerializer.SerializeToElement(value);
        object Details(decimal total,object? tax)=>new{categoryId=category,payeeId=payee,incurredDate=day,amount=total,description="GST reference expense",gst=tax};
        foreach(var (rule,total,expectedBase,expectedTax) in new[]{(normal,1180m,1000m,180m),(twelve,3000m,2678.57m,321.43m),(five,105m,100m,5m),(special,100.25m,100m,0.25m),(nil,100m,100m,0m),(exempt,100m,100m,0m),(zero,100m,100m,0m),(normal,0.03m,0.03m,0m)})
        {
            var quote=await repo.TaxPreviewAsync(new(total,today,Element(Tax(rule))));
            var engine=GstRules.InclusiveAmounts(total,quote.GetProperty("rate").GetDecimal());
            Check(quote.GetProperty("total").GetDecimal()==total&&quote.GetProperty("taxableAmount").GetDecimal()==expectedBase&&quote.GetProperty("gstAmount").GetDecimal()==expectedTax&&engine.TaxableAmount==expectedBase&&engine.GstAmount==expectedTax,
                "Configured inclusive expense rate reconciles with shared GST arithmetic: "+rule+" / "+total);
            Check(quote.GetProperty("cgst").ValueKind==JsonValueKind.Null&&quote.GetProperty("igst").ValueKind==JsonValueKind.Null&&quote.GetProperty("reviewStatus").GetString()=="NEEDS_REVIEW"&&!quote.GetProperty("itcClaimed").GetBoolean(),"Missing place of supply never guesses a tax split or claims ITC");
        }
        var salesQuote=SaleRules.CalculateLines(new CreateSaleRequest{PricesIncludeTax=true,Items=[new(){ProductId=1,Quantity=1,SellingPrice=1180}]},[18m],false);
        Check(salesQuote.TotalAmount==1180m&&salesQuote.Subtotal==1000m&&salesQuote.TaxAmount==180m,"Shared inclusive arithmetic preserves existing billing results");
        foreach(var rule in new[]{unreviewed,cess,reverse,inactive,future,ended})await Reject(()=>repo.TaxPreviewAsync(new(1180m,today,Element(Tax(rule)))),"Reject unreviewed, unsupported, inactive or out-of-date expense rule: "+rule);
        await Reject(()=>repo.TaxPreviewAsync(new(1180m,today.AddYears(-2),Element(Tax(normal)))),"Reject a rate outside its configured effective dates");
        await Reject(()=>repo.TaxPreviewAsync(new(1180m,today,Element(Tax(normal,"123456789012345")))),"Optional supplier GSTIN still requires a valid checksum when provided");
        await Reject(()=>repo.TaxPreviewAsync(new(1180m,today,Element(Tax(normal,status:"ELIGIBLE_VERIFIED")))),"Even an owner cannot claim verified expense ITC without a supported verification workflow");
        var preview=await client.PostAsJsonAsync("/api/expenses/tax-preview",new ExpenseTaxPreview(1180m,today,Element(Tax(normal))));
        Check(preview.IsSuccessStatusCode&&(await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gstAmount").GetDecimal()==180m,"Authenticated GST preview API uses reviewed shop rates and decimal calculations");
        var invalidPreview=await client.PostAsJsonAsync("/api/expenses/tax-preview",new ExpenseTaxPreview(1180m,today,Element(Tax(normal,"BAD"))));
        Check((int)invalidPreview.StatusCode==400&&(await invalidPreview.Content.ReadAsStringAsync()).Contains("valid supplier GSTIN"),"GST preview gives a clear validation message for an invalid optional GSTIN");
        var noGst=Guid.NewGuid();await repo.RecordAsync(noGst,new(Guid.NewGuid(),0,Element(new{expense=Details(1180m,null),payment=(object?)null})));
        Check((await repo.GetAsync(noGst)).GetProperty("gst_details").ValueKind==JsonValueKind.Null,"No GST records the entered total with no tax snapshot or payment");
        var draft=Guid.NewGuid();await ExpenseRecordingChecks.SeedLegacyAsync(setup,draft,new(Guid.NewGuid(),0,Element(Details(1180m,Tax(normal)))));var savedDraft=await repo.GetAsync(draft);
        Check(savedDraft.GetProperty("status").GetString()=="DRAFT"&&savedDraft.GetProperty("paid").GetDecimal()==0&&savedDraft.GetProperty("payments").GetArrayLength()==0&&savedDraft.GetProperty("gst_details").GetProperty("gstAmount").GetDecimal()==180m,"Saving GST information in a draft creates no payment or claim");
        var overpaid=Guid.NewGuid();await Reject(()=>repo.RecordAsync(overpaid,new(Guid.NewGuid(),0,Element(new{expense=Details(1180m,Tax(normal)),payment=new{amount=1180.01m,paymentMode="UPI",paidDate=day,confirmed=true}}))),"GST-inclusive initial overpayment is rejected against the unchanged total");
        Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM expenses WHERE expense_id=@overpaid",new{overpaid})==0,"Failed GST payment rolls back expense, GST snapshot, posting and payment atomically");
        var forged=await repo.TaxPreviewAsync(new(1180m,today,Element(new{ruleId=normal,rate=0,taxableAmount=1180m,gstAmount=0m,cgst=180m,itcStatus="NOT_ASSESSED",itcClaimed=true})));
        Check(forged.GetProperty("gstAmount").GetDecimal()==180m&&!forged.GetProperty("itcClaimed").GetBoolean()&&forged.GetProperty("cgst").ValueKind==JsonValueKind.Null,"Client-supplied tax amounts, splits and claim flags cannot override the authoritative calculation");
        var taxed=Guid.NewGuid();var operation=new ExpenseOperation(Guid.NewGuid(),0,Element(new{expense=Details(1180m,Tax(normal,invoice:" SYNTHETIC-01 ")),payment=new{amount=500m,paymentMode="BANK_TRANSFER",paidDate=day,confirmed=true}}));
        await repo.RecordAsync(taxed,operation);await repo.RecordAsync(taxed,operation);var expense=await repo.GetAsync(taxed);var savedTax=expense.GetProperty("gst_details");
        Check(expense.GetProperty("amount").GetDecimal()==1180m&&expense.GetProperty("paid").GetDecimal()==500m&&expense.GetProperty("payments").GetArrayLength()==1&&savedTax.GetProperty("gstAmount").GetDecimal()==180m&&savedTax.GetProperty("invoiceNumber").GetString()=="SYNTHETIC-01","Inclusive GST recording keeps payment totals unchanged and exact retries create one settlement");
        await repo.WriteAsync(taxed,"PAY",new(Guid.NewGuid(),expense.GetProperty("version").GetInt64(),Element(new{amount=680m,paymentMode="BANK_TRANSFER",paidDate=day})));
        expense=await repo.GetAsync(taxed);Check(expense.GetProperty("paid").GetDecimal()==1180m&&expense.GetProperty("gst_details").GetRawText()==savedTax.GetRawText(),"Remaining payments settle the inclusive total without recalculating historical tax facts");
        await repo.WriteAsync(taxed,"CORRECT",new(Guid.NewGuid(),expense.GetProperty("version").GetInt64(),Element(new{categoryId=category,payeeId=payee,incurredDate=day,amount=1180m,description="Legacy client text correction",reason="Synthetic text-only correction"})));
        expense=await repo.GetAsync(taxed);Check(expense.GetProperty("gst_details").GetRawText()==savedTax.GetRawText(),"Older clients omitting GST preserve existing reference data on text corrections");
        await Reject(()=>repo.WriteAsync(taxed,"CORRECT",new(Guid.NewGuid(),expense.GetProperty("version").GetInt64(),Element(new{categoryId=category,payeeId=payee,incurredDate=day,amount=1200m,description="No GST context",reason="Synthetic amount change"}))),"Older clients cannot silently change a GST expense total without its tax context");
        var events=expense.GetProperty("events");Check(events[0].GetProperty("snapshot").GetProperty("gst_details").GetProperty("gstAmount").GetDecimal()==180m,"Expense audit events retain the original GST snapshot");
        var options=(await repo.OptionsAsync()).GetProperty("taxRules");Check(options.EnumerateArray().Any(r=>r.GetProperty("id").GetInt64()==normal)&&!options.EnumerateArray().Any(r=>r.GetProperty("id").GetInt64()==unreviewed)&&!options.EnumerateArray().Single(r=>r.GetProperty("id").GetInt64()==cess).GetProperty("supported").GetBoolean(),"Expense tax options expose reviewed configured rules and flag unsupported calculations");
        Check((await client.GetStringAsync("/Expenses/Create")).Contains("id=\"expense-gst-settings\""),"GST Settings recovery link is rendered for an authorized user");
        const string gstPermissions="UPDATE user_action_permissions SET is_allowed=@allowed WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='GST_SETTINGS' AND a.action_code";
        const string gstView=gstPermissions+"='VIEW')",gstActions=gstPermissions+"<>'VIEW')";
        async Task RefreshVersion(){client.DefaultRequestHeaders.Remove("Test-Version");client.DefaultRequestHeaders.Add("Test-Version",(await setup.ExecuteScalarAsync<long>("SELECT security_version FROM users WHERE user_id=1")).ToString());}
        try
        {
            await setup.ExecuteAsync(gstActions,new{allowed=false});await setup.ExecuteAsync(gstView,new{allowed=false});await RefreshVersion();
            Check(!(await client.GetStringAsync("/Expenses/Create")).Contains("id=\"expense-gst-settings\""),"Expense entry does not expose a GST Settings link without configuration-view permission");
            var staffOptions=await client.GetFromJsonAsync<JsonElement>("/api/expenses/options");
            Check(staffOptions.GetProperty("taxRules").EnumerateArray().Any(r=>r.GetProperty("id").GetInt64()==normal),"Expense users can use applicable shared rates without receiving GST configuration privileges");
        }
        finally{await setup.ExecuteAsync(gstView,new{allowed=true});await setup.ExecuteAsync(gstActions,new{allowed=true});await RefreshVersion();}
        Check(await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==stock&&await setup.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents")==taxDocuments,"GST expense references create neither stock movements nor tax/ITC claim documents");
    }
}
