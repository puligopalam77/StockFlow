using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Help;

namespace StockFlow.MasterDataChecks;

internal static class HelpSalesChecks
{
    public static async Task Run(Factory factory)
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<ISalesEnquiryRepository>();
            services.AddScoped<ISalesEnquiryRepository, ScopedSales>();
            services.PostConfigure<HelpAiOptions>(options => options.Enabled = false);
        }));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        async Task<HttpResponseMessage> Ask(int id, string question)
        {
            client.DefaultRequestHeaders.Remove("Test-UserId");
            client.DefaultRequestHeaders.Add("Test-UserId", id.ToString());
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
            return await client.PostAsJsonAsync("/api/help/questions", new { question });
        }
        ScopedSales.Calls = 0;
        var plainSales=new[]{("WHAT IS THE LAST WEEK SALES?","last-week"),("Show this week sales","this-week"),
            ("Yesterday sales?","yesterday"),("What are last month sales?","last-month"),("Sales today","today")};
        for(var index=0;index<plainSales.Length;index++)
        {
            var item=plainSales[index];
            var direct=(await (await Ask(3050+index,item.Item1)).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(direct.Status=="answered" && direct.Sections.Length==0 && direct.Links.Length==0
                && ScopedSales.LastPeriod?.Period==item.Item2 && SalesQuestion.Parse(item.Item1)?.Metric=="amount",
                "Plain sales question returns scoped revenue directly: "+item.Item1);
        }
        ScopedSales.Calls = 0;
        var response = await Ask(3100,"HOW MUCH QUANTITY SOLD TODAY?");
        response.EnsureSuccessStatusCode();
        var answer = (await response.Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Status == "answered" && answer.Message == "7", "Today's sold quantity returns only the verified value");
        Check(answer.Sections.Length==0 && answer.Links.Length==0, "Numeric answers have no instructions or extra links");
        Check(response.Headers.CacheControl?.NoStore == true, "Live sales answer is not cached");
        answer=(await (await Ask(4400,"how much quantity sold tofay")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Message=="7" && answer.Sections.Length==0 && answer.Links.Length==0,"Screenshot typo still returns today's value directly");
        client.DefaultRequestHeaders.Add("Test-Tenant","2");
        answer = (await (await Ask(3101,"How many units were sold today")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Message=="99","Authenticated business scope is passed to the repository");
        client.DefaultRequestHeaders.Remove("Test-Tenant");
        client.DefaultRequestHeaders.Add("Test-Deny","SALES");
        var calls = ScopedSales.Calls;
        Check((int)(await Ask(3102,"quantity sold today")).StatusCode == 403 && ScopedSales.Calls == calls,"Sales permission denied before querying records");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        client.DefaultRequestHeaders.Add("Test-Location","99");
        Check((int)(await client.PostAsJsonAsync("/api/help/questions",new {question="quantity sold today"})).StatusCode == 403 && ScopedSales.Calls == calls,"Unauthorized location denied before querying records");
        client.DefaultRequestHeaders.Remove("Test-Location");
        answer = (await (await Ask(3104,"How much quantity sold yesterday?")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Status == "answered" && answer.Message == "4" && ScopedSales.LastPeriod?.Period == "yesterday", "Yesterday returns its own quantity without directions");
        answer=(await (await Ask(4100,"how much amout collected day before yesterday")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Message=="₹80.00" && ScopedSales.LastPeriod?.Period=="day-before-yesterday" && answer.Links.Length==0,"Collected amount uses payments rather than sales revenue, with common typo and two-days-ago date");
        await Task.Delay(3100);
        answer=(await (await Ask(4100,"yesterday")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Message=="₹80.00" && ScopedSales.LastPeriod?.Period=="yesterday","Date-only follow-up reuses the previous collected-money metric");
        answer=(await (await Ask(4101,"day before yesterday")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Status=="clarify" && !answer.Message.Contains("Admin") && answer.Links.Length==0,"Date without a previous topic asks for the metric instead of administrator fallback");
        answer=(await (await Ask(4102,"how many units sold two days ago")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Status=="answered" && ScopedSales.LastPeriod?.Period=="day-before-yesterday","Two days ago is a supported relative period");
        var datedQuestions = new[] {
            ("How many invoices completed last week?", "last-week", "2"),
            ("How much quantity sold this month?", "this-month", "7"),
            ("How much sales last month?", "last-month", "₹100.00"),
            ("How much quantity sold from 2026-10-01 to 2026-10-07?", "range", "7"),
            ("How many items sold on 2026-10-07?", "range", "7")
        };
        for (var index=0; index<datedQuestions.Length; index++) {
            var item=datedQuestions[index];
            answer=(await (await Ask(3800+index,item.Item1)).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status=="answered" && answer.Message==item.Item3 && ScopedSales.LastPeriod?.Period==item.Item2 && answer.Links.Length==0 && answer.Sections.Length==0,"Direct dated enquiry: "+item.Item1);
        }
        foreach (var question in new[] {"How much quantity sold for product ABC yesterday?", "How much sales from 2026-10-09 to 2026-10-01?", "How many units sold today and yesterday?", "How much sales yesterday in Mumbai?"}) {
            calls=ScopedSales.Calls;
            answer=(await (await Ask(3900+Array.IndexOf(new[] {"How much quantity sold for product ABC yesterday?", "How much sales from 2026-10-09 to 2026-10-01?", "How many units sold today and yesterday?", "How much sales yesterday in Mumbai?"},question),question)).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status=="clarify" && answer.Links.Length==0 && ScopedSales.Calls==calls,"Ambiguous or unsupported filters cannot silently query a whole-business total: "+question);
        }
        var guideQuestions = new[] {"How do I add a product?", "How do I receive stock?", "How do I make a bill?", "How do I refund a customer?", "How do I check warranty?", "How do I create a repair request?", "How do I use reports?", "How do I transfer stock?", "How do I close cash?", "How do I change staff accounts?", "How do I manage roles?", "How do I create a store?", "How do I change language?", "How do I change password?", "How do I change company details?", "How do I check inventory?"};
        for (var index=0; index<guideQuestions.Length; index++) {
            answer = (await (await Ask(3200+index,guideQuestions[index])).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status=="answered" && answer.Sections.Length>0,"Local guide without AI: "+guideQuestions[index]);
        }
        answer = (await (await Ask(3300,"What is the weather?")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Status=="unsupported" && answer.Message.Contains("Contact Super Admin"),"Outside question directs to Super Admin");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        var cases = JsonSerializer.Deserialize<HelpAiChecks.Case[]>(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"tests/StockFlow.MasterDataChecks/help-ai-questions.json")),new JsonSerializerOptions {PropertyNameCaseInsensitive=true})!;
        for (var index=0; index<cases.Length; index++) {
            if (cases[index].Article is null) continue;
            answer = (await (await Ask(3500+index,cases[index].Question)).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status=="answered" && (cases[index].Article=="business" || answer.Sections.Any(section => section.ArticleId==cases[index].Article)),"Local question source: "+cases[index].Question);
        }
        client.DefaultRequestHeaders.Add("Test-Deny", "ROLE_MANAGEMENT");
        answer = (await (await Ask(3301,"How do I manage roles?")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Status=="restricted" && answer.Sections.Length==0,"Denied role instructions remain restricted");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        foreach (var question in new[] {"how much sales today", "this month sales amount", "how much stock is available", "how many pending repairs", "how many low stock products"}) {
            answer = (await (await Ask(3400+Array.IndexOf(new[] {"how much sales today", "this month sales amount", "how much stock is available", "how many pending repairs", "how many low stock products"},question),question)).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status=="answered" && answer.Sections.Length==0 && answer.Links.Length==0,"Value-only dashboard enquiry: "+question);
        }
        answer=(await (await Ask(3700,"HOW MUCH SALE COMPLETED TODAY?")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Message=="2" && answer.Sections.Length==0,"Screenshot question returns completed bill count, not bill creation instructions");
        answer=(await (await Ask(3701,"How do I check completed sales today?")).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Sections.Any(section=>section.Title=="Check today's completed sales"),"How-to question returns checking instructions");
        using var anonymous = app.CreateClient();
        Check((int)(await anonymous.PostAsJsonAsync("/api/help/questions",new {question="quantity sold today"})).StatusCode==401,"Anonymous sales enquiry denied");
        var interpreter=new SalesIntentProvider();
        using var interpretedApp = factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=> {
            services.RemoveAll<ISalesEnquiryRepository>(); services.AddScoped<ISalesEnquiryRepository,ScopedSales>();
            services.RemoveAll<IInventoryRepository>(); services.AddScoped<IInventoryRepository,HelpStockChecks.ScopedStock>();
            services.RemoveAll<IHelpAiProvider>(); services.AddSingleton<IHelpAiProvider>(interpreter);
            services.PostConfigure<HelpAiOptions>(options=> {options.Enabled=true;options.Model="test-only";});
        }));
        using var interpretedClient=interpretedApp.CreateClient();
        interpretedClient.DefaultRequestHeaders.Add("Test-Role","Admin");
        interpretedClient.DefaultRequestHeaders.Add("Test-UserId","4001");
        var interpretedToken=await interpretedClient.GetFromJsonAsync<JsonElement>("/api/antiforgery");
        interpretedClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN",interpretedToken.GetProperty("requestToken").GetString());
        answer=(await (await interpretedClient.PostAsJsonAsync("/api/help/questions",new {question="कल कितने सामान बेचे?"})).Content.ReadFromJsonAsync<HelpAnswer>())!;
        Check(answer.Message=="4" && answer.Links.Length==0 && ScopedSales.LastPeriod?.Period=="yesterday","AI-interpreted wording uses scoped records, not model-generated figures");
        var metrics=new[] {"stock","low-stock","products","suppliers","customers","repairs","ready-repairs","warranties"};
        for(var index=0;index<metrics.Length;index++) {
            interpreter.Intent=new(metrics[index],"current");
            interpretedClient.DefaultRequestHeaders.Remove("Test-UserId");
            interpretedClient.DefaultRequestHeaders.Add("Test-UserId",(4200+index).ToString());
            interpretedClient.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            interpretedToken=await interpretedClient.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            interpretedClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN",interpretedToken.GetProperty("requestToken").GetString());
            calls=ScopedSales.Calls;
            answer=(await (await interpretedClient.PostAsJsonAsync("/api/help/questions",new {question="Tell me the current business figure"})).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status=="answered" && !string.IsNullOrWhiteSpace(answer.Message) && answer.Sections.Length==0 && answer.Links.Length==0 && calls==ScopedSales.Calls,"AI routes to the appropriate dashboard service: "+metrics[index]);
        }
        var stockCases=new[] {("Quantity","answered","12"),("missing","answered","No matching product found."),("many","clarify",(string?)null),("Battery","clarify",(string?)null)};
        for(var index=0;index<stockCases.Length;index++) {
            var item=stockCases[index];
            interpreter.Intent=new("product-stock","current",Term:item.Item1);
            interpretedClient.DefaultRequestHeaders.Remove("Test-UserId");
            interpretedClient.DefaultRequestHeaders.Add("Test-UserId",(4250+index).ToString());
            interpretedClient.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            interpretedToken=await interpretedClient.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            interpretedClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN",interpretedToken.GetProperty("requestToken").GetString());
            answer=(await (await interpretedClient.PostAsJsonAsync("/api/help/questions",new {question="Check named product availability"})).Content.ReadFromJsonAsync<HelpAnswer>())!;
            Check(answer.Status==item.Item2 && (item.Item3 is null || answer.Message==item.Item3) && answer.Links.Length==0,"Product-stock tool distinguishes quantity, missing records, broad matches and discrepancies: "+item.Item1);
        }
        interpreter.Intent=new("stock","current");
        interpretedClient.DefaultRequestHeaders.Add("Test-Deny","INVENTORY");
        interpretedClient.DefaultRequestHeaders.Remove("Test-UserId");
        interpretedClient.DefaultRequestHeaders.Add("Test-UserId","4300");
        interpretedClient.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        interpretedToken=await interpretedClient.GetFromJsonAsync<JsonElement>("/api/antiforgery");
        interpretedClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN",interpretedToken.GetProperty("requestToken").GetString());
        Check((int)(await interpretedClient.PostAsJsonAsync("/api/help/questions",new {question="Tell me the current business figure"})).StatusCode==403,"AI-selected tools still enforce inventory permission");
        Console.WriteLine("Sales enquiry checks use scoped fake records; no database writes or live database accuracy claims.");
    }
    private static void Check(bool condition,string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    private class ScopedSales(BusinessScope scope) : ISalesEnquiryRepository
    {
        public static int Calls;
        public static SalesPeriod? LastPeriod;
        public Task<SalesPeriodSummary> GetSummaryAsync(SalesPeriod period, CancellationToken cancellation)
        {
            Calls++;
            LastPeriod=period;
            return Task.FromResult(new SalesPeriodSummary(new DateTime(2026,10,7),new DateTime(2026,10,7),
                scope.TenantId==2?99:period.Period=="yesterday"?4:7,2,100m,80m));
        }
        public Task<TodaySoldQuantity> GetTodaySoldQuantityAsync()
        {
            Calls++;
            return Task.FromResult(new TodaySoldQuantity(new DateTime(2026,10,8),scope.TenantId==2?99:7,2));
        }
    }
    private class SalesIntentProvider : IHelpAiProvider
    {
        public SalesQuestion Intent=new("quantity","yesterday");
        public Task<string[]> SelectAsync(string question,HelpSection[] sections,CancellationToken cancellation)=>Task.FromResult(Array.Empty<string>());
        public Task<SalesQuestion?> InterpretSalesAsync(string question,CancellationToken cancellation)=>Task.FromResult<SalesQuestion?>(Intent);
    }
}
