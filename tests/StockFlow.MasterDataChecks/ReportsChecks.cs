using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Reports;
using System.Net.Http.Json;
namespace StockFlow.MasterDataChecks;
internal static class ReportsChecks
{
    public static async Task Run(Factory factory)
    {
        using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});
        admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        var landing=await admin.GetAsync("/Reports");
        if((int)landing.StatusCode!=302 || !landing.Headers.Location!.OriginalString.StartsWith("/Reports/Activity"))throw new Exception("Reports must open Shop summary instead of report cards.");
        var html=await admin.GetStringAsync("/Reports/Activity?fromReports=1");
        if(!html.Contains("data-nav-group=\"Reports\"")||!html.Contains("Completed repairs")||!html.Contains("sf-report-more"))throw new Exception("Report submenus did not render.");
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/reports-ui-fixtures");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"Reports.html"),html);
        foreach(var route in new[]{"/Sales?fromReports=1","/Inventory?fromReports=1","/Service?status=DELIVERED&fromReports=1"})
        {
            var detail=await admin.GetStringAsync(route);
            var navStart=detail.IndexOf("<nav class=\"sf-nav sf-shop-nav\"");
            var nav=detail[navStart..detail.IndexOf("</nav>",navStart)];
            if(System.Text.RegularExpressions.Regex.Matches(nav,"aria-current=\"page\"").Count!=1)throw new Exception("Report navigation must have exactly one active submenu: "+route);
            await File.WriteAllTextAsync(Path.Combine(output,route.Split('?')[0].Trim('/')+".html"),detail);
        }
        html=await admin.GetStringAsync("/Reports/Activity");await File.WriteAllTextAsync(Path.Combine(output,"Activity.html"),html);
        if(!html.Contains("activity-period")||!html.Contains("This Week")||!html.Contains("Custom dates"))throw new Exception("Period presets missing");
        admin.DefaultRequestHeaders.Add("Test-Deny","SALES");
        if((int)(await admin.GetAsync("/api/operations/reports/product-sales?from=2026-10-01&to=2026-10-03")).StatusCode!=403)throw new Exception("Product profit report exposed sales to a denied source reader");
        html=await admin.GetStringAsync("/Reports/Activity");
        if(html.Contains("id=\"financial-report\""))throw new Exception("Financial report visible without sales access");
        await File.WriteAllTextAsync(Path.Combine(output,"ActivityStockOnly.html"),html);admin.DefaultRequestHeaders.Remove("Test-Deny");
        foreach(var query in new[]{"", "?from=2026-10-03&to=2026-10-01", "?from=2026-10-01", "?from=0001-01-01&to=2026-10-03"})
            if((int)(await admin.GetAsync("/api/reports/activity"+query)).StatusCode!=400)throw new Exception("Invalid report dates accepted");
        admin.DefaultRequestHeaders.Add("Test-Deny","INVENTORY,SALES");
        if((int)(await admin.GetAsync("/api/reports/activity?from=2026-10-01&to=2026-10-03")).StatusCode!=403)throw new Exception("Report exposes denied modules");
        admin.DefaultRequestHeaders.Remove("Test-Deny");
        using var app=factory.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IReportingRepository>();s.AddScoped<IReportingRepository,FakePeriodReports>();s.RemoveAll<IBranchRepository>();s.AddSingleton<IBranchRepository>(new FakeBranchRepository{State=new(){IsActive=true}});}));
        using var staff=app.CreateClient(new(){AllowAutoRedirect=false});staff.DefaultRequestHeaders.Add("Test-Role","Sales");
        html=await staff.GetStringAsync("/Reports/Activity");if(html.Contains("id=\"business-location\""))throw new Exception("Staff report contains store switching");
        await File.WriteAllTextAsync(Path.Combine(output,"ActivityStaff.html"),html);
        var response=await staff.GetFromJsonAsync<ActivityReport>("/api/reports/activity?from=2026-03-01&to=2026-03-31&locationId=99&storeId=99");
        if(response?.Scope!="1:1")throw new Exception("Report failed to use assigned context");
        staff.DefaultRequestHeaders.Add("Test-Deny","SALES");response=await staff.GetFromJsonAsync<ActivityReport>("/api/reports/activity?from=2026-03-01&to=2026-03-31");
        if(response!.CanSeeSales||!response.CanSeeStock)throw new Exception("Denied report data sent to repository");
        admin.DefaultRequestHeaders.Add("Test-Deny","REPORTS");
        if((int)(await admin.GetAsync("/api/reports/activity?from=2026-10-01&to=2026-10-03")).StatusCode!=403)throw new Exception("Reports permission missing");
        admin.DefaultRequestHeaders.Remove("Test-Deny");
        admin.DefaultRequestHeaders.Add("Test-Deny","SALES,WARRANTY_CLAIMS");
        var restricted=await admin.GetStringAsync("/Reports/Activity?fromReports=1");
        if(restricted.Contains("href=\"/Sales?fromReports=1\"")||restricted.Contains("href=\"/Warranty/Claims?fromReports=1\""))throw new Exception("Restricted report submenus exposed.");
        Console.WriteLine("PASS: Reports renders and hides unauthorized destinations.");
    }
}
sealed class FakePeriodReports(BusinessScope scope):IReportingRepository
{
 public Task<ActivityReport> ActivityAsync(DateTime from,DateTime to,bool stock,bool sales)=>Task.FromResult(new ActivityReport{From=from,To=to,CanSeeStock=stock,CanSeeSales=sales,Scope=$"{scope.StoreId}:{scope.LocationId}"});
}
