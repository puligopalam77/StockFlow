using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StockFlow.MasterDataChecks;

internal static class HelpPanelChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS: " + message);
        }
        static JsonArray Articles(string html) => JsonNode.Parse(Regex.Match(html,
            "<script id=\"help-data\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value)!.AsArray();
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var purchase = await admin.GetStringAsync("/Purchases/Create");
        var articles = Articles(purchase);
        Check(articles.Count == 8 && articles.All(article => article!["content"]!.GetValue<string>().StartsWith("# ")),
            "Eight reviewed guides are embedded in the help panel");
        Check(purchase.Contains("id=\"help-open\"") && purchase.Contains("id=\"help-panel\""), "Authenticated screens include the help button and dialog");
        Check(articles.Single(article => article!["id"]!.GetValue<string>() == "billing")!["links"]!.AsArray()
            .Any(link => link!["url"]!.GetValue<string>() == "/Sales/Create"), "Authorized user receives billing screen link");
        using var restricted = factory.CreateClient();
        restricted.DefaultRequestHeaders.Add("Test-Role", "Admin");
        restricted.DefaultRequestHeaders.Add("Test-Deny-Action", "SALES:CREATE");
        var denied = Articles(await restricted.GetStringAsync("/Purchases/Create"));
        Check(!denied.SelectMany(article => article!["links"]!.AsArray())
            .Any(link => link!["url"]!.GetValue<string>() == "/Sales/Create"), "Create-sale link is omitted when permission is denied");
        using var anonymous = factory.CreateClient();
        Check(!(await anonymous.GetStringAsync("/Account/Login")).Contains("id=\"help-data\""), "Anonymous login does not expose staff help panel");
        var access = new StockFlow.Web.Security.PermissionContext { User = new StockFlow.Application.DTOs.Security.UserAccess {
            IsActive=true, Permissions=StockFlow.Application.Common.PermissionCatalog.Modules.Select(module =>
                new StockFlow.Application.DTOs.Security.ModulePermission {ModuleCode=module.Key,Actions=module.Value}).ToList()
        }};
        var service = new StockFlow.Web.Help.HelpAiService(new NoModel(),Microsoft.Extensions.Options.Options.Create(new StockFlow.Web.Help.HelpAiOptions {Enabled=true,Model="not-needed"}));
        foreach(var (question,title) in new[] {
            ("How do I use Shop summary?","Reports"),
            ("How do I view Product sales & profit?","Reports"),
            ("How do I find More reports?","Reports"),
            ("How do I export sales invoices?","Search and export sales invoices"),
            ("How do I search sales invoices?","Search and export sales invoices"),
            ("How do I correct stock quantity?","Correct stock quantity and count stock"),
            ("How do I count stock in the shop?","Correct stock quantity and count stock"),
            ("How do I find stock receipt history?","Find purchase orders and stock receipt history"),
            ("How do I view stock receipt details?","Find purchase orders and stock receipt history"),
            ("How do I filter item movements?","Find purchase orders and stock receipt history"),
            ("How do I change the business logo?","Shop details & bill settings"),
            ("How do I print the saved logo on invoices?","Shop details & bill settings"),
            ("How do I set invoice prefixes and payment terms?","Shop details & bill settings"),
            ("How do I calculate the cash closing difference?","Cash closing"),
            ("How do I connect the Android app?","Connect the Android app"),
            ("How do I scan using the camera?","Scan using the camera"),
            ("How do I use the microphone?","Ask using the microphone"),
            ("What can I ask Quick Assist?","Ask supported questions"),
            ("How do I save as PDF?","Upload files, download documents and print")
        })
        {
            var answer=await service.AskAsync(question,access,CancellationToken.None);
            Check(answer.Status=="answered" && answer.Sections.Single().Title==title,"Current workflow answered without AI: "+question);
        }
        var viewOnly=new StockFlow.Web.Security.PermissionContext {User=new StockFlow.Application.DTOs.Security.UserAccess {
            IsActive=true,Permissions=[new() {ModuleCode="INVENTORY",Actions=["VIEW"]},new() {ModuleCode="SALES",Actions=["VIEW"]}]
        }};
        Check(service.QuickAnswer("How do I correct stock quantity?",viewOnly)?.Status=="restricted","Stock adjustment guidance requires adjustment permission");
        Check(service.QuickAnswer("How do I export sales invoices?",viewOnly)?.Status=="answered","Sales list guidance works for permitted viewers without sale creation access");
        Check(service.QuickAnswer("How much profit today?",access) is null,"Profit figure is not replaced with report guidance");
        foreach (var code in new[] { "as", "bn", "brx", "doi", "gu", "hi", "kn", "ks", "kok", "mai", "ml", "mni", "mr", "ne", "or", "pa", "sa", "sat", "sd", "ta", "te", "ur" })
        {
            var response = await anonymous.GetAsync($"/languages/help/{code}.json");
            Check(response.IsSuccessStatusCode, $"Help language pack {code} is served locally");
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/help-panel-fixtures");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "purchase.html"), purchase);
        await File.WriteAllTextAsync(Path.Combine(output, "billing.html"), await admin.GetStringAsync("/Sales/Create"));
        await File.WriteAllTextAsync(Path.Combine(output, "restricted.html"), await restricted.GetStringAsync("/Purchases/Create"));
    }
    private sealed class NoModel : StockFlow.Web.Help.IHelpAiProvider
    {
        public Task<string[]> SelectAsync(string question,StockFlow.Web.Help.HelpSection[] sections,CancellationToken cancellation) => throw new Exception("Current workflow unnecessarily waited for AI.");
    }
}
