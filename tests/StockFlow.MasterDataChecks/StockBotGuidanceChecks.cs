using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Web.Help;

namespace StockFlow.MasterDataChecks;

internal static class StockBotGuidanceChecks
{
    public static async Task Run(Factory factory)
    {
        using var app=factory.WithWebHostBuilder(builder=>{
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["HelpAi:StartLocalRuntime"]="false",["HelpMonitoring:WriteFile"]="false" }));
            builder.ConfigureServices(services=>services.PostConfigure<HelpAiOptions>(options=>options.Enabled=false));
        });
        using var client=app.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var uid=9700;
        async Task<JsonElement> Metadata(string screen)=>await client.GetFromJsonAsync<JsonElement>("/api/help/assistant/guidance?screen="+Uri.EscapeDataString(screen));
        async Task<HelpAnswer> Ask(string question,string screen)
        {
            client.DefaultRequestHeaders.Remove("Test-UserId");client.DefaultRequestHeaders.Add("Test-UserId",(++uid).ToString());
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var token=await client.GetFromJsonAsync<JsonElement>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token.GetProperty("requestToken").GetString());
            var response=await client.PostAsJsonAsync("/api/help/questions",new {question,screenId=screen});response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<HelpAnswer>())!;
        }
        var sales=await Metadata("sales.new");
        Check(sales.GetProperty("screen").GetProperty("id").GetString()=="sales.new","Stable screen IDs select approved content");
        var steps=sales.GetProperty("screen").GetProperty("steps").EnumerateArray().ToArray();
        Check(steps.Length==5 && steps.Any(step=>step.GetProperty("instruction").GetString()!.Contains("does not accept new credit or split checkout")),"New Sale walkthrough matches current full-payment checkout");
        Check(!sales.GetRawText().Contains("passwordValue")&&!sales.GetRawText().Contains("connectionString"),"Guidance contains content, not entered form values or configuration secrets");
        var answer=await Ask("Explain this screen","sales.new");
        Check(answer.Guide?.Id=="sales.new" && answer.Status=="answered","Screen explanation works with AI disabled");
        var productWalkthrough=await Ask("How do I add a product?","inventory.products");
        Check(productWalkthrough.Guide?.Id=="inventory.products.create" && productWalkthrough.Sections.Any(section=>section.ArticleId=="products"),"Natural task questions start approved steppers while retaining their reviewed source");
        var field=await Ask("What is minimum stock?","inventory.stock");
        Check(field.Message.Contains("threshold") && !field.Message.Contains("Contact"),"Field terminology uses approved definitions without AI");
        var employee=await Ask("Where can I register a new employee?","sales.new");
        Check(employee.Links.Any(link=>link.Url=="/Admin/Users"),"Feature finder uses the permitted staff-management screen");
        var expense=await Ask("Where can I check my expenses?","sales.new");
        Check(expense.Links.Any(link=>link.Url=="/Expenses"),"Feature finder uses the actual expense route");
        var gst=await Ask("Where can I change GST settings?","sales.new");
        Check(gst.Links.Any(link=>link.Url=="/Admin/Gst"),"GST guidance opens the actual Admin/Gst Razor screen");
        client.DefaultRequestHeaders.Add("Test-Deny","USER_MANAGEMENT");
        var restricted=await Ask("Where can I register a new employee?","sales.new");
        Check(restricted.Status=="restricted" && restricted.Links.Length==0,"Unauthorized feature questions expose no actionable link");
        var filtered=await Metadata("settings.users.create");
        Check(filtered.GetProperty("screen").ValueKind==JsonValueKind.Null && !filtered.GetProperty("features").EnumerateArray().Any(item=>item.GetProperty("url").GetString()=="/Admin/Users"),"A forged screen ID cannot bypass current permissions");
        client.DefaultRequestHeaders.Remove("Test-Deny");client.DefaultRequestHeaders.Add("Test-Deny","PRODUCT_VARIANTS");
        var product=await Metadata("inventory.products.create");
        Check(product.GetProperty("screen").ValueKind==JsonValueKind.Null,"Product walkthrough requires variant-create permission too");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        var unknown=await Metadata("malicious.screen");Check(unknown.GetProperty("screen").ValueKind==JsonValueKind.Null,"Unknown screen IDs cannot inject instructions or selectors");
        client.DefaultRequestHeaders.Add("Test-Deny","SALES");
        var forbidden=await client.GetAsync("/api/sales");
        Check(forbidden.StatusCode==HttpStatusCode.Forbidden && forbidden.Headers.GetValues("X-StockFlow-Error-Code").Single()=="PERMISSION_DENIED","API authorization failures expose a fixed safe error code");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        var invalid=await client.PostAsJsonAsync("/api/help/questions",new {question="Explain this screen",screenId="<script>"});
        Check(invalid.StatusCode==HttpStatusCode.BadRequest && invalid.Headers.GetValues("X-StockFlow-Error-Code").Single()=="VALIDATION_ERROR","Invalid question context has structured validation guidance");
        var errors=StockBotGuidance.Errors;
        Check(errors.Single(e=>e.Code=="INSUFFICIENT_STOCK").Explanation.Contains("No available quantity has been guessed")&&errors.All(e=>!e.Explanation.Contains("SELECT ",StringComparison.OrdinalIgnoreCase)),"Error explanations never invent stock quantities or expose SQL");
        Check(StockBotErrorCodes.ForStatus(503)=="SERVICE_UNAVAILABLE"&&StockBotErrorCodes.ForStatus(200) is null,"Status-based advice does not guess a database outage or classify success as an error");
        Check(new GuidedValidationException("INVALID_PAYMENT_AMOUNT","Payment mismatch") is System.ComponentModel.DataAnnotations.ValidationException,"Typed error codes preserve existing validation handling");
        using var anonymous=app.CreateClient(new(){AllowAutoRedirect=false});
        Check((await anonymous.GetAsync("/api/help/assistant/guidance?screen=sales.new")).StatusCode==HttpStatusCode.Unauthorized,"Guidance metadata requires sign-in");
        var html=await client.GetStringAsync("/Sales/Create");
        Check(html.Contains("data-stockbot-screen=\"sales.new\"")&&html.Contains("help-check-form")&&html.Contains("help-guidance-data"),"The application embeds stable screen context and guide controls");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var directory=Path.Combine(root!.FullName,"tests/bin/stockbot-guidance-fixtures");Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory,"billing.html"),html);
        await File.WriteAllTextAsync(Path.Combine(directory,"sales.json"),(await Metadata("sales.new")).GetRawText());
    }
    private static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS: "+label);}
}
