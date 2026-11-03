using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StockFlow.MasterDataChecks;
internal static class ScreenLanguageChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool passed, string text) { if (!passed) throw new Exception(text); Console.WriteLine("PASS: " + text); }
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        Check((int)(await anonymous.GetAsync("/api/screenlanguages")).StatusCode == 401, "Language configuration requires sign-in");
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false }); admin.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var setting = new ScreenLanguageSetting { Screen = "application", Language = "te" };
        Check((int)(await admin.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode == 400, "Language saves require CSRF protection");
        var token = await admin.GetFromJsonAsync<JsonObject>("/api/antiforgery"); admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        Check((int)(await admin.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode == 204, "Administrator saves the business-wide language");
        var saved = await admin.GetFromJsonAsync<List<ScreenLanguageSetting>>("/api/screenlanguages");
        Check(saved!.Single().Screen == "application", "Every screen reads one shared language setting");
        var duplicate = new ScreenLanguageSetting { Screen = "purchases-create", Language = "te",
            Captions = new() { ["Supplier"] = "one", ["supplier"] = "two" } };
        Check((int)(await admin.PutAsJsonAsync("/api/screenlanguages", duplicate)).StatusCode == 400,
            "Old screen-specific caption payloads are rejected");
        setting.Language = "mr";
        Check((int)(await admin.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode == 204,
            "Any listed language can be saved without a caption editor");
        setting.Language = "invalid";
        Check((int)(await admin.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode == 400, "Unsupported language codes rejected");
        admin.DefaultRequestHeaders.Add("Test-Deny-Action", "COMPANY_SETTINGS:EDIT"); setting.Language = "hi";
        Check((int)(await admin.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode == 403, "Language configuration requires settings edit permission"); admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        using var staff = factory.CreateClient(new() { AllowAutoRedirect = false }); staff.DefaultRequestHeaders.Add("Test-Role", "Sales");
        Check((await staff.GetAsync("/api/screenlanguages")).IsSuccessStatusCode, "Staff can read the configured captions without settings permission");
        Check((int)(await staff.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode is 403 or 400, "Staff cannot change language configuration");
        using var unconfigured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IScreenLanguageRepository>();
            services.AddSingleton<IScreenLanguageRepository>(new FakeScreenLanguageRepository { Available = false });
        }));
        using var missing = unconfigured.CreateClient(new() { AllowAutoRedirect = false }); missing.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var missingResponse = await missing.GetAsync("/api/screenlanguages");
        var setup = await missingResponse.Content.ReadFromJsonAsync<JsonObject>();
        Check((int)missingResponse.StatusCode == 503 && setup!["code"]!.GetValue<string>() == "SCREEN_LANGUAGE_SETUP_REQUIRED", "Missing migration returns a clear setup response instead of a PostgreSQL exception");
        var missingToken = await missing.GetFromJsonAsync<JsonObject>("/api/antiforgery"); missing.DefaultRequestHeaders.Add("X-CSRF-TOKEN", missingToken!["requestToken"]!.GetValue<string>());
        Check((int)(await missing.PutAsJsonAsync("/api/screenlanguages", setting)).StatusCode == 503, "Saving is blocked until language migration is applied");
        Check((await missing.GetAsync("/Sales/Create")).IsSuccessStatusCode, "Ordinary billing page remains available before language setup");
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName,"tests/bin/screen-language-fixtures"); Directory.CreateDirectory(output);
        Check((int)(await admin.GetAsync("/Configuration/Languages")).StatusCode == 404,
            "The separate Configuration language page has been removed");
        var header = await admin.GetStringAsync("/Purchases/Create");
        Check(header.Contains("id=\"language-choice\"") && !header.Contains("id=\"business-location\"")
            && !header.Contains("href=\"/Configuration/Languages\""), "Header language dropdown replaces store selector and configuration link");
        var staffHeader = await staff.GetStringAsync("/Sales/Create");
        Check(staffHeader.Contains("data-can-save=\"false\""), "Header preserves administrator-only business language permission");
        foreach (var page in new[] { "/Purchases/Create", "/Sales/Create", "/Customers" })
            await File.WriteAllTextAsync(Path.Combine(output,page.Trim('/').Replace('/','-') + ".html"), await admin.GetStringAsync(page));
        foreach (var code in new[] { "as", "bn", "brx", "doi", "gu", "hi", "kn", "ks", "kok", "mai", "ml", "mni", "mr", "ne", "or", "pa", "sa", "sat", "sd", "ta", "te", "ur" })
        {
            var response = await anonymous.GetAsync($"/languages/{code}.json");
            var pack = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<Dictionary<string,string>>() : null;
            Check(pack is not null && pack.ContainsKey("language settings") && pack.ContainsKey("validation.phone"),
                $"The application serves the {code} caption and validation pack locally");
        }
        Console.WriteLine("Screen language checks passed using mocked repositories.");
    }
}
internal class FakeScreenLanguageRepository : IScreenLanguageRepository
{
    public bool Available { get; set; } = true;
    public Task<bool> IsAvailableAsync() => Task.FromResult(Available);
    private readonly Dictionary<string,ScreenLanguageSetting> settings = new();
    public Task<List<ScreenLanguageSetting>> GetAsync() => Task.FromResult(settings.Values.ToList());
    public Task SaveAsync(ScreenLanguageSetting setting)
    { ScreenLanguageRepository.Validate(setting); settings[setting.Screen] = setting; return Task.CompletedTask; }
}
