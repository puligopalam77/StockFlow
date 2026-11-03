using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;
internal static class BarcodeManagementChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        const string url="/api/labels/management/lots";
        Check((await client.GetFromJsonAsync<JsonArray>(url))!.Count==1,"management lists barcode status by lot");
        Check((int)(await client.GetAsync(url+"/receipt/99")).StatusCode==404,"unknown lot is rejected");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:GENERATE");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/prepare",new{keys=new[]{"3:1"}})).StatusCode==403,"preview requires generation permission for missing codes");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/prepare",new{keys=new[]{"2:1"}})).StatusCode==200,"supplied codes preview without generation permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/prepare",new{keys=new[]{"not-owned"}})).StatusCode==400,"preview rejects a selection from another lot");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/prepare",new{keys=new[]{"3:1"}})).StatusCode==200,"preview saves the selected missing code");
        var request=new{requestId=Guid.NewGuid(),action="PRINT",format="sheet",items=new[]{new{key="3:1",barcode="SFR3X1",copies=2}}};
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/print",request)).StatusCode==200,"print request saves selected identifiers and copies");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/print",request)).StatusCode==200,"print request retry succeeds");
        var doc=await client.GetFromJsonAsync<JsonObject>(url+"/receipt/2");Check(doc!["history"]!.AsArray().Count==1,"print retry keeps one history entry");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/print",new{requestId=Guid.NewGuid(),action="PRINT",format="sheet",items=new[]{new{key="3:1",barcode="SFR3X1",copies=0}}})).StatusCode==400,"zero label copies are rejected");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:REPRINT");
        Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/reprint",new{requestId=Guid.NewGuid(),action="REPRINT",format="sheet",request.items})).StatusCode==403,"reprint requires its own permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var html=await client.GetStringAsync("/Labels?receiptId=2");Check(html.Contains("barcode-preview-dialog")&&html.Contains("barcode-history-rows")&&html.Contains("barcode-reprint-dialog"),"compact detail screen contains preview, reprint and history dialogs");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/barcode-management-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Labels.html"),html);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((int)(await client.PostAsJsonAsync(url+"/receipt/2/print",request)).StatusCode==400,"print requests require CSRF protection");
    }
}
