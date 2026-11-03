using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Help;

namespace StockFlow.MasterDataChecks;

internal static class HelpBusinessChecks
{
    public static async Task Run(Factory factory)
    {
        var directory=Path.Combine(Path.GetTempPath(),"StockFlowHelpChecks",Guid.NewGuid().ToString("N"));
        var fake=new Values();
        Check(BusinessQuestions.NormalizeNamedMetric(new("stock","current",Term:"Battery"),"How many Battery units are available?").Metric=="product-stock", "Named filters are retained when a model selects a generic metric");
        Check(BusinessQuestions.NormalizeNamedMetric(new("stock","current",Term:"shop"),"What is shop stock?").ValidPeriod() is null,"Generic shop words cannot become product filters or unfiltered answers");
        using var app=factory.WithWebHostBuilder(builder=>{
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["HelpMonitoring:Directory"]=directory,["HelpAi:StartLocalRuntime"]="false"}));
            builder.ConfigureServices(services=>{
                services.RemoveAll<IHelpBusinessRepository>(); services.AddSingleton<IHelpBusinessRepository>(fake);
                services.RemoveAll<ISalesEnquiryRepository>(); services.AddSingleton<ISalesEnquiryRepository,Sales>();
                services.PostConfigure<HelpAiOptions>(options=>options.Enabled=false);
            });
        });
        using var client=app.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role","Admin");
        async Task<HttpResponseMessage> Ask(int user,string question)
        {
            client.DefaultRequestHeaders.Remove("Test-UserId");client.DefaultRequestHeaders.Add("Test-UserId",user.ToString());
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var token=await client.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token.GetProperty("requestToken").GetString());
            return await client.PostAsJsonAsync("/api/help/questions",new {question});
        }
        async Task<HelpAnswer> Answer(int user,string question)
        {
            var response=await Ask(user,question);response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<HelpAnswer>())!;
        }
        var cases=new[] {
            ("Which purchase orders are still waiting for stock?","pending-purchases","current",(string?)null),
            ("Which products need purchasing, based on minimum stock?","reorder","current",(string?)null),
            ("Which products sold most this month?","top-products","this-month",(string?)null),
            ("Slow-moving products last week","slow-products","last-week",(string?)null),
            ("Which customers have overdue dues?","overdue-customers","current",(string?)null),
            ("Which repairs are overdue or ready for collection?","repair-deliveries","current",(string?)null),
            ("Which repairs are overdue?","overdue-repairs","current",(string?)null),
            ("Which repairs are ready for collection?","ready-repair-list","current",(string?)null),
            ("When does warranty expire for serial number SN-123?","warranty-detail","current","SN-123"),
            ("Which transfers are pending receipt?","pending-transfers","current",(string?)null),
            ("Compare this week\u2019s sales with last week.","sales-comparison","this-week",(string?)null),
            ("Compare this month's sales with last month.","sales-comparison","this-month",(string?)null),
            ("How much quantity sold for brand Samsung yesterday?","product-quantity","yesterday","Samsung"),
            ("How much sales for product Cable today?","product-amount","today","Cable"),
            ("How many invoices for product Cable last week?","product-count","last-week","Cable"),
            ("Customer dues","customer-dues","current",(string?)null),
            ("Customer dues for Ranga","customer-dues","current","Ranga"),
            ("Supplier balance for Supplier One","supplier-balance","current","Supplier One"),
            ("How many returns today?","return-count","today",(string?)null),
            ("How many items returned yesterday?","return-quantity","yesterday",(string?)null),
            ("Return amount last week","return-amount","last-week",(string?)null),
            ("How much money refunded this month?","refunded","this-month",(string?)null),
            ("Payment breakdown today","payment-breakdown","today",(string?)null),
            ("How much cash collected yesterday?","cash-collected","yesterday",(string?)null),
            ("How much UPI collected today?","upi-collected","today",(string?)null)
        };
        for(var i=0;i<cases.Length;i++)
        {
            var item=cases[i];var answer=await Answer(6000+i,item.Item1);
            Check(answer.Status=="answered" && answer.Message=="42" && answer.Sections.Length==0 && answer.Links.Length==0 &&
                fake.Last?.Metric==item.Item2 && fake.Last.Period.Period==item.Item3 && fake.Last.Term==item.Item4,"Approved business query: "+item.Item1);
        }
        var missingSerial=await Answer(6050,"When does warranty expire for this serial number?");
        Check(missingSerial.Status=="clarify" && missingSerial.Message.Contains("serial number"),"Missing serial is clarified without a warranty query");
        var missingRanking=await Answer(6051,"Which products sold most?");
        Check(missingRanking.Status=="clarify","Ranking requires a stated period");
        foreach(var (question,module) in new[]{("Pending purchase deliveries","PURCHASES"),("Reorder suggestions","INVENTORY"),("Which products sold most this month?","PRODUCTS"),("Overdue customer payments","CUSTOMERS"),("Overdue repairs","SERVICE"),("Warranty expiry for serial SN123","WARRANTY_LOOKUP"),("Pending stock transfers","STOCK_TRANSFER")})
        {
            client.DefaultRequestHeaders.Add("Test-Deny",module);var before=fake.Calls;
            Check((await Ask(6060+Array.IndexOf(new[]{"PURCHASES","INVENTORY","PRODUCTS","CUSTOMERS","SERVICE","WARRANTY_LOOKUP","STOCK_TRANSFER"},module),question)).StatusCode==HttpStatusCode.Forbidden && before==fake.Calls,"Operational permission enforced: "+module);
            client.DefaultRequestHeaders.Remove("Test-Deny");
        }
        Check(new SalesQuestion("reorder","yesterday").ValidPeriod() is null,"Current reorder cannot accept historical dates");
        Check(new SalesQuestion("sales-comparison","range","2026-10-01","2026-10-07").ValidPeriod() is null,"Unsupported comparisons are rejected");
        Check(new SalesQuestion("top-products","today",Term:"Samsung").ValidPeriod() is null,"Ranking does not discard unsupported named filters");
        Check(!BusinessQuestions.MatchesFilters(new("customer-dues","current"),"Which customers have overdue dues?"),"Overdue qualifier cannot be dropped");
        Check(!BusinessQuestions.MatchesFilters(new("overdue-customers","current"),"How much do customers still owe?"),"Ordinary dues cannot be narrowed to overdue balances");
        await Answer(6052,"When does warranty expire?");await Task.Delay(3100);
        var serialFollowUp=await Answer(6052,"SN123");
        Check(serialFollowUp.Status=="answered" && fake.Last?.Metric=="warranty-detail" && fake.Last.Term=="SN123","Serial clarification accepts the next answer in the assistant");
        await Answer(6100,"Quantity sold yesterday");await Task.Delay(3100);
        await Answer(6100,"And last week?");await Task.Delay(3100);
        await Answer(6100,"Only Samsung?");
        Check(fake.Last?.Metric=="product-quantity" && fake.Last.Term=="Samsung" && fake.Last.Period.Period=="last-week","Date then brand follow-up retains metric and period");
        await Task.Delay(3100);await Answer(6100,"And yesterday?");
        Check(fake.Last?.Term=="Samsung" && fake.Last.Period.Period=="yesterday","Date follow-up retains product filter");
        var calls=fake.Calls;
        var missing=await Answer(6101,"How much sales?");
        Check(missing.Status=="clarify" && !missing.Message.Contains("Admin") && fake.Calls==calls,"Missing date is clarified inside chat");
        await Task.Delay(3100);var dated=await Answer(6101,"Yesterday");
        Check(dated.Status=="answered" && dated.Message.Contains("100.00"),"Answer to date clarification executes the pending metric");
        var unknown=await Answer(6102,"Only Samsung");
        Check(unknown.Status!="answered","Product follow-up without context cannot guess a period");
        calls=fake.Calls;var outside=await Answer(6103,"What is the weather today?");
        Check(outside.Status=="unsupported" && fake.Calls==calls,"Known unrelated questions never query shop records");
        client.DefaultRequestHeaders.Add("Test-Deny","SALES");calls=fake.Calls;
        Check((await Ask(6110,"Return amount today")).StatusCode==HttpStatusCode.Forbidden && fake.Calls==calls,"Return view permission checked before records");
        client.DefaultRequestHeaders.Remove("Test-Deny");client.DefaultRequestHeaders.Add("Test-Deny","SUPPLIERS");
        Check((await Ask(6111,"Supplier balance")).StatusCode==HttpStatusCode.Forbidden && fake.Calls==calls,"Supplier balances require supplier and purchase access");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        var deniedFilter=await Answer(6112,"How much cash collected today for employee Bob?");
        Check(deniedFilter.Status=="clarify" && fake.Calls==calls,"Unsupported filters never become unfiltered totals");
        fake.Fail=true;var failure=await Ask(6120,"Payment breakdown today");
        var failed=(await failure.Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(failure.StatusCode==HttpStatusCode.ServiceUnavailable && failed.Status=="unavailable" && failed.Message!="0","Timeout is a retryable failure, never zero");
        fake.Fail=false;fake.Value="0";var zero=await Answer(6121,"How many returns today?");
        Check(zero.Status=="answered" && zero.Message=="0","Confirmed zero is answered normally");
        fake.Value="42";var receipt=await Answer(6130,"Payment breakdown today");
        var feedback=await client.PostAsJsonAsync("/api/help/questions/feedback",new {receipt.AnswerId,helpful=false});
        Check(feedback.IsSuccessStatusCode,"Authenticated feedback is recorded for the issued answer");
        Check((await client.PostAsJsonAsync("/api/help/questions/feedback",new {receipt.AnswerId,helpful=true})).StatusCode==HttpStatusCode.NotFound,"Duplicate feedback cannot distort monitoring");
        var other=await Answer(6131,"Payment breakdown today");
        Check((await client.PostAsJsonAsync("/api/help/questions/feedback",new {receipt.AnswerId,helpful=true})).StatusCode==HttpStatusCode.NotFound,"Another user cannot vote on the answer");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Check((await client.PostAsJsonAsync("/api/help/questions/feedback",new {other.AnswerId,helpful=true})).StatusCode==HttpStatusCode.BadRequest,"Feedback requires CSRF");
        var logs=string.Join("",Directory.GetFiles(directory,"*.jsonl").Select(File.ReadAllText));
        Check(logs.Contains("milliseconds") && logs.Contains("feedback") && !logs.Contains("Ranga") && !logs.Contains("Samsung") && !logs.Contains("question"),"Monitoring records timing/status/feedback without questions or account names");
        var india=ShopDateRange.Resolve(new("today"),new ShopTimeOptions(),new Clock(new(2026,10,7,18,31,0,TimeSpan.Zero)));
        Check(india.From==new DateTime(2026,10,8) && india.StartUtc==new DateTime(2026,10,7,18,30,0,DateTimeKind.Utc),"Midnight uses shop timezone rather than UTC server date");
        var dst=ShopDateRange.Resolve(new("range",new(2026,3,8),new(2026,3,8)),new ShopTimeOptions{TimeZoneId="America/New_York"},TimeProvider.System);
        Check((dst.EndUtc-dst.StartUtc).TotalHours==23,"DST boundaries use consecutive local midnights");
        async Task<HttpResponseMessage> Staff(int user)
        {
            using var staff=app.CreateClient();staff.DefaultRequestHeaders.Add("Test-Role","Admin");staff.DefaultRequestHeaders.Add("Test-UserId",user.ToString());
            var token=await staff.GetFromJsonAsync<JsonElement>("/api/antiforgery");staff.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token.GetProperty("requestToken").GetString());
            return await staff.PostAsJsonAsync("/api/help/questions",new {question="Payment breakdown today"});
        }
        var simultaneous=await Task.WhenAll(Staff(6200),Staff(6201),Staff(6202));
        Check(simultaneous.All(response=>response.IsSuccessStatusCode),"Simultaneous staff enquiries complete independently");
        var gate=new HelpModelGate();using var first=await gate.Enter(CancellationToken.None);
        var busy=false;try {using var second=await gate.Enter(CancellationToken.None);}catch(HttpRequestException){busy=true;}
        Check(busy,"Simultaneous model work is bounded and excess waiting fails promptly");
        Console.WriteLine("Scoped API tests use controlled records; real shop totals still need staff reconciliation.");
    }
    private static void Check(bool passed,string message) {if(!passed)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    private class Values : IHelpBusinessRepository
    {
        public BusinessValueQuery? Last;public int Calls;public bool Fail;public string Value="42";
        public Task<BusinessValueResult> GetAsync(BusinessValueQuery query,CancellationToken cancellation) {Interlocked.Increment(ref Calls);Last=query;if(Fail)throw new TimeoutException();return Task.FromResult(new BusinessValueResult("answered",Value));}
    }
    private class Sales : ISalesEnquiryRepository
    {
        public Task<TodaySoldQuantity> GetTodaySoldQuantityAsync()=>Task.FromResult(new TodaySoldQuantity(DateTime.Today,7,2));
        public Task<SalesPeriodSummary> GetSummaryAsync(SalesPeriod period,CancellationToken cancellation)=>Task.FromResult(new SalesPeriodSummary(DateTime.Today,DateTime.Today,7,2,100,80));
    }
    private class Clock(DateTimeOffset instant) : TimeProvider {public override DateTimeOffset GetUtcNow()=>instant;}
}
