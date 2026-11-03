using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Web.Help;

namespace StockFlow.MasterDataChecks;

internal static class HelpAiChecks
{
    public record Case(string Question, string? Article);
    public static async Task Run(Factory factory, bool live = false)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var cases = JsonSerializer.Deserialize<Case[]>(await File.ReadAllTextAsync(Path.Combine(root!.FullName,
            "tests/StockFlow.MasterDataChecks/help-ai-questions.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var fake = new FakeHelpProvider(cases);
        var model = Environment.GetEnvironmentVariable("STOCKFLOW_HELP_MODEL");
        if (live && string.IsNullOrWhiteSpace(model)) throw new Exception("Set STOCKFLOW_HELP_MODEL to an installed local model before live evaluation.");
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<HelpAiOptions>(options => { options.Enabled = true; options.PreferLocalAnswers = false; options.Model = live ? model! : "test-only"; });
            if (!live) { services.RemoveAll<IHelpAiProvider>(); services.AddSingleton<IHelpAiProvider>(fake); }
        }));
        using var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await admin.GetFromJsonAsync<JsonElement>("/api/antiforgery");
        admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
        async Task SetUser(int id)
        {
            admin.DefaultRequestHeaders.Remove("Test-UserId");
            admin.DefaultRequestHeaders.Add("Test-UserId", id.ToString());
            admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var csrf = await admin.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        }
        var results = new List<object>();
        var failures = 0;
        for (var index = 0; index < cases.Length; index++)
        {
            await SetUser(1000 + index);
            var response = await admin.PostAsJsonAsync("/api/help/questions", new { question = cases[index].Question });
            response.EnsureSuccessStatusCode();
            var answer = (await response.Content.ReadFromJsonAsync<HelpAnswer>())!;
            var passed = cases[index].Article == "business" ? answer.Status == "answered" && answer.Sections.Length == 0 : cases[index].Article is null ? answer.Status == "unsupported" :
                answer.Status == "answered" && answer.Sections.Any(section => section.ArticleId == cases[index].Article);
            foreach (var section in answer.Sections)
            {
                var source = HelpCatalog.Articles.Single(article => article.Id == section.ArticleId);
                if (!source.Content.Replace("\r", "").Contains(section.Content)) throw new Exception("Answer is not an approved excerpt.");
            }
            if (!passed) failures++;
            results.Add(new { cases[index].Question, cases[index].Article, answer.Status, passed,
                sources = answer.Sections.Select(section => section.Id) });
        }
        Directory.CreateDirectory(Path.Combine(root.FullName, "tests/bin"));
        await File.WriteAllTextAsync(Path.Combine(root.FullName, live ? "tests/bin/help-ai-live-report.json" : "tests/bin/help-ai-contract-report.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        if (live)
        {
            Console.WriteLine($"Live evaluation: {cases.Length - failures}/{cases.Length} expected source or fallback results. Native-language review is separate.");
            if (failures > 0) throw new Exception("Review the failed live AI answers before rollout.");
            return;
        }
        if (failures > 0) throw new Exception("Controlled-provider contract evaluation failed.");
        await HelpAiCancellationChecks.Run();
        // A configured but unavailable model must not delay common approved instructions.
        var quickProvider = new RejectSlowProvider();
        using (var quickApp = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<HelpAiOptions>(options => { options.Enabled=true; options.Model="offline-test"; options.PreferLocalAnswers=true; });
            services.RemoveAll<IHelpAiProvider>(); services.AddSingleton<IHelpAiProvider>(quickProvider);
        })))
        {
            using var quickClient=quickApp.CreateClient(); quickClient.DefaultRequestHeaders.Add("Test-Role","Admin");
            var csrf=await quickClient.GetFromJsonAsync<JsonElement>("/api/antiforgery");
            quickClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf.GetProperty("requestToken").GetString());
            var response=await quickClient.PostAsJsonAsync("/api/help/questions",new {question="How do I make a bill?"});
            if((await response.Content.ReadFromJsonAsync<HelpAnswer>())?.Status!="answered" || quickProvider.Calls!=0)
                throw new Exception("Approved help waited for the model.");
            var service=new HelpAiService(quickProvider,Microsoft.Extensions.Options.Options.Create(new HelpAiOptions {Enabled=true,Model="offline-test"}));
            foreach(var fact in new[] {"How many Battery units are available now?","How much did we collect by UPI last week?","Which customers have overdue dues?"})
                if(service.QuickAnswer(fact,new StockFlow.Web.Security.PermissionContext()) is not null)
                    throw new Exception("Quick help replaced a live business query with a guide.");
            using var canceled=new CancellationTokenSource(); canceled.Cancel();
            try { await service.AskAsync("How do I make a bill?",new StockFlow.Web.Security.PermissionContext(),canceled.Token); throw new Exception("Quick help ignored cancellation."); }
            catch(OperationCanceledException) when(canceled.IsCancellationRequested) { }
        }
        Console.WriteLine("PASS: approved help bypasses unavailable AI, live facts retain their query path, and quick answers respect cancellation.");
        using var wireClient = new HttpClient(new ProviderHandler());
        var wire = new OllamaHelpProvider(wireClient, Microsoft.Extensions.Options.Options.Create(new HelpAiOptions { Enabled = true, Model = "test-local-model" }));
        var wireIds = await wire.SelectAsync("How do I receive goods?", [new("receiving-stock:1", "receiving-stock", "Receive goods now", "Approved receipt instructions")], CancellationToken.None);
        if (wireIds.Single() != "receiving-stock:1") throw new Exception("Ollama response contract failed.");
        using var salesClient = new HttpClient(new SalesProviderHandler());
        var salesWire = new OllamaHelpProvider(salesClient, Microsoft.Extensions.Options.Options.Create(new HelpAiOptions { Enabled=true,Model="test-local-model" }));
        var intent = await salesWire.InterpretSalesAsync("What did we sell the previous day?",CancellationToken.None);
        if (intent?.Metric!="quantity" || intent.ValidPeriod()?.Period!="yesterday") throw new Exception("Sales intent response contract failed.");
        if(await salesWire.InterpretSalesAsync("How many units sold today?",CancellationToken.None) is not null)
            throw new Exception("The model was allowed to change an explicitly stated date.");
        if(await salesWire.InterpretSalesAsync("How many units sold on 2026-10-01?",CancellationToken.None) is not null)
            throw new Exception("The model was allowed to discard an explicit ISO date.");
        if(new SalesQuestion("stock","current").MatchesStatedPeriod("How much stock was available yesterday?"))
            throw new Exception("A historical stock question was interpreted as a current balance.");
        foreach (var invalid in new[] {
            new SalesQuestion("profit","yesterday"), new SalesQuestion("quantity","all-businesses"),
            new SalesQuestion("amount","range","2026-02-30","2026-03-01"),
            new SalesQuestion("quantity","range","2026-10-08","2026-10-01"),
            new SalesQuestion("count","range","2020-01-01","2026-10-08"),
            new SalesQuestion("quantity","today","2026-10-01","2026-10-01")
        }) if (invalid.ValidPeriod() is not null) throw new Exception("Invalid model sales filters accepted.");
        await SetUser(2000);
        fake.ForcedIds = ["invented-source"];
        var invented = await admin.PostAsJsonAsync("/api/help/questions", new { question = "How do I make a bill?" });
        if ((await invented.Content.ReadFromJsonAsync<HelpAnswer>())!.Status != "unsupported") throw new Exception("Invented citations accepted.");
        await SetUser(2001);
        admin.DefaultRequestHeaders.Add("Test-Deny-Action", "SALES:RETURN");
        fake.ForcedIds = ["returns:1"];
        var denied = await admin.PostAsJsonAsync("/api/help/questions", new { question = "How do I return an item?" });
        if ((await denied.Content.ReadFromJsonAsync<HelpAnswer>())!.Status != "unsupported" || fake.LastSections.Any(section => section.ArticleId == "returns"))
            throw new Exception("Restricted return guide was supplied to the model.");
        if ((int)(await admin.PostAsJsonAsync("/api/help/questions", new { question = "another question" })).StatusCode != 429) throw new Exception("Question cooldown missing.");
        await SetUser(2002);
        fake.Fail = true;
        var unavailable = await admin.PostAsJsonAsync("/api/help/questions", new { question = "How do I bill?" });
        if ((await unavailable.Content.ReadFromJsonAsync<HelpAnswer>())!.Status != "answered") throw new Exception("Provider failure did not use approved local help.");
        using var disabledApp = factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
            services.PostConfigure<HelpAiOptions>(options=>options.Enabled=false)));
        using var disabled = disabledApp.CreateClient(); disabled.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var disabledToken = await disabled.GetFromJsonAsync<JsonElement>("/api/antiforgery");
        disabled.DefaultRequestHeaders.Add("X-CSRF-TOKEN", disabledToken.GetProperty("requestToken").GetString());
        var notConfigured = await disabled.PostAsJsonAsync("/api/help/questions", new {question="How do I bill?"});
        if ((await notConfigured.Content.ReadFromJsonAsync<HelpAnswer>())!.Status != "answered") throw new Exception("Unconfigured AI did not use approved local help.");
        using var noToken = app.CreateClient(); noToken.DefaultRequestHeaders.Add("Test-Role", "Admin");
        if ((int)(await noToken.PostAsJsonAsync("/api/help/questions", new {question="How do I bill?"})).StatusCode != 400) throw new Exception("Missing CSRF accepted.");
        if ((int)(await admin.PostAsJsonAsync("/api/help/questions", new {question=new string('a',501)})).StatusCode != 400) throw new Exception("Oversized question accepted.");
        using var anonymous = app.CreateClient();
        if ((int)(await anonymous.PostAsJsonAsync("/api/help/questions", new {question="How do I bill?"})).StatusCode != 401) throw new Exception("Anonymous AI request accepted.");
        Console.WriteLine("PASS: 60 representative questions with controlled-provider responses, approved excerpts, unsupported fallback, permission filtering, invented sources, CSRF, sign-in, length and cooldown. Live model accuracy not measured.");
    }
    private class RejectSlowProvider : IHelpAiProvider
    {
        public int Calls;
        public Task<string[]> SelectAsync(string question, HelpSection[] sections, CancellationToken cancellation) { Calls++; throw new Exception("Quick help called AI."); }
        public Task<SalesQuestion?> InterpretSalesAsync(string question,CancellationToken cancellation) { Calls++; throw new Exception("Quick help called AI interpretation."); }
    }
    private class FakeHelpProvider(Case[] cases) : IHelpAiProvider
    {
        public string[]? ForcedIds;
        public bool Fail;
        public HelpSection[] LastSections = [];
        public Task<string[]> SelectAsync(string question, HelpSection[] sections, CancellationToken cancellation)
        {
            LastSections = sections;
            if (Fail) throw new HttpRequestException("test provider offline");
            if (ForcedIds is not null) return Task.FromResult(ForcedIds);
            var article = cases.Single(item => item.Question == question).Article;
            return Task.FromResult(article is null ? [] : new[] { sections.First(section => section.ArticleId == article).Id });
        }
    }
    private class ProviderHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (request.RequestUri!.AbsolutePath != "/api/chat" || payload.GetProperty("stream").GetBoolean() ||
                payload.GetProperty("model").GetString() != "test-local-model" ||
                payload.GetProperty("format").GetProperty("properties").GetProperty("sectionIds").GetProperty("maxItems").GetInt32() != 3)
                throw new Exception("Ollama structured request contract failed.");
            return new(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(new {
                message = new { content = JsonSerializer.Serialize(new { sectionIds = new[] { "receiving-stock:1" } }) }
            }) };
        }
    }
    private class SalesProviderHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var payload=await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var schema=payload.GetProperty("format");
            if (schema.GetProperty("additionalProperties").GetBoolean() ||
                !schema.GetProperty("properties").GetProperty("period").GetProperty("enum").EnumerateArray().Any(value=>value.GetString()=="yesterday"))
                throw new Exception("Sales intent schema does not constrain model filters.");
            return new(System.Net.HttpStatusCode.OK) {Content=JsonContent.Create(new {
                message=new {content=JsonSerializer.Serialize(new SalesQuestion("quantity","yesterday"))}
            })};
        }
    }
}
