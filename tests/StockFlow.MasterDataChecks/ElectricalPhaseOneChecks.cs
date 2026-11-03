using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

internal static class ElectricalPhaseOneChecks
{
    // Fake repositories only: no SQL, migrations or running-server connection.
    public static async Task Run()
    {
        using (var factory = new Factory())
        {
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("Test-Role", "Admin");
            var branches = (FakeBranchRepository)factory.Services.GetRequiredService<IBranchRepository>();
            var original = branches.State;
            try
            {
                branches.State = new() { IsActive = true, IsOwner = true, BusinessProfileCode = "" };
                var context = await client.GetFromJsonAsync<JsonObject>("/api/branches/context");
                Check(context!["businessProfileCode"]!.GetValue<string>() == BusinessProfiles.Electrical
                    && !context["businessProfilePersisted"]!.GetValue<bool>(),
                    "legacy business context defaults to electrical before manual migration");

                branches.State = new() { IsActive = true, IsOwner = true, BusinessProfileCode = BusinessProfiles.Electrical, BusinessProfilePersisted = true };
                context = await client.GetFromJsonAsync<JsonObject>("/api/branches/context");
                Check(context!["businessProfileCode"]!.GetValue<string>() == BusinessProfiles.Electrical
                    && context["businessProfilePersisted"]!.GetValue<bool>()
                    && context["businessProfileName"]!.GetValue<string>() == BusinessProfiles.ElectricalName,
                    "persisted electrical identity is exposed without changing active location");
                Check(context["locationId"]!.GetValue<string>() == "1", "profile identity preserves existing location selection");

                client.DefaultRequestHeaders.Add("X-Business-Profile", "CLOTHING");
                context = await client.GetFromJsonAsync<JsonObject>("/api/branches/context?businessProfileCode=BIKE_ACCESSORIES");
                Check(context!["businessProfileCode"]!.GetValue<string>() == BusinessProfiles.Electrical,
                    "headers and query strings cannot change the business profile");
                client.DefaultRequestHeaders.Remove("X-Business-Profile");

                foreach (var path in new[] { "/Products", "/Purchases/Create", "/Sales/Create", "/Inventory", "/Warranty/Lookup", "/Service", "/Reports/ProductSales" })
                {
                    var html = await client.GetStringAsync(path);
                    Check(html.Contains("data-shop-type=\"battery-geyser\"") && html.Contains("data-business-profile=\"ELECTRICAL\""),
                        "legacy electrical UI behavior retained: " + path);
                    Check(html.Contains("/Warranty/Lookup") && html.Contains("/Service"),
                        "existing warranty and service navigation retained: " + path);
                }
                var products = await client.GetStringAsync("/Products");
                foreach (var id in new[] { "quick-category", "quick-tracking", "quick-warranty", "quick-provider", "quick-purchase", "quick-sale", "quick-has-variants", "quick-specifications" })
                    Check(products.Contains("id=\"" + id + "\""), "electrical registration field retained: " + id);

                branches.State.BusinessProfileCode = "UNRELEASED_PROFILE";
                Check((await client.GetAsync("/api/branches/context")).StatusCode == HttpStatusCode.Forbidden,
                    "unreleased profiles cannot silently use electrical rules");
            }
            finally { branches.State = original; }

            using var anonymous = factory.CreateClient();
            Check((await anonymous.GetAsync("/api/branches/context")).StatusCode == HttpStatusCode.Unauthorized,
                "business profile context requires authentication");
        }

        // Isolate mutable fake data between existing regression groups.
        await Group("product registration", ProductRegistrationChecks.Run);
        await Group("receiving and permissions", PurchaseReceivingChecks.Run);
        await Group("sales and serials", SalesChecks.Run);
        await Group("barcode and serial lookup", ScanChecks.Run);
        await Group("warranty", WarrantyChecks.Run);
        await Group("repairs", ServiceChecks.Run);
        await Group("inventory", InventoryControlChecks.Run);
        await Group("billing", BillingChecks.Run);
        await Group("configuration fixtures", ConfigurationChecks.Run);
        await Group("receiving fixtures", VariantUiFixtures.Write);
        await Group("service fixtures", ServiceUiFixtures.Write);
        Console.WriteLine("PASS: Phase 1 electrical baseline completed with fake repositories. No database scripts executed.");
    }

    private static async Task Group(string name, Func<Factory, Task> run)
    {
        using var factory = new Factory();
        await run(factory);
        Console.WriteLine("PASS: electrical baseline group — " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
