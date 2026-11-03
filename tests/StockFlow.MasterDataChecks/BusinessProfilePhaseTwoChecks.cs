using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

internal static class BusinessProfilePhaseTwoChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        var branches = (FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();
        var repository = (FakeBusinessProfileRepository)factory.Services.GetRequiredService<IBusinessProfileRepository>();
        BusinessProfileChange Change(string code, long version) => new() { Code = code, ExpectedVersion = version, Reason = "Configure test business", Acknowledged = true, CreateStarterCategories = true };
        var before = await client.GetFromJsonAsync<JsonObject>("/api/business-profile");
        Check(!before!["available"]!.GetValue<bool>(), "profile configuration is unavailable before manual migration 047");
        Check((await client.PutAsJsonAsync("/api/business-profile", Change("CLOTHING", 1))).StatusCode == HttpStatusCode.ServiceUnavailable, "save before migration returns actionable setup error");
        branches.State.BusinessProfileConfigurationAvailable = true; branches.State.BusinessProfileVersion = 1; branches.State.BusinessProfilePersisted = true;
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/business-profile-phase2-fixtures"); Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "Settings.html"), await client.GetStringAsync("/Settings/BusinessProfile"));
        await File.WriteAllTextAsync(Path.Combine(output, "Electrical.html"), await client.GetStringAsync("/Products"));
        foreach (var code in new[] { BusinessProfiles.Clothing, BusinessProfiles.Footwear, BusinessProfiles.BikeAccessories })
        {
            var version = branches.State.BusinessProfileVersion;
            Check((await client.PutAsJsonAsync("/api/business-profile", Change(code, version))).IsSuccessStatusCode, "administrator can configure empty business as " + code);
            var context = await client.GetFromJsonAsync<JsonObject>("/api/branches/context");
            Check(context!["businessProfileCode"]!.GetValue<string>() == code && context["businessProfileName"]!.GetValue<string>() == BusinessProfiles.Name(code), "profile is reloaded from tenant state");
            var categories = await client.GetFromJsonAsync<JsonArray>("/api/categories");
            Check(categories!.Count == BusinessProfiles.StarterCategories(code).Length && categories.All(c => c!["businessProfileCode"]!.GetValue<string>() == code), "only current-profile starter categories are returned");
            var html = await client.GetStringAsync("/Products");
            var navStart = html.IndexOf("<nav class=\"sf-nav sf-shop-nav\""); var navEnd = html.IndexOf("</nav>", navStart);
            var nav = html[navStart..navEnd];
            Check(!nav.Contains("/Warranty/") && !nav.Contains("/Service") && nav.Contains("/Purchases/Create") && nav.Contains("/Settings/BusinessProfile"), "profile filters specialist menus while keeping common menus");
            await File.WriteAllTextAsync(Path.Combine(output, code + ".html"), html);
            await File.WriteAllTextAsync(Path.Combine(output, "Settings-" + code + ".html"), await client.GetStringAsync("/Settings/BusinessProfile"));
            foreach (var url in new[] { "/api/service", "/api/warranty/1", "/api/dashboard/recent-services", "/api/operations/service/1" })
                Check((await client.GetAsync(url)).StatusCode == HttpStatusCode.Forbidden, "direct specialist API blocked: " + url);
            Check((await client.GetAsync("/Warranty/Lookup")).StatusCode == HttpStatusCode.Redirect, "direct specialist page blocked");
            var invalid = new { productName = "Invalid serialized product", categoryId = categories[0]!["categoryId"]!.GetValue<int>(), trackingType = "SERIAL" };
            Check((await client.PostAsJsonAsync("/api/products", invalid)).StatusCode == HttpStatusCode.BadRequest, "hidden serial feature cannot be re-enabled by direct product API");
            Check((await client.PutAsJsonAsync("/api/business-profile", Change(BusinessProfiles.Electrical, version))).StatusCode == HttpStatusCode.Conflict, "stale profile version rejected");
        }
        Check((await client.PutAsJsonAsync("/api/business-profile", Change("GROCERY", branches.State.BusinessProfileVersion))).StatusCode == HttpStatusCode.BadRequest, "out-of-scope shop profiles rejected");
        var unacknowledged = Change(BusinessProfiles.Clothing, branches.State.BusinessProfileVersion); unacknowledged.Acknowledged = false;
        Check((await client.PutAsJsonAsync("/api/business-profile", unacknowledged)).StatusCode == HttpStatusCode.BadRequest, "impact acknowledgement required");
        repository.Transactions = 1;
        Check((await client.PutAsJsonAsync("/api/business-profile", Change(BusinessProfiles.Clothing, branches.State.BusinessProfileVersion))).StatusCode == HttpStatusCode.BadRequest, "existing transactions block profile change");
        repository.Transactions = 0;
        var productRepo = factory.Services.GetRequiredService<IProductRepository>();
        var category = (await client.GetFromJsonAsync<JsonArray>("/api/categories"))![0]!["categoryId"]!.GetValue<int>();
        var productId = await productRepo.CreateAsync(new() { ProductName = "Existing inactive catalog item", CategoryId = category, IsActive = false });
        Check((await client.PutAsJsonAsync("/api/business-profile", Change(BusinessProfiles.Clothing, branches.State.BusinessProfileVersion))).StatusCode == HttpStatusCode.BadRequest, "inactive products also block profile change");
        Check((await productRepo.GetByIdAsync(productId))!.ProductName == "Existing inactive catalog item", "blocked changes preserve existing catalog");
        client.DefaultRequestHeaders.Add("Test-Deny-Action", "COMPANY_SETTINGS:EDIT");
        Check((await client.PutAsJsonAsync("/api/business-profile", Change(BusinessProfiles.Clothing, branches.State.BusinessProfileVersion))).StatusCode == HttpStatusCode.Forbidden, "profile capability does not grant missing user edit permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Check((await client.PutAsJsonAsync("/api/business-profile", Change(BusinessProfiles.Clothing, branches.State.BusinessProfileVersion))).StatusCode == HttpStatusCode.BadRequest, "profile saves require CSRF token");
        using var anonymous = factory.CreateClient(); Check((await anonymous.GetAsync("/api/business-profile")).StatusCode == HttpStatusCode.Unauthorized, "anonymous profile access denied");
        Console.WriteLine("PASS: Phase 2 profiles, impact protection, categories, menu/API restrictions, versioning and permissions. Fake repositories only; no SQL executed.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
}
