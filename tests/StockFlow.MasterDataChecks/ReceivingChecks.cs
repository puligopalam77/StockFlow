using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;

static class ReceivingChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok,string msg){if(!ok)throw new Exception("FAIL: "+msg);Console.WriteLine("PASS: "+msg);}
        using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        var token=await admin.GetFromJsonAsync<JsonObject>("/api/antiforgery");admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var html=await admin.GetStringAsync("/Receiving");Check(html.Contains("intake-registration")&&html.Contains("intake-kind")&&html.Contains("Advanced: orders"),"receiving page contains registration, opening stock and advanced deliveries");
        admin.DefaultRequestHeaders.Add("Test-Deny","PURCHASES");var receivingOnly=await admin.GetStringAsync("/Receiving");Check(!receivingOnly.Contains("Advanced: orders")&&receivingOnly.Contains("data-can-purchases=\"false\""),"receiving-only staff are not offered purchase screens");admin.DefaultRequestHeaders.Remove("Test-Deny");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;var output=Path.Combine(root!.FullName,"tests/bin/receiving-ui-fixtures");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"Receiving.html"),html);
        StockIntakeRequest Request()=>new(){RequestId=Guid.NewGuid(),ReceivedDate=new DateTime(2026,10,3),SupplierId=1,Items=[new(){ProductId=1,VariantId=1,Quantity=2,PurchasePrice=10}]};
        var r=Request();Check((int)(await admin.PostAsJsonAsync("/api/receiving",r)).StatusCode==200,"receipt uses receive permission without purchase approval");
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_INFLOW:RECEIVE");Check((int)(await admin.PostAsJsonAsync("/api/receiving",r)).StatusCode==403,"receiving action permission enforced");admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        r.SupplierId=null;Check((int)(await admin.PostAsJsonAsync("/api/receiving/opening",r)).StatusCode==200,"opening stock works without supplier");
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_ADJUSTMENT:ADJUST");Check((int)(await admin.PostAsJsonAsync("/api/receiving/opening",r)).StatusCode==403,"opening stock requires adjustment permission");admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        r.LocationId=999;Check((int)(await admin.PostAsJsonAsync("/api/receiving/opening",r)).StatusCode==400,"receipt cannot target another location");r.LocationId=null;
        r.RequestId=Guid.Empty;Check((int)(await admin.PostAsJsonAsync("/api/receiving/opening",r)).StatusCode==400,"receipt requires retry identifier");
        r=Request();r.Items[0].Quantity=0;Check((int)(await admin.PostAsJsonAsync("/api/receiving",r)).StatusCode==400,"invalid receipt quantity rejected");
        r=Request();r.Items[0].SerialNumbers=["S1","s1"];Check((int)(await admin.PostAsJsonAsync("/api/receiving",r)).StatusCode==400,"duplicate serials rejected");
        r=Request();Check((int)(await admin.PostAsJsonAsync("/api/receiving/opening",r)).StatusCode==400,"opening stock rejects supplier purchases");
        var identifiers=new {variantId=1,requestId=Guid.Empty,count=1,locationId=1,businessProfileCode="ELECTRICAL"};
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","PURCHASES:APPROVE,STOCK_INFLOW:RECEIVE,STOCK_ADJUSTMENT:ADJUST");
        Check((int)(await admin.PostAsJsonAsync("/api/receiving/unit-identifiers",identifiers)).StatusCode==403,"unit allocation requires receiving, approval or opening-stock permission");admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","PURCHASES:APPROVE,STOCK_ADJUSTMENT:ADJUST");
        Check((int)(await admin.PostAsJsonAsync("/api/receiving/unit-identifiers",identifiers)).StatusCode==400,"receiving-only staff reach allocation validation without purchase approval");admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((int)(await admin.PostAsJsonAsync("/api/receiving",r)).StatusCode==400,"receipt requires CSRF token");
        Check((int)(await admin.PostAsJsonAsync("/api/receiving/unit-identifiers",identifiers)).StatusCode==400,"unit allocation requires CSRF token");
        Console.WriteLine("Receiving application checks passed; no database accessed.");
    }
}
sealed class FakeStockIntakeRepository:IStockIntakeRepository
{
    public Task<IReadOnlyList<StockLabelResponse>?> LabelsAsync(long intakeId,string? printAction=null,int userId=0) => Task.FromResult<IReadOnlyList<StockLabelResponse>?>(intakeId==1 ? new List<StockLabelResponse>{new(){VariantId=1,ProductName="Shirt / Blue M",Sku="BLUE-M",Quantity=3,ExistingBarcode="0000123",InternalBarcode="SFV00000001",SellingPrice=100},new(){VariantId=2,UnitId=1,ProductName="Battery",Sku="BAT",SerialNumber="BAT-01",Quantity=1,ExistingBarcode="BAT-01",SellingPrice=50}} : null);
    public Task<StockIntakeResult> PostAsync(StockIntakeRequest request,bool opening,int userId){StockIntakeRules.Validate(request,opening);return Task.FromResult(new StockIntakeResult {IntakeId=1,PurchaseId=opening?null:1,Kind=opening?"OPENING_STOCK":"RECEIPT"});}
}
