using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

static class StoreAssignmentChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name); }
        var branches=new FakeBranchRepository { State=new(){IsActive=true}, Locations=[
            new(){LocationId=1,StoreId=1,StoreName="Main store",LocationName="Counter",LocationType="SALES_FLOOR",IsActive=true,IsDefault=true},
            new(){LocationId=2,StoreId=1,LocationType="BACKROOM",IsActive=true,IsDefault=true},
            new(){LocationId=3,StoreId=2,LocationType="SALES_FLOOR",IsActive=true}
        ]};
        Check(StoreContextRules.DefaultLocation(branches.Locations,false)?.LocationId==1,"staff login selects default store sales floor");
        Check(StoreContextRules.DefaultLocation(branches.Locations.AsEnumerable().Reverse(),false)?.LocationId==1,"default assignment does not depend on query order");
        Check(StoreContextRules.DefaultLocation(branches.Locations.Where(l=>l.LocationType!="SALES_FLOOR"),false) is null,"retail account without sales floor cannot silently use backroom");
        Check(StoreContextRules.DefaultLocation([new(){LocationId=9,LocationType="WAREHOUSE",IsActive=true}],false)?.LocationId==9,"warehouse-only staff retain assigned warehouse");
        using var app=factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{
            services.RemoveAll<IBranchRepository>();services.AddSingleton<IBranchRepository>(branches);
        }));
        using var staff=app.CreateClient(new(){AllowAutoRedirect=false});staff.DefaultRequestHeaders.Add("Test-Role","Sales");
        var token=await staff.GetFromJsonAsync<JsonObject>("/api/antiforgery");staff.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var html=await staff.GetStringAsync("/Sales/Create");
        Check(html.Contains("id=\"assigned-store\"")&&!html.Contains("id=\"business-location\""),"cashier sees assigned store without selector");
        var context=await staff.GetFromJsonAsync<JsonObject>("/api/branches/context");
        Check(context!["canReviewStores"]!.GetValue<bool>()==false&&context["locations"]!.AsArray().Count==1,"staff context exposes only pinned location");
        Check((int)(await staff.PostAsJsonAsync("/api/sales",new CreateSaleRequest {
            RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,Quantity=1,SellingPrice=10}],
            Payments=[new(){PaymentMode="CASH",Amount=11.80m}]
        })).StatusCode==201,"cashier can complete sale without selecting or submitting a store");
        staff.DefaultRequestHeaders.Add("X-StockFlow-Location","3");
        Check((int)(await staff.PostAsJsonAsync("/api/sales",new{})).StatusCode==409,"foreign location request stamp is rejected before posting");
        staff.DefaultRequestHeaders.Remove("X-StockFlow-Location");
        foreach(var target in new[]{new{locationId=3,storeId=2},new{locationId=2,storeId=1},new{locationId=0,storeId=0},new{locationId=1,storeId=1}})
            Check((int)(await staff.PostAsJsonAsync("/api/branches/context",target)).StatusCode==403,"staff context switching rejected: "+target.locationId);
        staff.DefaultRequestHeaders.Remove("Test-Role");staff.DefaultRequestHeaders.Add("Test-Role","Admin");
        Check((int)(await staff.PostAsJsonAsync("/api/branches/context",new{locationId=3,storeId=2})).StatusCode==403,"claimed Admin role cannot override current database staff state");
        staff.DefaultRequestHeaders.Remove("Test-Role");staff.DefaultRequestHeaders.Add("Test-Role","Sales");
        staff.DefaultRequestHeaders.Add("Test-Location","3");staff.DefaultRequestHeaders.Add("Test-Store","2");
        Check((int)(await staff.GetAsync("/api/sales")).StatusCode==403,"even assigned second-store session is rejected for staff");
        staff.DefaultRequestHeaders.Remove("Test-Location");staff.DefaultRequestHeaders.Remove("Test-Store");
        staff.DefaultRequestHeaders.Add("Test-Location","0");staff.DefaultRequestHeaders.Add("Test-Store","0");
        Check((int)(await staff.GetAsync("/api/sales")).StatusCode==403,"staff consolidated scope rejected");
        staff.DefaultRequestHeaders.Remove("Test-Location");staff.DefaultRequestHeaders.Remove("Test-Store");
        branches.Locations[0].IsDefault=false;branches.Locations[1].IsDefault=false;branches.Locations[2].IsDefault=true;
        Check((int)(await staff.GetAsync("/api/sales")).StatusCode==403,"changed default assignment invalidates old store session");
        branches.Locations[0].IsDefault=true;branches.Locations[1].IsDefault=true;branches.Locations[2].IsDefault=false;
        branches.State.IsAdmin=true;
        using var admin=app.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        var adminToken=await admin.GetFromJsonAsync<JsonObject>("/api/antiforgery");admin.DefaultRequestHeaders.Add("X-CSRF-TOKEN",adminToken!["requestToken"]!.GetValue<string>());
        Check((await admin.GetStringAsync("/Sales/Create")).Contains("id=\"business-location\""),"database Admin retains store selector without owner flag");
        Check((int)(await admin.PostAsJsonAsync("/api/branches/context",new{locationId=3,storeId=2})).StatusCode==204,"administrator can switch to authorized store");
        Check((int)(await admin.PostAsJsonAsync("/api/branches/context",new{locationId=999,storeId=2})).StatusCode==403,"administrator cannot select unassigned location");
        admin.DefaultRequestHeaders.Add("Test-Location","0");admin.DefaultRequestHeaders.Add("Test-Store","0");
        Check((int)(await admin.GetAsync("/Reports/Stores")).StatusCode==200,"administrator can review consolidated reports");
        Check((int)(await admin.PostAsJsonAsync("/api/sales",new{})).StatusCode==400,"consolidated administration view cannot post sales");
        Console.WriteLine("Store assignment checks passed. No application database was accessed.");
    }
}
