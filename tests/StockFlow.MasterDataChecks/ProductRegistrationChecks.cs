using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StockFlow.MasterDataChecks;
internal static class ProductRegistrationChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new { categoryName = "Registration checks" });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<JsonObject>();
        var body = new JsonObject { ["productName"] = "  Cotton   Shirt  ", ["categoryId"] = category!["categoryId"]!.GetValue<int>(),
            ["description"] = " Cotton shirt ", ["hasVariants"] = true, ["trackingType"] = "QUANTITY", ["taxPercentage"] = 5,
            ["warrantyMonths"] = 12, ["warrantyProvider"] = "DEALER", ["purchasePrice"] = 123, ["sellingPrice"] = 234 };
        var created = await client.PostAsJsonAsync("/api/products", body); created.EnsureSuccessStatusCode();
        var row = (await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = row["productId"]!.GetValue<int>();
        Check(row["productName"]!.GetValue<string>() == "Cotton Shirt" && row["description"]!.GetValue<string>() == "Cotton shirt" && row["hasVariants"]!.GetValue<bool>() && row["warrantyProvider"]!.GetValue<string>() == "DEALER", "Product defaults and metadata round-trip");
        body.Remove("purchasePrice"); body.Remove("sellingPrice"); body["isActive"] = true;
        (await client.PutAsJsonAsync("/api/products/" + id, body)).EnsureSuccessStatusCode();
        row = (await client.GetFromJsonAsync<JsonObject>("/api/products/" + id))!;
        Check(row["purchasePrice"]!.GetValue<decimal>() == 123 && row["sellingPrice"]!.GetValue<decimal>() == 234, "Metadata edit preserves existing commercial values");
        foreach (var (field, bad) in new (string, JsonNode?)[] { ("productName", JsonValue.Create(" ")), ("productName", JsonValue.Create("7")), ("productName", JsonValue.Create("12345")), ("description", JsonValue.Create(new string('x',2001))), ("trackingType", JsonValue.Create("BATCH")), ("warrantyProvider", JsonValue.Create("INVALID")), ("taxPercentage", JsonValue.Create(1.234)), ("warrantyMonths", JsonValue.Create(241)) })
        {
            var invalid = (JsonObject)body.DeepClone(); invalid[field] = bad;
            Check((int)(await client.PostAsJsonAsync("/api/products", invalid)).StatusCode == 400, "Reject invalid " + field);
        }
        body["trackingType"] = "SERIAL"; body["warrantyMonths"] = 0;
        (await client.PutAsJsonAsync("/api/products/" + id, body)).EnsureSuccessStatusCode();
        row = (await client.GetFromJsonAsync<JsonObject>("/api/products/" + id))!;
        Check(row["isSerialTracked"]!.GetValue<bool>() && row["trackingType"]!.GetValue<string>() == "SERIAL" && row["warrantyProvider"] is null, "Tracking selection maps correctly; zero warranty clears provider");
        Check((int)(await client.PostAsJsonAsync("/api/sales", new { requestId=Guid.NewGuid(), items=new[]{new{productId=id,quantity=1,sellingPrice=0}},payments=Array.Empty<object>() })).StatusCode == 400, "Zero-priced catalog item cannot complete an ordinary sale");
        Console.WriteLine("Product registration checks use fake repositories; no SQL executed.");
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS: " + message); }
}
