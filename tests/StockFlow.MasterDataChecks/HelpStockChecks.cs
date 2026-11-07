using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;
internal static class HelpStockChecks
{
    public static async Task Run(Factory factory)
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IInventoryRepository>(); services.AddScoped<IInventoryRepository, ScopedStock>();
            services.RemoveAll<IBranchRepository>();
            services.AddSingleton<IBranchRepository>(new FakeBranchRepository { Locations = [
                new() {LocationId=1,StoreId=1,StoreName="Main store",LocationName="Main stock",LocationType="SALES_FLOOR",IsActive=true,IsDefault=true},
                new() {LocationId=2,StoreId=1,StoreName="Main store",LocationName="Other stock",LocationType="SALES_FLOOR",IsActive=true}
            ] });
        }));
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var result = await client.GetFromJsonAsync<JsonElement>("/api/help/stock?term=Battery&tenantId=2&locationId=99");
        var row = result.GetProperty("items")[0];
        Check(row.GetProperty("availableQuantity").GetInt64() == 5 && row.GetProperty("currentStock").GetInt64() == 7 && row.GetProperty("hasDiscrepancy").GetBoolean(), "Serial availability and stock discrepancy match repository values");
        Check(result.GetProperty("scopeName").GetString() == "Main store / Main stock", "Request uses validated stock location; submitted tenant/location filters are ignored");
        client.DefaultRequestHeaders.Add("Test-Tenant", "2");
        var other = await client.GetFromJsonAsync<JsonElement>("/api/help/stock?term=Battery");
        Check(other.GetProperty("items")[0].GetProperty("currentStock").GetInt64() == 99, "Different authenticated businesses use different scoped repository records");
        client.DefaultRequestHeaders.Remove("Test-Tenant");
        client.DefaultRequestHeaders.Add("Test-Location", "2");
        var location = await client.GetFromJsonAsync<JsonElement>("/api/help/stock?term=Battery");
        Check(location.GetProperty("items")[0].GetProperty("currentStock").GetInt64()==13, "Authorized locations receive their own scoped balance");
        client.DefaultRequestHeaders.Remove("Test-Location");
        var quantity = await client.GetFromJsonAsync<JsonElement>("/api/help/stock?term=Quantity");
        Check(quantity.GetProperty("items")[0].GetProperty("availableQuantity").GetInt64() == 12, "Quantity-tracked items use current stock rather than serial count");
        var empty = await client.GetFromJsonAsync<JsonElement>("/api/help/stock?term=missing");
        Check(empty.GetProperty("items").GetArrayLength() == 0, "No matches remain distinct from zero stock");
        var many = await client.GetFromJsonAsync<JsonElement>("/api/help/stock?term=many");
        Check(many.GetProperty("items").GetArrayLength() == 50 && many.GetProperty("truncated").GetBoolean(), "Large results are limited and marked as partial");
        Check((await client.GetAsync("/api/help/stock?lowStockOnly=true")).IsSuccessStatusCode, "Low stock enquiry supports an empty product filter");
        Check((int)(await client.GetAsync("/api/help/stock?term=x")).StatusCode == 400 &&
            (int)(await client.GetAsync("/api/help/stock?term=" + new string('a',151))).StatusCode == 400, "Search input limits enforced");
        client.DefaultRequestHeaders.Add("Test-Deny", "INVENTORY");
        Check((int)(await client.GetAsync("/api/help/stock?term=Battery")).StatusCode == 403, "Inventory permission is required by the enquiry API");
        Check(!(await client.GetStringAsync("/Purchases/Create")).Contains("id=\"help-stock-form\""), "Missing permission hides the enquiry form");
        using var anonymous = app.CreateClient();
        Check((int)(await anonymous.GetAsync("/api/help/stock?term=Battery")).StatusCode == 401, "Anonymous stock enquiry denied");
        client.DefaultRequestHeaders.Remove("Test-Deny"); client.DefaultRequestHeaders.Add("Test-Location", "99");
        Check((int)(await client.GetAsync("/api/help/stock?term=Battery")).StatusCode == 403, "Unassigned location denied before stock retrieval");
        using var staffApp = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IInventoryRepository>(); services.AddScoped<IInventoryRepository, ScopedStock>();
            services.RemoveAll<IBranchRepository>(); services.AddSingleton<IBranchRepository>(new FakeBranchRepository { State=new(){IsActive=true,IsOwner=false} });
        }));
        using var staff=staffApp.CreateClient(); staff.DefaultRequestHeaders.Add("Test-Role","Sales");
        Check((await staff.GetAsync("/api/help/stock?term=Battery")).IsSuccessStatusCode,"Staff inventory readers can enquire at their assigned location");
        staff.DefaultRequestHeaders.Add("Test-Location","0");staff.DefaultRequestHeaders.Add("Test-Store","0");
        Check((int)(await staff.GetAsync("/api/help/stock?term=Battery")).StatusCode==403,"Staff cannot request a consolidated scope");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        Directory.CreateDirectory(Path.Combine(root!.FullName,"tests/bin/help-panel-fixtures"));
        client.DefaultRequestHeaders.Remove("Test-Location");
        await File.WriteAllTextAsync(Path.Combine(root.FullName,"tests/bin/help-panel-fixtures/purchase.html"), await client.GetStringAsync("/Purchases/Create"));
        Console.WriteLine("Stock enquiry checks use scoped fake records; existing PostgreSQL RLS is reused, without database writes.");
    }
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    internal class ScopedStock(BusinessScope scope) : IInventoryRepository
    {
        public Task<LowStockPage> GetLowStockPageAsync(string? term,string? stockStatus,int page,int pageSize)=>throw new NotSupportedException("This enquiry fake does not serve paged low-stock reports.");
        public Task<IEnumerable<InventoryBalance>> GetBalancesAsync(string? term,bool lowStockOnly,bool includeInactive)
        {
            if(includeInactive)throw new Exception("Inactive stock included");
            var row = new InventoryBalance { ProductId=1, VariantId=11, ProductName="Battery / 150 Ah", Sku="BAT-150", IsActive=true,
                IsSerialTracked=term!="Quantity",CurrentStock=scope.TenantId==2?99:scope.LocationId==2?13:term=="Quantity"?12:7,AvailableUnits=5 };
            return Task.FromResult<IEnumerable<InventoryBalance>>(term=="missing"?[]:term=="many"?Enumerable.Repeat(row,51):[row]);
        }
        public Task<IEnumerable<InventoryMovement>> GetHistoryAsync(int? p,DateTime? f,DateTime? t,int o,long? v=null)=>throw new Exception("Unexpected history read");
        public Task<IEnumerable<InventoryUnit>> GetUnitsAsync(int? p,string? t,bool a,int o,long? v=null)=>throw new Exception("Unexpected unit read");
        public Task<long> AdjustAsync(StockAdjustmentRequest r,int u)=>throw new Exception("Stock enquiry must not write");
    }
}
