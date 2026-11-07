using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StockFlow.MasterDataChecks;

internal static class SupplierDirectoryChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        var ids = new List<int>();
        for (var index = 0; index < 31; index++)
        {
            var response = await client.PostAsJsonAsync("/api/suppliers", new { supplierName = $"Directory Supplier {index:D2}", mobileNumber = (9800000000L + index).ToString(), supplierType = "DISTRIBUTOR", notes = "Preserve all details", address = "Original address" });
            response.EnsureSuccessStatusCode();
            ids.Add((await response.Content.ReadFromJsonAsync<JsonObject>())!["supplierId"]!.GetValue<int>());
        }
        var first = await client.GetFromJsonAsync<JsonObject>("/api/suppliers/page?limit=10");
        var second = await client.GetFromJsonAsync<JsonObject>("/api/suppliers/page?limit=10&offset=10");
        Check(first!["total"]!.GetValue<long>() == 31 && first["items"]!.AsArray().Count == 10, "Supplier directory count and page size are server-produced");
        Check(!first["items"]!.AsArray().Select(x => x!["supplierId"]!.GetValue<int>()).Intersect(second!["items"]!.AsArray().Select(x => x!["supplierId"]!.GetValue<int>())).Any(), "Stable pages do not repeat supplier IDs");
        var mobile = await client.GetFromJsonAsync<JsonObject>("/api/suppliers/page?term=9800000007");
        Check(mobile!["total"]!.GetValue<long>() == 1, "Directory searches stored mobile values");
        var before = await client.GetFromJsonAsync<JsonObject>("/api/suppliers/" + ids[7]);
        (await client.PostAsJsonAsync($"/api/suppliers/{ids[7]}/status", new { isActive = false })).EnsureSuccessStatusCode();
        var inactive = await client.GetFromJsonAsync<JsonObject>("/api/suppliers/page?status=inactive");
        Check(inactive!["total"]!.GetValue<long>() == 1 && inactive["items"]![0]!["supplierId"]!.GetValue<int>() == ids[7], "Inactive filter uses server status");
        var after = await client.GetFromJsonAsync<JsonObject>("/api/suppliers/" + ids[7]);
        Check(after!["address"]!.GetValue<string>() == before!["address"]!.GetValue<string>() && after["notes"]!.GetValue<string>() == before["notes"]!.GetValue<string>() && after["supplierCode"]?.ToJsonString() == before["supplierCode"]?.ToJsonString(), "Status-only update preserves supplier data and identity");
        Check(!(await client.GetFromJsonAsync<JsonArray>("/api/suppliers?activeOnly=true"))!.Any(x => x!["supplierId"]!.GetValue<int>() == ids[7]), "Inactive supplier is removed from existing active-only selections");
        (await client.PostAsJsonAsync($"/api/suppliers/{ids[7]}/status", new { isActive = true })).EnsureSuccessStatusCode();
        Check((await client.GetFromJsonAsync<JsonObject>("/api/suppliers/page?status=inactive"))!["total"]!.GetValue<long>() == 0, "Activation restores status without remapping");
        foreach (var query in new[] { "limit=1000", "offset=-1", "status=invalid", "term=" + new string('x', 201) })
            Check((await client.GetAsync("/api/suppliers/page?" + query)).StatusCode == HttpStatusCode.BadRequest, "Invalid directory query rejected: " + query[..Math.Min(query.Length, 20)]);
        Check((await client.PostAsJsonAsync($"/api/suppliers/{ids[0]}/status", new { })).StatusCode == HttpStatusCode.BadRequest, "Missing status is rejected");
        Check((await client.PostAsJsonAsync("/api/suppliers/999999/status", new { isActive = false })).StatusCode == HttpStatusCode.NotFound, "Unknown supplier status target is rejected");
        client.DefaultRequestHeaders.Add("Test-Deny-Action", "SUPPLIERS:DEACTIVATE");
        Check((await client.PostAsJsonAsync($"/api/suppliers/{ids[0]}/status", new { isActive = false })).StatusCode == HttpStatusCode.Forbidden, "Status endpoint requires existing DELETE permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny-Action", "SUPPLIERS:CREATE");
        (await client.PostAsJsonAsync($"/api/suppliers/{ids[0]}/status", new { isActive = true })).EnsureSuccessStatusCode();
        Check(true, "Status permission does not accidentally require CREATE");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var directory = Path.Combine(root!.FullName, "tests/bin/supplier-directory-fixtures"); Directory.CreateDirectory(directory);
        foreach (var (file, deny) in new[] { ("List", ""), ("Readonly", "SUPPLIERS:CREATE,SUPPLIERS:EDIT,SUPPLIERS:DEACTIVATE") })
        {
            if (deny.Length > 0) client.DefaultRequestHeaders.Add("Test-Deny-Action", deny);
            var html = await client.GetStringAsync("/Suppliers");
            Check(!html.Contains("Saved Suppliers"), "Supplier list omits redundant Saved Suppliers heading");
            await File.WriteAllTextAsync(Path.Combine(directory, file + ".html"), html);
            client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        }
        client.DefaultRequestHeaders.Add("Test-Deny", "SUPPLIERS");
        Check((await client.GetAsync("/api/suppliers/page")).StatusCode == HttpStatusCode.Forbidden, "Directory read requires SUPPLIERS authorization");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Check((await client.PostAsJsonAsync($"/api/suppliers/{ids[0]}/status", new { isActive = false })).StatusCode == HttpStatusCode.BadRequest, "Status writes retain CSRF protection");
        Console.WriteLine("Supplier directory HTTP checks use fake repositories; real SQL pagination is checked separately.");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
}
