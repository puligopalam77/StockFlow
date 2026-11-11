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
        foreach (var (url, name) in new[] { ("/Purchases/Create?mode=order", "Purchase"), ("/Receiving", "Receiving") })
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
        var minimalResponse = await client.PostAsJsonAsync("/api/suppliers", new { supplierName = "Name Only Supplier" });
        minimalResponse.EnsureSuccessStatusCode();
        var minimal = (await minimalResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        Check(minimal["countryCode"]!.GetValue<string>() == "IN" && minimal["mobileNumber"] is null && minimal["gstNumber"] is null, "Name-only creation uses India default and leaves optional details empty");
        Check((int)(await client.PostAsJsonAsync("/api/suppliers", new { supplierName = "   " })).StatusCode == 400, "Blank supplier name rejected");
        client.DefaultRequestHeaders.Add("Test-Deny-Action", "SUPPLIERS:CREATE");
        Check((int)(await client.PostAsJsonAsync("/api/suppliers", new { supplierName = "Denied supplier" })).StatusCode == 403, "Unauthorized supplier creation denied");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var body=new JsonObject{["supplierName"]="  Example   Textiles  ",["supplierType"]="WHOLESALER",["mobileNumber"]="9876543210",["alternateMobile"]="9876543211",["countryCode"]="IN",["state"]="Karnataka",["city"]="Bengaluru",["district"]="Bengaluru Urban",["postalCode"]="560001",["gstNumber"]="29abcde1234f1z5",["paymentTermsDays"]=45,["notes"]=" Deliver weekdays "};
        var result=await client.PostAsJsonAsync("/api/suppliers",body);result.EnsureSuccessStatusCode();var row=(await result.Content.ReadFromJsonAsync<JsonObject>())!;var id=row["supplierId"]!.GetValue<int>();
        Check(row["supplierName"]!.GetValue<string>()=="Example Textiles" && row["gstNumber"]!.GetValue<string>()=="29ABCDE1234F1Z5" && row["mobileNumber"]!.GetValue<string>()=="+919876543210" && row["paymentTermsDays"]!.GetValue<int>()==45,"Supplier normalization and fields round-trip");
        Check(row["district"]!.GetValue<string>()=="Bengaluru Urban","Optional district round-trips through authorized supplier API");
        var duplicates=await client.GetFromJsonAsync<JsonArray>("/api/suppliers/duplicates?name=example%20textiles");Check(duplicates!.Count==1,"Duplicate warning matches normalized name");
        duplicates=await client.GetFromJsonAsync<JsonArray>($"/api/suppliers/duplicates?name=example%20textiles&excludingId={id}");Check(duplicates!.Count==0,"Duplicate warning excludes edited supplier");
        (await client.PostAsJsonAsync("/api/suppliers",body)).EnsureSuccessStatusCode();Check(true,"Matching names are permitted after review");
        foreach(var (field,value) in new (string,JsonNode?)[]{("supplierName",JsonValue.Create("x")),("supplierName",JsonValue.Create("12345")),("countryCode",JsonValue.Create("ZZ")),("supplierType",JsonValue.Create("UNKNOWN")),("mobileNumber",JsonValue.Create("123")),("alternateMobile",JsonValue.Create("+919876543210")),("email",JsonValue.Create("invalid")),("postalCode",JsonValue.Create("123")),("gstNumber",JsonValue.Create("INVALID")),("state",null),("address",JsonValue.Create(new string('x',501))),("notes",JsonValue.Create(new string('x',1001))),("paymentTermsDays",JsonValue.Create(366))}){
            var invalid=(JsonObject)body.DeepClone();invalid[field]=value;Check((int)(await client.PostAsJsonAsync("/api/suppliers",invalid)).StatusCode==400,"Reject invalid "+field);
        }
        var foreign=new{supplierName="Overseas supplier",countryCode="US",mobileNumber="2125550123",postalCode="10001",paymentTermsDays=0};Check((int)(await client.PostAsJsonAsync("/api/suppliers",foreign)).StatusCode==400,"New international supplier rejected");
        Check(options!["indianStates"]!.AsArray().Any(x=>x!.GetValue<string>()=="Karnataka"), "Supplier address suggestions reuse Indian state reference data");
        foreach(var (line1,line2) in new (string?,string?)[]{("  12 Market Road  "," First floor "),("12 Market Road",null),(null,null),(null,"Near station")})
        {
            var response=await client.PostAsJsonAsync("/api/suppliers",new{supplierName="Structured address supplier",useStructuredAddress=true,addressLine1=line1,addressLine2=line2,city="Bengaluru",state="Karnataka",postalCode="560001",supplierCode="FORGED"});
            response.EnsureSuccessStatusCode();var savedAddress=(await response.Content.ReadFromJsonAsync<JsonObject>())!;var addressId=savedAddress["supplierId"]!.GetValue<int>();
            var expected=StockFlow.Application.Common.SupplierRules.Clean(StockFlow.Application.Common.PostalAddress.Format(line1,line2));
            Check(savedAddress["address"]?.GetValue<string>()==expected && savedAddress["useStructuredAddress"]!.GetValue<bool>(), "Structured optional lines round-trip with compatible combined address");
            Check(savedAddress["supplierCode"]?.GetValue<string>()!="FORGED", "Create payload cannot set system-generated supplier code");
            var reopened=(await client.GetFromJsonAsync<JsonObject>("/api/suppliers/"+addressId))!;
            Check(reopened["addressLine2"]?.GetValue<string>()==line2?.Trim(), "Reopening supplier retains explicitly entered second line");
            (await client.PutAsJsonAsync("/api/suppliers/"+addressId,new{supplierName="Edited address supplier",isActive=false,addressLine1="New street",useStructuredAddress=true})).EnsureSuccessStatusCode();
            reopened=(await client.GetFromJsonAsync<JsonObject>("/api/suppliers/"+addressId))!;
            Check(reopened["address"]!.GetValue<string>()=="New street" && !reopened["isActive"]!.GetValue<bool>(), "Structured address edit retains explicit inactive status");
            (await client.PutAsJsonAsync("/api/suppliers/"+addressId,new{supplierName="Older client edit",address="New street",isActive=false})).EnsureSuccessStatusCode();
            reopened=(await client.GetFromJsonAsync<JsonObject>("/api/suppliers/"+addressId))!;
            Check(reopened["addressLine1"]!.GetValue<string>()=="New street" && reopened["useStructuredAddress"]!.GetValue<bool>(), "Older client edit preserves structured lines");
        }
        var legacyResponse=await client.PostAsJsonAsync("/api/suppliers",new{supplierName="Legacy address supplier",address="Old building, Floor 3\nUnsplit locality"});legacyResponse.EnsureSuccessStatusCode();var legacy=(await legacyResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        Check(legacy["address"]!.GetValue<string>()=="Old building, Floor 3\nUnsplit locality" && legacy["addressLine1"] is null && legacy["addressLine2"] is null && !legacy["useStructuredAddress"]!.GetValue<bool>(), "Legacy text is retained without guessing address lines");
        foreach(var invalid in new object[]{new{supplierName="Too long address",useStructuredAddress=true,addressLine1=new string('x',251)},new{supplierName="Wrong Indian PIN",useStructuredAddress=true,postalCode="123"}})
            Check((int)(await client.PostAsJsonAsync("/api/suppliers",invalid)).StatusCode==400, "Reject invalid optional address details");
        Check((int)(await client.PostAsJsonAsync("/api/suppliers",new{supplierName="Foreign partial address",countryCode="US",addressLine1="12 Market Street",postalCode="10001"})).StatusCode==400,"New foreign partial supplier rejected");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","SUPPLIERS:EDIT");
        Check((int)(await client.PutAsJsonAsync("/api/suppliers/"+id,new{supplierName="Denied edit",isActive=true})).StatusCode==403,"Unauthorized supplier edit denied");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        using var sales=factory.CreateClient();sales.DefaultRequestHeaders.Add("Test-Role","Sales");Check((int)(await sales.GetAsync("/api/suppliers/options")).StatusCode==403 && (int)(await sales.GetAsync("/api/suppliers/duplicates?name=test")).StatusCode==403,"Supplier helpers retain module permissions");
        Console.WriteLine("Supplier checks use fake repositories. Database migration and snapshot triggers are not executed.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
