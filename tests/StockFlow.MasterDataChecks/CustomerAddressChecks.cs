using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace StockFlow.MasterDataChecks;
internal static class CustomerAddressChecks
{
    public static async Task Run(Factory factory)
    {
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        (await client.PostAsJsonAsync("/api/customers",new{customerName="Name only customer"})).EnsureSuccessStatusCode();
        var details=new{addressLine1="20 Retail Street",addressLine2="Floor 2",state="Karnataka",district="Bengaluru Urban",city="Bengaluru",country="India",postalCode="560001"};
        var response=await client.PostAsJsonAsync("/api/customers",new{customerName="Structured customer",useStructuredAddress=true,addressDetails=details});response.EnsureSuccessStatusCode();var row=(await response.Content.ReadFromJsonAsync<JsonObject>())!;var id=row["customerId"]!.GetValue<int>();
        Check(row["addressDetails"]!["district"]!.GetValue<string>()=="Bengaluru Urban"&&row["address"]!.GetValue<string>().Contains("20 Retail Street"),"Customer address components and printable text round-trip through existing API");
        (await client.PutAsJsonAsync("/api/customers/"+id,new{customerName="Older client edit",address=row["address"]!.GetValue<string>(),isActive=false})).EnsureSuccessStatusCode();
        row=(await client.GetFromJsonAsync<JsonObject>("/api/customers/"+id))!;
        Check(row["addressDetails"]!["addressLine2"]!.GetValue<string>()=="Floor 2"&&!row["isActive"]!.GetValue<bool>(),"Older customer contract preserves components and explicit inactive status");
        (await client.PutAsJsonAsync("/api/customers/"+id,new{customerName="Cleared address",useStructuredAddress=true,isActive=false})).EnsureSuccessStatusCode();row=(await client.GetFromJsonAsync<JsonObject>("/api/customers/"+id))!;
        Check(row["address"] is null&&row["addressDetails"] is null,"Optional customer address can be explicitly cleared");
        Check((int)(await client.PostAsJsonAsync("/api/customers",new{customerName="Invalid PIN",addressDetails=new{postalCode="123",country="India"}})).StatusCode==400,"Customer API rejects invalid supplied Indian PIN");
        Check((int)(await client.PostAsJsonAsync("/api/customers",new{customerName="Too long line",addressDetails=new{addressLine1=new string('x',251)}})).StatusCode==400,"Customer API validates supplied address line length");
        Check((int)(await client.PostAsJsonAsync("/api/customers",new{customerName="International customer",addressDetails=new{addressLine1="Market street",country="United States",postalCode="10001",city="New York",state="New York"}})).StatusCode==400,"New international address is rejected by country policy");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","CUSTOMERS:CREATE");
        Check((int)(await client.PostAsJsonAsync("/api/customers",new{customerName="Denied customer",addressDetails=details})).StatusCode==403,"Structured customer creation retains existing permissions");
        Console.WriteLine("Customer address HTTP checks use fake repositories; isolated PostgreSQL checks verify persistence separately.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
