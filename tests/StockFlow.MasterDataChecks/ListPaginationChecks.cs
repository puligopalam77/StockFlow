using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Filters;

namespace StockFlow.MasterDataChecks;

internal static class ListPaginationChecks
{
    public static async Task Run(Factory factory)
    {
        foreach (var query in new[]{"?offset=-1","?offset=2147483648","?page=0","?page=1.5","?pageSize=101","?limit=0","?offset=0&offset=1","?from=invalid","?to=2026-02-31","?from=2026-12-01&to=2026-01-01","?term="+new string('a',201)})
            Check(Validate(query,"GET"),"reject invalid query "+query[..Math.Min(70,query.Length)]);
        foreach(var query in new[]{"?offset=0&limit=100","?page=1&pageSize=25","?term=%20Cable%20","?from=2026-01-01&to=2026-12-01"})
            Check(!Validate(query,"GET"),"accept valid list query "+query);
        Check(!Validate("?page=0","POST"),"write validation remains owned by existing endpoints");
        using var client=factory.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var url in new[]{"/api/service?offset=-1","/api/sales/list?limit=101","/api/admin/users?offset=1.5","/api/categories?term="+new string('a',201)})
            Check((int)(await client.GetAsync(url)).StatusCode==400,"HTTP guard "+url[..Math.Min(70,url.Length)]);
        Check((await client.GetAsync("/api/categories?term=%20Helmet%20")).IsSuccessStatusCode,"valid scoped category search remains available");
        Console.WriteLine("PASS: pagination and filter query validation, HTTP integration, existing write validation preserved.");
    }

    public static async Task Database()
    {
        // Read-only queries against a disposable loopback test database; never application configuration.
        const string cs="Host=127.0.0.1;Port=55439;Database=product_sku_previews_test;Username=postgres";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
        var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
        var factory=new DbConnectionFactory(config,scope);
        var warranty=new WarrantyRepository(factory);var security=new SecurityRepository(factory);var inventory=new InventoryRepository(factory);var dashboard=new DashboardRepository(factory);
        async Task ReadAll(){
            Count(await new ServiceRepository(factory,warranty).GetAllAsync(new()),"service");
            Count(await warranty.GetExpiringAsync(3650,0),"expiring warranties");Count(await warranty.GetClaimsAsync(null,null,0),"warranty claims");
            Count(await security.GetUsersAsync(null,0),"staff");Count(await security.GetAuditAsync(new()),"audit");
            Count(await inventory.GetHistoryAsync(null,null,null,0),"movements");Count(await inventory.GetUnitsAsync(null,null,false,0),"units");
            Count(await dashboard.GetMovementsAsync(new(),true),"dashboard movements");Count(await dashboard.GetLowStockAsync(0),"low stock");
            var notifications=new NotificationRepository(factory);var ten=await notifications.FeedPageAsync(1,10);var fifty=await notifications.FeedPageAsync(1,50);
            Check(ten.Items.Count<=10&&fifty.Items.Count<=50&&ten.Total==fifty.Total,"notification counts and requested page sizes");
        }
        await ReadAll();scope.BusinessProfileCode="BIKE_ACCESSORIES";await ReadAll();
        scope.TenantId=987654;Check(!(await security.GetUsersAsync(null,0)).Any(),"paged staff query cannot cross tenant scope");
        Check(!(await inventory.GetUnitsAsync(null,null,false,0)).Any(),"paged units cannot cross tenant scope");
        Console.WriteLine("PASS: scoped paging/count SQL executes against disposable PostgreSQL, both shop profiles and foreign tenant isolation.");
    }
    static void Count<T>(IEnumerable<T> rows,string label){var data=rows.ToArray();foreach(var row in data){var count=(long?)typeof(T).GetProperty("TotalCount")!.GetValue(row);Check(count>=data.Length,label+" includes filtered total");}Console.WriteLine($"READ {label}: {data.Length} rows");}
    static bool Validate(string query,string method){var http=new DefaultHttpContext();http.Request.Method=method;http.Request.QueryString=new QueryString(query);var action=new ActionContext(http,new RouteData(),new ActionDescriptor());var context=new ActionExecutingContext(action,[],new Dictionary<string,object?>(),new object());new ListQueryValidationFilter().OnActionExecuting(context);return context.Result is BadRequestObjectResult;}
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
