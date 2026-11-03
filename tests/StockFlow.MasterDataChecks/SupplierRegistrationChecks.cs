using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;
internal static class SupplierRegistrationChecks
{
    public static async Task Run(Factory factory)
    {
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "tests/bin/quick-supplier-ui-fixtures");
        Directory.CreateDirectory(fixtures);
        foreach (var (url, name) in new[] { ("/Purchases/Create", "Purchase"), ("/Receiving", "Receiving") })
        {
            var html = await client.GetStringAsync(url);
            Check(html.Contains("data-create-only=\"true\"") && html.Contains("+ Add Supplier"), "Quick Add Supplier is available on " + url);
            await File.WriteAllTextAsync(Path.Combine(fixtures, name + ".html"), html);
            client.DefaultRequestHeaders.Add("Test-Deny-Action", "SUPPLIERS:CREATE");
            html = await client.GetStringAsync(url);
            Check(!html.Contains("data-create-only=\"true\"") && !html.Contains("+ Add Supplier"), "Supplier creation permission hides the action and dialog on " + url);
            await File.WriteAllTextAsync(Path.Combine(fixtures, name + "Limited.html"), html);
            client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        }
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var options=await client.GetFromJsonAsync<JsonObject>("/api/suppliers/options");Check(options!["countries"]!.AsArray().Any(x=>x!["code"]!.GetValue<string>()=="IN"),"Country options include India");
        var body=new JsonObject{["supplierName"]="  Example   Textiles  ",["supplierType"]="WHOLESALER",["mobileNumber"]="9876543210",["alternateMobile"]="9876543211",["countryCode"]="IN",["state"]="Karnataka",["city"]="Bengaluru",["postalCode"]="560001",["gstNumber"]="29abcde1234f1z5",["paymentTermsDays"]=45,["notes"]=" Deliver weekdays "};
        var result=await client.PostAsJsonAsync("/api/suppliers",body);result.EnsureSuccessStatusCode();var row=(await result.Content.ReadFromJsonAsync<JsonObject>())!;var id=row["supplierId"]!.GetValue<int>();
        Check(row["supplierName"]!.GetValue<string>()=="Example Textiles" && row["gstNumber"]!.GetValue<string>()=="29ABCDE1234F1Z5" && row["mobileNumber"]!.GetValue<string>()=="+919876543210" && row["paymentTermsDays"]!.GetValue<int>()==45,"Supplier normalization and fields round-trip");
        var duplicates=await client.GetFromJsonAsync<JsonArray>("/api/suppliers/duplicates?name=example%20textiles");Check(duplicates!.Count==1,"Duplicate warning matches normalized name");
        duplicates=await client.GetFromJsonAsync<JsonArray>($"/api/suppliers/duplicates?name=example%20textiles&excludingId={id}");Check(duplicates!.Count==0,"Duplicate warning excludes edited supplier");
        (await client.PostAsJsonAsync("/api/suppliers",body)).EnsureSuccessStatusCode();Check(true,"Matching names are permitted after review");
        foreach(var (field,value) in new (string,JsonNode?)[]{("supplierName",JsonValue.Create("x")),("supplierName",JsonValue.Create("12345")),("countryCode",JsonValue.Create("ZZ")),("supplierType",JsonValue.Create("UNKNOWN")),("mobileNumber",JsonValue.Create("123")),("alternateMobile",JsonValue.Create("+919876543210")),("email",JsonValue.Create("invalid")),("postalCode",JsonValue.Create("123")),("gstNumber",JsonValue.Create("INVALID")),("state",null),("address",JsonValue.Create(new string('x',501))),("notes",JsonValue.Create(new string('x',1001))),("paymentTermsDays",JsonValue.Create(366))}){
            var invalid=(JsonObject)body.DeepClone();invalid[field]=value;Check((int)(await client.PostAsJsonAsync("/api/suppliers",invalid)).StatusCode==400,"Reject invalid "+field);
        }
        var foreign=new{supplierName="Overseas supplier",countryCode="US",mobileNumber="2125550123",postalCode="10001",paymentTermsDays=0};(await client.PostAsJsonAsync("/api/suppliers",foreign)).EnsureSuccessStatusCode();Check(true,"International supplier with 10-digit local phone and no GSTIN accepted");
        using var sales=factory.CreateClient();sales.DefaultRequestHeaders.Add("Test-Role","Sales");Check((int)(await sales.GetAsync("/api/suppliers/options")).StatusCode==403 && (int)(await sales.GetAsync("/api/suppliers/duplicates?name=test")).StatusCode==403,"Supplier helpers retain module permissions");
        Console.WriteLine("Supplier checks use fake repositories. Database migration and snapshot triggers are not executed.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
