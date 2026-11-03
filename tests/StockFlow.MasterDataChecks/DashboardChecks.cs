using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.DTOs.Dashboard;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
internal static class DashboardChecks
{
    public static async Task Run(Factory factory)
    {
        using var anonymous=factory.CreateClient();
        Check((int)(await anonymous.GetAsync("/api/dashboard/business-summary")).StatusCode==401,"anonymous dashboard denied");
        foreach(var role in new[]{"Admin","Sales","Service"})
        {
            using var client=factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role",role);
            foreach(var url in new[]{"/","/StockInflow","/StockOutflow","/Inventory/LowStock","/Reports","/Search?term=BAT",
                "/api/dashboard/inventory-summary","/api/dashboard/business-summary","/api/dashboard/category-stock",
                "/api/dashboard/recent-inflow","/api/dashboard/recent-outflow","/api/dashboard/recent-services","/api/dashboard/low-stock",
                "/api/dashboard/movements?incoming=false","/api/dashboard/search?term=BAT"})
                Check((await client.GetAsync(url)).IsSuccessStatusCode,role+" reads "+url);
            var html=await client.GetStringAsync("/");
            Check(html.Contains("sf-hero") && html.Contains("sf-donut") && html.Contains("Low Stock Products") && html.Contains("Recent Service Requests"),"dashboard preserved and extended");
            Check(html.Contains("id=\"recent-sales\"")==(role!="Service"),"sales panel permission");
            Check(html.Contains("Today's Sales")==(role!="Service"),"sales KPI permission");
            Check(html.Contains("Customer Outstanding Dues")== (role!="Service") && html.Contains("Overdue Payments")== (role!="Service"),"customer dues cards respect financial permissions");
            if(role!="Service") Check(html.Contains("status=COMPLETED") && html.Contains("/Sales?status=OUTSTANDING") && html.Contains("/Sales?status=OVERDUE"),"dashboard cards link to matching invoice filters");
            Check(!html.Contains("<script>alert(1)</script>") && html.Contains("&lt;script&gt;"),"dashboard encodes data");
            var summary=await client.GetFromJsonAsync<JsonObject>("/api/dashboard/business-summary");
            Check((summary!["todaySalesAmount"] is null)==(role=="Service"),"service role financial totals omitted");
            if(role=="Service") Check(summary["customerOutstandingAmount"] is null && summary["overduePaymentAmount"] is null,"restricted customer dues omitted from API");
            var inventory=await client.GetFromJsonAsync<JsonObject>("/api/dashboard/inventory-summary");
            Check((inventory!["totalSuppliers"] is null)==(role!="Admin"),"supplier count permission");
            Check((int)(await client.GetAsync("/api/dashboard/recent-sales")).StatusCode==(role=="Service"?403:200),"recent sales endpoint permission");
            Check((int)(await client.GetAsync("/Users")).StatusCode==(role=="Admin"?200:403),"users page permission");
            Check((int)(await client.GetAsync("/api/dashboard/users")).StatusCode==(role=="Admin"?200:403),"users endpoint permission");
            foreach(var (period,count) in new[]{("7days",7),("30days",30),("6months",6),("12months",12)})
            {
                var trend=await client.GetFromJsonAsync<JsonArray>("/api/dashboard/inflow-outflow-trend?period="+period);
                Check(trend!.Count==count,"period "+period);
            }
            foreach(var url in new[]{"/api/dashboard/inflow-outflow-trend?period=bad","/api/dashboard/low-stock?offset=-1",
                "/api/dashboard/movements?incoming=false&type=PURCHASE","/api/dashboard/movements?from=2026-10-01&to=2026-09-01",
                "/api/dashboard/search?term=x"})
                Check((int)(await client.GetAsync(url)).StatusCode==400,"dashboard input validation");
        }
        Console.WriteLine("PASS: dashboard roles, preserved layout, new panels, endpoints, period choices, validation and HTML encoding (fake repository).");
    }
    public static async Task Fixtures(Factory factory)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/phase13-ui-fixtures"); Directory.CreateDirectory(output);
        foreach(var role in new[]{"Admin","Service"})
        {
            using var c=factory.CreateClient(); c.DefaultRequestHeaders.Add("Test-Role",role);
            foreach(var (url,name) in new[]{("/","dashboard"),("/StockInflow","stock-receipts"),("/StockInflow/Details/PURCHASE/1","stock-receipt-detail"),("/StockOutflow","outflow"),("/Inventory/LowStock","low-stock"),("/Search","search")})
                await File.WriteAllTextAsync(Path.Combine(output,role+"-"+name+".html"),await c.GetStringAsync(url));
        }
        Console.WriteLine("Dashboard browser fixtures: "+output);
    }
    static void Check(bool ok,string label) { if(!ok) throw new Exception("FAIL: "+label); }
}
internal class FakeDashboardRepository : IDashboardRepository
{
    public Task<DashboardInventorySummaryResponse> GetInventorySummaryAsync(bool admin)=>Task.FromResult(new DashboardInventorySummaryResponse{TotalProducts=5,TotalSuppliers=admin?3:null,TotalCustomers=2,StockInflow=20,StockOutflow=7,CurrentStock=13});
    public Task<BusinessDashboardSummaryResponse> GetBusinessSummaryAsync(bool sales)=>Task.FromResult(new BusinessDashboardSummaryResponse{Today=DateTime.Today,TodaySalesAmount=sales?5900:null,MonthSalesAmount=sales?12500:null,TodayInvoiceCount=sales?2:null,LowStockCount=1,PendingServiceCount=2,ReadyServiceCount=1,ExpiringWarrantyCount=3});
    public Task<IEnumerable<MonthlyStock>> GetTrendAsync(string period)=>Task.FromResult<IEnumerable<MonthlyStock>>(Enumerable.Range(0,period switch{"7days"=>7,"30days"=>30,"12months"=>12,_=>6}).Select(i=>new MonthlyStock{Month=DateTime.Today.AddDays(-i),Period="Period "+i,Inflow=i+5,Outflow=i}));
    public Task<IEnumerable<CategoryStock>> GetCategoryStockAsync()=>Task.FromResult<IEnumerable<CategoryStock>>([new(){CategoryName="Battery",Quantity=13}]);
    public Task<IEnumerable<RecentStock>> GetMovementsAsync(DashboardMovementSearch search,bool admin,int limit=100)=>Task.FromResult<IEnumerable<RecentStock>>([new(){ProductName="Battery",Quantity=2,CreatedAt=DateTime.Now,TransactionType=search.Incoming?"PURCHASE":"SERVICE_OUT",PartyName=admin?"Example party":null,ReferenceNumber=search.Incoming?null:"SRV-1",ReferenceUrl=search.Incoming?null:"/Service/Details/1"}]);
    public Task<IEnumerable<RecentSaleResponse>> GetRecentSalesAsync()=>Task.FromResult<IEnumerable<RecentSaleResponse>>([new(){SaleId=1,InvoiceNumber="INV-1",CustomerName="Ravi",SaleDate=DateTime.Today,TotalAmount=5900,Payment="CASH + UPI"}]);
    public Task<IEnumerable<RecentServiceResponse>> GetRecentServicesAsync()=>Task.FromResult<IEnumerable<RecentServiceResponse>>([new(){ServiceRequestId=1,ServiceNumber="SRV-1",CustomerName="<script>alert(1)</script>",ProductName="Battery",Status="READY"}]);
    public Task<IEnumerable<LowStockResponse>> GetLowStockAsync(int offset,int limit=100)=>Task.FromResult<IEnumerable<LowStockResponse>>([new(){ProductId=1,ProductName="Battery",CategoryName="Battery",CurrentStock=2,MinimumStockLevel=5,Required=3}]);
    public Task<IEnumerable<GlobalSearchResult>> SearchAsync(string term)=>Task.FromResult<IEnumerable<GlobalSearchResult>>([new(){Kind="SERIAL",Title="BAT001",Detail="Battery / SOLD",Url="/Scan?code=BAT001"}]);
    public Task<IEnumerable<DashboardUser>> GetUsersAsync()=>Task.FromResult<IEnumerable<DashboardUser>>([new(){UserId=1,FullName="Admin",Username="admin",RoleName="Admin",IsActive=true}]);
    public async Task<DashboardResponse> GetAsync(bool admin,bool sales=true)
    {
        var inventory=await GetInventorySummaryAsync(admin);
        return new(){TotalProducts=inventory.TotalProducts,TotalSuppliers=inventory.TotalSuppliers,TotalCustomers=inventory.TotalCustomers,StockInflow=20,StockOutflow=7,CurrentStock=13,
            Business=await GetBusinessSummaryAsync(sales),Months=(await GetTrendAsync("6months")).ToList(),Categories=(await GetCategoryStockAsync()).ToList(),
            RecentInflow=(await GetMovementsAsync(new(){Incoming=true},admin,5)).ToList(),RecentOutflow=(await GetMovementsAsync(new(),admin,5)).ToList(),
            RecentSales=sales?(await GetRecentSalesAsync()).ToList():[],RecentServices=(await GetRecentServicesAsync()).ToList(),LowStock=(await GetLowStockAsync(0,5)).ToList()};
    }
}
