using System.Net.Http.Json;
using StockFlow.Web.Security;
namespace StockFlow.MasterDataChecks;
internal static class CrmHttpChecks
{
    public static async Task Run(Factory factory)
    {
        Check(PermissionMiddleware.Requirement("/api/crm/enquiries/1/follow-ups","POST")==("CRM","FOLLOW_UP"),"Follow-up permission route");
        Check(PermissionMiddleware.Requirement("/api/crm/customers/1/preferences","PUT")==("CRM","EDIT"),"Preference edit permission route");
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        Directory.CreateDirectory("tests/bin/crm-ui-fixtures");
        foreach(var route in new[]{"/Crm","/Crm/Enquiries"}){var response=await client.GetAsync(route);Check(response.IsSuccessStatusCode,"CRM Razor renders "+route);await File.WriteAllTextAsync("tests/bin/crm-ui-fixtures/"+(route.EndsWith("Enquiries")?"enquiries":"customers")+".html",await response.Content.ReadAsStringAsync());}
        client.DefaultRequestHeaders.Add("Test-Deny","CRM");Check((int)(await client.GetAsync("/api/crm/dashboard")).StatusCode==403,"Denied CRM viewer cannot read dashboard");
        client.DefaultRequestHeaders.Remove("Test-Deny");Check((int)(await client.PostAsJsonAsync("/api/crm/enquiries",new{})).StatusCode==400,"Enquiry save requires CSRF token");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","CRM:FOLLOW_UP");Check((int)(await client.PostAsJsonAsync("/api/crm/enquiries/1/follow-ups",new{})).StatusCode==403,"View permission does not authorize follow-up writes");
        Console.WriteLine("CRM HTTP permission, anti-forgery and Razor checks passed.");
    }
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
}
