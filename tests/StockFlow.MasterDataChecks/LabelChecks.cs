using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace StockFlow.MasterDataChecks;

static class LabelChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var html=await client.GetStringAsync("/Labels?intakeId=1");Check(html.Contains("barcode-print")&&html.Contains("barcode-reprint-tab")&&html.Contains("Thermal printer"),"label page offers optional printing, reprinting and thermal layout");
        var variantHtml=await client.GetStringAsync("/Products/Variants");
        Check(html.Contains("barcode-lot-rows")&&html.Contains("href=\"/Labels\""),"barcode page offers received lots under purchase navigation");
        Check((await client.GetFromJsonAsync<JsonArray>("/api/labels/lots"))!.Count==2,"barcode lots include guided and purchase receipt records");
        Check((int)(await client.GetAsync("/api/labels/lots/receipt/99")).StatusCode==404,"inaccessible purchase lot returns not found");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:GENERATE");
        Check((int)(await client.PostAsJsonAsync("/api/labels/lots/receipt/2/generate",new{})).StatusCode==403,"purchase lot generation requires generate permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        var receiptLabels=await (await client.PostAsJsonAsync("/api/labels/lots/receipt/2/generate",new{})).Content.ReadFromJsonAsync<JsonArray>();
        var receiptAgain=await client.GetFromJsonAsync<JsonArray>("/api/labels/lots/receipt/2");
        Check(receiptLabels!.Count==2&&receiptLabels.ToJsonString()==receiptAgain!.ToJsonString(),"purchase receipt labels stay unchanged for reprinting");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:PRINT");
        Check((int)(await client.PostAsJsonAsync("/api/labels/lots/receipt/2/print",new{})).StatusCode==403,"purchase lot print permission is enforced");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:REPRINT");
        Check((int)(await client.PostAsJsonAsync("/api/labels/lots/receipt/2/reprint",new{})).StatusCode==403,"purchase lot reprint permission is enforced");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check(variantHtml.Contains("label-select-all")&&variantHtml.Contains("label-item-list"),"variant label dialog offers individual and all-item selection");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;var output=Path.Combine(root!.FullName,"tests/bin/receiving-ui-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Labels.html"),html);
        var variantOutput=Path.Combine(root.FullName,"tests/bin/variant-ui-fixtures");Directory.CreateDirectory(variantOutput);await File.WriteAllTextAsync(Path.Combine(variantOutput,"Products-Variants.html"),variantHtml);
        Check((int)(await client.GetAsync("/api/receiving/1/labels")).StatusCode==200,"saved receipt exposes label manifest");
        Check((int)(await client.GetAsync("/api/receiving/99/labels")).StatusCode==404,"missing or inaccessible receipt labels return not found");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:PRINT");Check((int)(await client.PostAsJsonAsync("/api/receiving/1/labels/print",new{})).StatusCode==403,"printing requires barcode print permission");html=await client.GetStringAsync("/Labels");Check(html.Contains("data-print=\"false\"")&&html.Contains("data-reprint=\"true\""),"print controls follow individual permissions");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:REPRINT");Check((int)(await client.PostAsJsonAsync("/api/receiving/1/labels/reprint",new{})).StatusCode==403,"reprinting requires barcode reprint permission");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check((int)(await client.PostAsJsonAsync("/api/receiving/1/labels/print",new{})).StatusCode==200,"permitted printing reads existing identifiers");
        Check((int)(await client.PostAsJsonAsync("/api/receiving/1/labels/reprint",new{})).StatusCode==200,"permitted reprinting reads existing identifiers");
        var unique=await client.PostAsJsonAsync("/api/products/variants/1/item-labels/generate",new{});
        Check((int)unique.StatusCode==200,"unique item labels use barcode generate permission");
        var codes=await unique.Content.ReadFromJsonAsync<JsonArray>();
        Check(codes!.Count==3&&codes.Select(row=>row!["internalBarcode"]!.GetValue<string>()).Distinct().Count()==3,"three received items have distinct stored barcodes");
        var again=await (await client.PostAsJsonAsync("/api/products/variants/1/item-labels/generate",new{})).Content.ReadFromJsonAsync<JsonArray>();
        Check(codes.ToJsonString()==again!.ToJsonString(),"preparing labels again retains original item identities");
        var read=await client.GetFromJsonAsync<JsonArray>("/api/products/variants/1/item-labels");
        Check(codes.ToJsonString()==read!.ToJsonString(),"reprint lookup returns saved labels without generation");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:GENERATE");
        Check((int)(await client.PostAsJsonAsync("/api/products/variants/1/item-labels/generate",new{})).StatusCode==403,"unique label generation enforces barcode generate permission");
        Check((int)(await client.GetAsync("/api/products/variants/1/item-labels")).StatusCode==200,"label viewing does not require generating permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        Check((int)(await client.PostAsJsonAsync("/api/products/variants/1/print",new{existingBarcode="EAN123",copies=501})).StatusCode==400,"manufacturer label printing rejects oversized batches");
        Check((int)(await client.PostAsJsonAsync("/api/products/variants/1/print",new{existingBarcode="EAN123",copies=1,labelIds=new long[]{1}})).StatusCode==400,"manufacturer and unique label requests cannot be mixed");
        Check((int)(await client.PostAsJsonAsync("/api/products/variants/1/print",new{existingBarcode="unassigned",copies=1})).StatusCode==400,"unassigned manufacturer barcode cannot be printed");
        Check((int)(await client.PostAsJsonAsync("/api/products/variants/1/reprint",new{labelIds=new long[]{1,1}})).StatusCode==400,"reprint rejects duplicate selected label identities");
        client.DefaultRequestHeaders.Add("Test-Deny-Action","BARCODES:REPRINT");
        Check((int)(await client.PostAsJsonAsync("/api/products/variants/1/reprint",new{labelIds=new long[]{1}})).StatusCode==403,"individual item reprint requires reprint permission");
        client.DefaultRequestHeaders.Remove("Test-Deny-Action");
        client.DefaultRequestHeaders.Add("Test-Deny","BARCODES");Check((int)(await client.GetAsync("/api/receiving/1/labels")).StatusCode==403,"label data requires barcode view permission");Check((int)(await client.GetAsync("/api/labels/lots")).StatusCode==403,"lot list requires barcode view permission");var receiving=await client.GetStringAsync("/Receiving");Check(!receiving.Contains("id=\"intake-label-link\""),"receiving hides optional label action without access");client.DefaultRequestHeaders.Remove("Test-Deny");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((int)(await client.PostAsJsonAsync("/api/receiving/1/labels/print",new{})).StatusCode==400,"label print action requires CSRF protection");
    }
}
