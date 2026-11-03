using System.Net.Http.Json;
using StockFlow.Application.DTOs.Scanning;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class ScanChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient();
        if ((int)(await client.GetAsync("/api/scan/00123")).StatusCode != 401) throw new Exception("Scan must require login");
        foreach (var role in new[] { "Admin", "Sales", "Service" })
        {
            client.DefaultRequestHeaders.Remove("Test-Role"); client.DefaultRequestHeaders.Add("Test-Role", role);
            foreach (var code in new[] { "00123", "BAT001", "UNIT001", "SOLD", "DAMAGED", "RETURNED", "INACTIVE" })
            {
                var result = await client.GetFromJsonAsync<ScanLookupResponse>("/api/scan?code=" + Uri.EscapeDataString(" " + code.ToLowerInvariant() + " "));
                if (result == null || result.IsAvailable != (code is "00123" or "BAT001" or "UNIT001")) throw new Exception("Scan availability failed: " + code);
                if (code == "00123" && result.MatchType != "PRODUCT_BARCODE") throw new Exception("Product match failed");
                if (code == "UNIT001" && result.MatchType != "UNIT_BARCODE") throw new Exception("Unit match failed");
            }
            if (!(await client.GetAsync("/Scan")).IsSuccessStatusCode) throw new Exception("Scan page missing");
        }
        foreach (var code in new[] { "", new string('x', 201), "a\nb" })
            if ((int)(await client.GetAsync("/api/scan?code=" + Uri.EscapeDataString(code))).StatusCode != 400) throw new Exception("Invalid code accepted");
        if ((int)(await client.GetAsync("/api/scan/unknown")).StatusCode != 404) throw new Exception("Missing scan must be 404");
        Console.WriteLine("PASS: scan auth, all roles, normalization, match types, status availability, invalid and missing codes");
    }
}
internal class FakeScanRepository : IScanRepository
{
    public Task<ScanLookupResponse?> FindByCodeAsync(string code)
    {
        code = code.ToUpperInvariant();
        if (!new[] { "00123", "BAT001", "UNIT001", "SOLD", "DAMAGED", "RETURNED", "INACTIVE" }.Contains(code)) return Task.FromResult<ScanLookupResponse?>(null);
        return Task.FromResult<ScanLookupResponse?>(new() {
            ProductId = 1, ProductName = "Test product", IsActive = code != "INACTIVE", CurrentStock = 3,
            ProductUnitId = code == "00123" ? null : 1, IsSerialTracked = code != "00123",
            MatchType = code == "00123" ? "PRODUCT_BARCODE" : code == "UNIT001" ? "UNIT_BARCODE" : "SERIAL_NUMBER",
            UnitStatus = code is "SOLD" or "DAMAGED" or "RETURNED" ? code : "IN_STOCK"
        });
    }
}
