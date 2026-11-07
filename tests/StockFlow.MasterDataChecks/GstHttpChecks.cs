using System.Net.Http.Json;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

internal static class GstHttpChecks
{
    public static async Task Run(Factory factory)
    {
        Check(PermissionMiddleware.Requirement("/api/gst/settings/businesses","POST")==("GST_SETTINGS","EDIT"),"GST writes require manage settings");
        Check(PermissionMiddleware.Requirement("/api/gst/settings/catalog/export","GET")==("GST_SETTINGS","EXPORT_TAX"),"Catalog export uses its explicit permission");
        Check(PermissionMiddleware.Requirement("/api/gst/settings/catalog/import","POST")==("GST_SETTINGS","IMPORT_TAX"),"Catalog import uses its explicit permission");
        Check(PermissionMiddleware.Requirement("/api/gst/settings/catalog/template","GET")==("GST_SETTINGS","IMPORT_TAX"),"Prefilled template requires import authorization");
        Check(PermissionMiddleware.Requirement("/api/gst/settings/catalog/rules/review","POST")==("GST_SETTINGS","APPROVE_TAX"),"Classification approval uses its explicit permission");
        Check(PermissionMiddleware.Requirement("/api/gst/settings/catalog/billing-defaults","POST")==("COMPANY_SETTINGS","EDIT"),"Billing default reuses Business & Billing authorization");
        Check(PermissionMiddleware.Requirement("/api/gst/export","GET")==("GST_REPORTS","EXPORT"),"Tax export requires explicit export permission");
        Check(PermissionMiddleware.Requirement("/api/gst/review","POST")==("GST_REPORTS","REVIEW"),"Review has a distinct permission");
        var output=Path.GetFullPath("tests/bin/gst-ui-fixtures");Directory.CreateDirectory(output);
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var route in new[]{"/Admin/Gst","/Reports/Gst"})
        {
            var response=await client.GetAsync(route);Check(response.IsSuccessStatusCode,"Razor screen renders: "+route);
            await File.WriteAllTextAsync(Path.Combine(output,route.Contains("Admin")?"settings.html":"reports.html"),await response.Content.ReadAsStringAsync());
        }
        var settingsHtml=await File.ReadAllTextAsync(Path.Combine(output,"settings.html"));
        var settingsBody=settingsHtml.Split("id=\"gst-settings\"")[1];
        Check(!settingsBody.Contains("Integrations: E-Way Bill provider readiness"),"GST setup no longer contains the misplaced provider button");
        var providerHtml=await client.GetStringAsync("/EWayBills/Provider");
        await File.WriteAllTextAsync(Path.Combine(output,"provider.html"),providerHtml);
        var nav=System.Text.RegularExpressions.Regex.Match(providerHtml,"<nav class=\"sf-nav[\\s\\S]*?</nav>").Value;
        Check(System.Text.RegularExpressions.Regex.Matches(nav,"href=\"/EWayBills/Provider\"").Count==1,"Single provider sidebar entry");
        Check(nav.Contains("data-nav-group=\"Integrations\"")&&nav.Contains("aria-current=\"page\""),"Existing provider route selects Administration Integrations");
        Check(System.Text.RegularExpressions.Regex.IsMatch(nav,"<details[^>]*open[^>]*data-nav-group=\"Setup\""),"Provider page expands Administration");
        Check(providerHtml.Contains("<h1>E-Way Bill Provider</h1>"),"Provider page has correct heading without fake credential fields");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","EWAY_BILLS:CONFIGURE_PROVIDER");
        Check((await client.GetAsync("/EWayBills/Provider")).Headers.Location?.OriginalString=="/Account/AccessDenied","Provider direct Razor route requires its own action");
        Check((int)(await client.GetAsync("/api/ewaybills/provider")).StatusCode==403,"Provider API denied before repository when action is absent");
        Check((await client.GetAsync("/Admin/Gst")).IsSuccessStatusCode,"GST setup does not depend on provider configuration permission");
        var deniedHtml=await client.GetStringAsync("/Admin/Gst");
        Check(!deniedHtml.Contains("data-nav-group=\"Integrations\""),"No empty Integrations group without provider action");
        Check((await client.GetAsync("/EWayBills")).IsSuccessStatusCode,"Operational preparations retain separate view authorization");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny","GST_SETTINGS,GST_REPORTS");
        Check((int)(await client.GetAsync("/api/gst/registrations")).StatusCode==403,"Denied GST viewer cannot reach report repository");
        Check((int)(await client.PostAsJsonAsync("/api/gst/settings/mappings",new{})).StatusCode==403,"Denied manager cannot submit shop mappings");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        Check((int)(await client.PostAsJsonAsync("/api/gst/documents",new{})).StatusCode==400,"GST source entry requires anti-forgery token");
        Console.WriteLine("GST HTTP permissions, CSRF and Razor fixture checks passed; no database used.");
    }
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
}
