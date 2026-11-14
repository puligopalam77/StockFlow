using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Help;

namespace StockFlow.MasterDataChecks;

internal static class HelpRetailChecks
{
    public static async Task Run(Factory factory)
    {
        var branches = new FakeBranchRepository();
        foreach (var invalidScope in new[] {
            new BusinessScope { TenantId=1,UserId=1,CanReviewStores=true,Consolidated=true },
            new BusinessScope { TenantId=1,UserId=1,IsOwner=true,CanReviewStores=true,LocationId=1,StoreId=1 },
            new BusinessScope { TenantId=0,UserId=1,IsOwner=true,CanReviewStores=true,Consolidated=true } })
        {
            var disconnected = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]="Host=localhost;Database=unused" }).Build();
            var repository = new HelpRetailRepository(new DbConnectionFactory(disconnected,invalidScope),invalidScope,Options.Create(new ShopTimeOptions()),TimeProvider.System);
            var rejectedBeforeConnection=false;
            try { await repository.CompareShopsAsync(new("today"),CancellationToken.None); }
            catch(UnauthorizedAccessException) { rejectedBeforeConnection=true; }
            Check(rejectedBeforeConnection,"The concrete comparison repository rejects invalid scope before opening a connection");
        }
        Check(RetailAssistantService.Parse("Compare this month's collections by shop")?.Metric=="shop-collections", "Natural month comparison wording selects the collection tool");
        Check(RetailAssistantService.Parse("Compare sales by shop from 2026-10-01 to 2026-10-07")?.Period.Period=="range", "Shop comparisons retain validated explicit date ranges");
        using(var healthCache = new MemoryCache(new MemoryCacheOptions()))
        {
            var missingHealth = new HelpHealthService(new HealthClients("{\"models\":[]}"), Options.Create(new HelpAiOptions { Enabled=true,Model="qwen3:4b" }),new ConfigurationBuilder().Build(),healthCache);
            Check((await missingHealth.CheckAsync(CancellationToken.None)).ModelStatus=="model-missing", "Health distinguishes a missing model from connectivity failure");
        }
        using(var healthCache = new MemoryCache(new MemoryCacheOptions()))
        {
            var offlineHealth = new HelpHealthService(new HealthClients(null), Options.Create(new HelpAiOptions { Enabled=true,Model="qwen3:4b" }),new ConfigurationBuilder().Build(),healthCache);
            var offline = await offlineHealth.CheckAsync(CancellationToken.None);
            Check(offline.ModelStatus=="unavailable" && offline.VoiceStatus=="unavailable", "Runtime failures produce fallback capability status without throwing");
        }
        var values = new Values();
        var comparisons = new Comparisons();
        using var app = factory.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["HelpAi:StartLocalRuntime"]="false", ["HelpMonitoring:WriteFile"]="false" }));
            builder.ConfigureServices(services => {
                services.RemoveAll<IBranchRepository>(); services.AddSingleton<IBranchRepository>(branches);
                services.RemoveAll<IHelpRetailRepository>(); services.AddSingleton<IHelpRetailRepository>(comparisons);
                services.RemoveAll<ISalesEnquiryRepository>(); services.AddSingleton<ISalesEnquiryRepository, Sales>();
                services.RemoveAll<IHelpBusinessRepository>(); services.AddSingleton<IHelpBusinessRepository>(values);
                services.PostConfigure<HelpAiOptions>(options => { options.Enabled=true; options.Model="qwen3:4b"; });
                services.AddHttpClient("StockBotHealth").ConfigurePrimaryHttpMessageHandler(() => new HealthHandler());
            });
        });
        using var client = app.CreateClient();
        using var anonymous = app.CreateClient(new() { AllowAutoRedirect=false });
        Check((await anonymous.GetAsync("/api/help/assistant/health")).StatusCode == HttpStatusCode.Unauthorized, "Runtime health requires sign-in");
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var id=8500;
        async Task<HelpAnswer> Ask(string text, bool newUser=true)
        {
            if (newUser) { client.DefaultRequestHeaders.Remove("Test-UserId"); client.DefaultRequestHeaders.Add("Test-UserId", (++id).ToString()); }
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
            var response=await client.PostAsJsonAsync("/api/help/questions",new { question=text, conversationId="11111111-1111-4111-8111-111111111111" });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<HelpAnswer>())!;
        }
        var health=await client.GetFromJsonAsync<HelpHealth>("/api/help/assistant/health");
        Check(health?.ModelStatus=="available" && health.VoiceStatus=="available" && health.LatencyMilliseconds.HasValue, "Health checks model presence and local speech connectivity without inference");
        var summary=await Ask("Summarize today's business");
        Check(summary.Context?.Scope.Contains("Main store")==true && summary.Context.From==summary.Context.To && summary.Context.Sources.Length>0,
            "Daily summary carries the authenticated shop, date and permitted source links");
        Check(summary.Facts.Any(f=>f.Label=="Money collected today" && f.Value.Contains("75")) && summary.Facts.Any(f=>f.Label=="Today's returns"),
            "Daily summary keeps collections and returns separate from sales");
        client.DefaultRequestHeaders.Add("Test-Deny", "SALES");
        var context=await client.GetFromJsonAsync<JsonElement>("/api/help/assistant?screen=/Inventory");
        var suggestions=context.GetProperty("suggestions").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        Check(!suggestions.Any(q=>q.Contains("sales",StringComparison.OrdinalIgnoreCase)||q.Contains("Payment",StringComparison.OrdinalIgnoreCase)), "Suggestions follow current effective permissions");
        var restricted=await Ask("Daily business summary");
        Check(!restricted.Facts.Any(f=>f.Label.Contains("sales",StringComparison.OrdinalIgnoreCase)||f.Label.Contains("collected",StringComparison.OrdinalIgnoreCase)||f.Label.Contains("dues",StringComparison.OrdinalIgnoreCase)),
            "Denied financial modules are omitted from summary without querying them");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        values.FailTransfers=true;
        var partial=await Ask("What needs attention today?");
        Check(partial.Facts.Any(f=>f.Label=="Transfers awaiting receipt" && f.Value=="Unavailable") && partial.Facts.Any(f=>f.Label=="Purchase deliveries" && f.Value=="42"),
            "One failing summary source is unavailable rather than zero and does not hide other sources");
        values.FailTransfers=false;
        var selected=await Ask("Compare sales by shop this week");
        Check(selected.Status=="clarify" && comparisons.Calls==0, "A comparison cannot silently broaden a selected shop scope");
        client.DefaultRequestHeaders.Remove("Test-Location"); client.DefaultRequestHeaders.Add("Test-Location","0");
        client.DefaultRequestHeaders.Remove("Test-Store"); client.DefaultRequestHeaders.Add("Test-Store","0");
        var compared=await Ask("Compare sales by shop this week");
        Check(compared.Status=="answered" && compared.Facts.Length==2 && comparisons.Calls==1 && compared.Context?.From is not null,
            "Authorized all-shops owner comparison returns dated structured results");
        var collections=await Ask("Compare collections by shop this month");
        Check(collections.Facts[0].Label=="Second shop", "Collections rank by collections rather than sales");
        client.DefaultRequestHeaders.Add("Test-Deny","REPORTS");
        var denied=await Ask("Compare sales by shop today");
        Check(denied.Status=="restricted" && comparisons.Calls==2, "Report permission is checked before any shop comparison query");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        branches.State=new BranchUserState { IsActive=true, IsAdmin=true, IsOwner=false };
        var admin=await Ask("Compare sales by shop today");
        Check(admin.Status=="restricted" && comparisons.Calls==2, "Shop administrators cannot use owner comparison tools");
        branches.State=new BranchUserState { IsActive=true, IsOwner=true };
        await Ask("Compare sales by shop"); await Task.Delay(3100);
        var followUp=await Ask("Yesterday",false);
        Check(followUp.Status=="answered" && comparisons.Last?.Period=="yesterday", "Comparison date clarification retains the approved metric");
        await Ask("Quantity sold yesterday"); await Task.Delay(3100);
        client.DefaultRequestHeaders.Add("Test-Deny","INVENTORY");
        var revokedContext=await Ask("Yesterday",false);
        Check(revokedContext.Status=="clarify", "Permission changes discard old conversation intent");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        client.DefaultRequestHeaders.Add("Test-Version","2");
        Check((await client.GetAsync("/api/help/assistant/health")).StatusCode==HttpStatusCode.Unauthorized, "Revoked security versions cannot access assistant capabilities");
        client.DefaultRequestHeaders.Remove("Test-Version");
        foreach (var unsupported in new[] { "Compare sales for shop Other today", "Which shops have low-stock products?", "Compare collections across tenant Other today" })
            Check(SalesQuestion.HasUnsupportedFilters(unsupported), "Unsupported shop or tenant filters cannot become an unfiltered total: " + unsupported);
        Check(RetailAssistantService.Parse("Compare sales of my three garments shops") is null, "Named-shop filters are not silently removed");
        var html=await client.GetStringAsync("/Sales/Create");
        Check(html.Contains("help-runtime-status") && html.Contains("help-shop-scope") && html.Contains("help-assistant-data"), "The drawer includes capability status and server-filtered suggestions");
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        var directory=Path.Combine(root!.FullName,"tests/bin/help-retail-fixtures");Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory,"billing.html"),html);
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); Console.WriteLine("PASS: "+message); }
    private sealed class Sales : ISalesEnquiryRepository
    {
        public Task<TodaySoldQuantity> GetTodaySoldQuantityAsync()=>throw new NotSupportedException();
        public Task<SalesPeriodSummary> GetSummaryAsync(SalesPeriod period,CancellationToken cancellation)=>Task.FromResult(new SalesPeriodSummary(new(2026,11,14),new(2026,11,14),4,2,100,75));
    }
    private sealed class Values : IHelpBusinessRepository
    {
        public bool FailTransfers;
        public Task<BusinessValueResult> GetAsync(BusinessValueQuery query,CancellationToken cancellation)=> FailTransfers && query.Metric=="pending-transfers"
            ? Task.FromException<BusinessValueResult>(new TimeoutException()) : Task.FromResult(new BusinessValueResult("answered","42"));
    }
    private sealed class Comparisons : IHelpRetailRepository
    {
        public int Calls; public SalesPeriod? Last;
        public Task<IReadOnlyList<ShopAssistantTotal>> CompareShopsAsync(SalesPeriod period,CancellationToken cancellation)
        { Calls++; Last=period; return Task.FromResult<IReadOnlyList<ShopAssistantTotal>>([new(1,"Main shop",2,100,10),new(2,"Second shop",1,50,80)]); }
    }
    private sealed class HealthHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {Content=new StringContent(request.RequestUri!.AbsolutePath=="/api/tags" ? "{\"models\":[{\"name\":\"qwen3:4b\"}]}" : "{}",Encoding.UTF8,"application/json")});
    }
    private sealed class HealthClients(string? models) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(new Handler(models));
        private sealed class Handler(string? models) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) => models is null
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Controlled offline runtime"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(models,Encoding.UTF8,"application/json") });
        }
    }
}
