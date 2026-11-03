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
        client.DefaultRequestHeaders.Add("Test-Deny","GST_SETTINGS,GST_REPORTS");
        Check((int)(await client.GetAsync("/api/gst/registrations")).StatusCode==403,"Denied GST viewer cannot reach report repository");
        Check((int)(await client.PostAsJsonAsync("/api/gst/settings/mappings",new{})).StatusCode==403,"Denied manager cannot submit shop mappings");
        client.DefaultRequestHeaders.Remove("Test-Deny");
        Check((int)(await client.PostAsJsonAsync("/api/gst/documents",new{})).StatusCode==400,"GST source entry requires anti-forgery token");
        Console.WriteLine("GST HTTP permissions, CSRF and Razor fixture checks passed; no database used.");
    }
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
}
