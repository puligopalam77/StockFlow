using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

static class InventoryControlChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/inventory-control-ui-fixtures");Directory.CreateDirectory(output);
        using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var (url,file) in new[]{("/Inventory","Inventory"),("/Inventory/LowStock","LowStock"),("/Transfers","Transfers")})
            await File.WriteAllTextAsync(Path.Combine(output,file+".html"),await admin.GetStringAsync(url));
        var html=await admin.GetStringAsync("/Inventory");
        Check(html.Contains("id=\"adjustment\"")&&html.Contains("id=\"adjust-units\"")&&!html.Contains("id=\"adjust-unit-ids\""),"authorized adjustment uses serial selection");
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","INVENTORY:APPROVE");
        Check((await admin.GetStringAsync("/Inventory")).Contains("id=\"adjustment\""),"adjustments do not depend on inventory approval");
        admin.DefaultRequestHeaders.Remove("Test-Deny-Action");admin.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_ADJUSTMENT:ADJUST");
        html=await admin.GetStringAsync("/Inventory");Check(!html.Contains("id=\"adjustment\""),"adjustment permission hides form");
        await File.WriteAllTextAsync(Path.Combine(output,"InventoryReader.html"),html);
        var token=await admin.GetFromJsonAsync<JsonObject>("/api/antiforgery");admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        Check((int)(await admin.PostAsJsonAsync("/api/inventory/adjustments",new{})).StatusCode==403,"unauthorized adjustment rejected before database");
        admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        foreach(var action in new[]{"DISPATCH","RECEIVE","CANCEL"})
        {
            admin.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_TRANSFER:"+action);
            html=await admin.GetStringAsync("/Transfers");
            Check(!html.Contains("data-action=\""+action.ToLowerInvariant()+"\""),"transfer button respects "+action);
            Check((int)(await admin.PostAsJsonAsync("/api/transfers/1/"+action.ToLowerInvariant(),new{})).StatusCode==403,"transfer API respects "+action);
            if(action=="DISPATCH")Check((int)(await admin.GetAsync("/api/transfers/1/dispatch-units?lineId=1")).StatusCode==403,"dispatch serial lookup requires dispatch permission");
            admin.DefaultRequestHeaders.Remove("Test-Deny-Action");
        }
        admin.DefaultRequestHeaders.Add("Test-Deny-Action","STOCK_TRANSFER:CREATE");
        Check((int)(await admin.GetAsync("/api/transfers/variants")).StatusCode==403,"transfer catalog lookup requires create permission");
        var branches=new FakeBranchRepository{State=new(){IsActive=true}};
        using var staffApp=factory.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IBranchRepository>();s.AddSingleton<IBranchRepository>(branches);}));
        using var staff=staffApp.CreateClient(new(){AllowAutoRedirect=false});staff.DefaultRequestHeaders.Add("Test-Role","Sales");
        foreach(var page in new[]{"Inventory","Transfers"}){
            html=await staff.GetStringAsync("/"+page);Check(!html.Contains("id=\"business-location\""),"staff "+page+" has assigned store only");
            await File.WriteAllTextAsync(Path.Combine(output,page+"Staff.html"),html);
        }
        Console.WriteLine("Inventory control HTTP and permission checks passed; no database accessed.");
    }
}
