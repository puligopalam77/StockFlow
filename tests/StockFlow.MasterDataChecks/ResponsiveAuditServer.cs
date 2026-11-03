using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

// Loopback-only adapter to TestServer. All writes are blocked. Unmocked database
// queries fail at port 1, never at a configured shop database.
internal static class ResponsiveAuditServer
{
    public static async Task Run()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/responsive-audit");Directory.CreateDirectory(output);
        using var source=new Factory();
        using var app=source.WithWebHostBuilder(builder=>{
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:DefaultConnection"]="Host=127.0.0.1;Port=1;Database=blocked;Username=blocked;Timeout=1;Pooling=false",
                ["HelpAi:Enabled"]="false"
            }));
            builder.ConfigureServices(services=>{
                services.RemoveAll<IReportingRepository>();services.AddScoped<IReportingRepository,FakePeriodReports>();
                services.RemoveAll<INotificationRepository>();services.AddSingleton<INotificationRepository,NotificationChecks.FakeNotifications>();
            });
        });
        using var client=app.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        // Populate only in-memory repositories so edit/details views contain real
        // synthetic fields instead of just exercising their missing-record state.
        await app.Services.GetRequiredService<ICategoryRepository>().CreateAsync(new StockFlow.Domain.Entities.Category{CategoryName="Synthetic replacement parts and accessories",IsActive=true});
        await app.Services.GetRequiredService<IBrandRepository>().CreateAsync(new StockFlow.Domain.Entities.Brand{BrandName="Synthetic Manufacturer",IsActive=true});
        await app.Services.GetRequiredService<ISupplierRepository>().CreateAsync(new StockFlow.Domain.Entities.Supplier{SupplierName="Synthetic Bengaluru Parts Supplier",MobileNumber="9123456780",IsActive=true});
        await app.Services.GetRequiredService<ICustomerRepository>().CreateAsync(new StockFlow.Domain.Entities.Customer{CustomerName="Synthetic Customer With A Longer Display Name",Address="Synthetic test address, Bengaluru, Karnataka",IsActive=true});
        await app.Services.GetRequiredService<IProductRepository>().CreateAsync(new StockFlow.Domain.Entities.Product{ProductName="Synthetic replacement assembly with a longer product name",CategoryId=1,BrandId=1,Sku="AUD-0001",PurchasePrice=900,SellingPrice=1250,IsActive=true});
        await app.Services.GetRequiredService<IVariantRepository>().SaveAsync(null,new StockFlow.Application.DTOs.Products.VariantRequest{ProductId=1,VariantName="Standard replacement model",Sku="AUD-0001",PurchasePrice=900,SellingPrice=1250,TaxPercentage=18,IsActive=true},1);
        var descriptors=app.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        var pages=descriptors.OfType<PageActionDescriptor>().OrderBy(x=>x.ViewEnginePath).ToArray();
        var inventory=new List<object>();var urls=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var page in pages){
            var template=page.AttributeRouteInfo?.Template ?? page.ViewEnginePath.TrimStart('/');
            var routes=new List<string>();
            if(template.Contains("{mode?}")) {
                var prefix="/"+template.Replace("/{mode?}","").Trim('/');
                routes.AddRange([prefix,prefix+"/create",prefix+"/edit?id=1",prefix+"/view?id=1"]);
            } else if(template.Contains("{source}")) {
                foreach(var kind in new[]{"PURCHASE","OPENING_STOCK","CUSTOMER_RETURN","STOCK_COUNT","ADJUSTMENT"}) routes.Add("/"+Regex.Replace(template.Replace("{source}",kind),@"\{id[^}]*\}","1").Trim('/'));
            } else routes.Add("/"+Regex.Replace(template,@"\{[^}]*\}","1").Trim('/'));
            foreach(var route in routes.ToArray()) if(route.EndsWith("/Index",StringComparison.OrdinalIgnoreCase)) routes.Add(route[..^6] is {Length:>0} canonical?canonical:"/");
            foreach(var url in routes)urls.Add(url);
            inventory.Add(new{page=page.ViewEnginePath,template,routes});
        }
        var endpoints=descriptors.Where(x=>x is not PageActionDescriptor).Select(x=>new{route=x.AttributeRouteInfo?.Template,action=x.DisplayName}).ToArray();
        await File.WriteAllTextAsync(Path.Combine(output,"inventory.json"),JsonSerializer.Serialize(new{pages=inventory,urls,controllerEndpoints=endpoints},new JsonSerializerOptions{WriteIndented=true}));
        using var listener=new HttpListener();listener.Prefixes.Add("http://127.0.0.1:55863/");listener.Start();
        Console.WriteLine($"Responsive audit: {pages.Length} page definitions, {urls.Count} screen URLs. Loopback port 55863; writes/database access blocked.");
        while(listener.IsListening){
            var context=await listener.GetContextAsync();
            if(context.Request.Url!.AbsolutePath=="/_audit/stop"){context.Response.StatusCode=204;context.Response.Close();break;}
            _=Handle(context);
        }
        async Task Handle(HttpListenerContext context){
            try{
                var url=context.Request.Url!;
                if(context.Request.HttpMethod!="GET"){context.Response.StatusCode=405;context.Response.Close();return;}
                if(url.AbsolutePath=="/_audit/inventory"){context.Response.ContentType="application/json";await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(output,"inventory.json")));return;}
                if(url.AbsolutePath.StartsWith("/css/")||url.AbsolutePath.StartsWith("/js/")||url.AbsolutePath.StartsWith("/lib/")||url.AbsolutePath.StartsWith("/images/")){
                    var relative=Regex.Replace(url.AbsolutePath,@"\.[a-z0-9]{10}(?=\.(css|js|png|svg|woff2?)$)","");
                    var webroot=Path.GetFullPath(Path.Combine(root.FullName,"src/StockFlow.Web/wwwroot"));
                    var file=Path.GetFullPath(Path.Combine(webroot,relative.TrimStart('/')));
                    if(!file.StartsWith(webroot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(file)){context.Response.StatusCode=404;return;}
                    context.Response.ContentType=Path.GetExtension(file) switch{".css"=>"text/css",".js"=>"application/javascript",".png"=>"image/png",".svg"=>"image/svg+xml",_=>"application/octet-stream"};
                    await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(file));return;
                }
                using var response=await client.GetAsync(url.PathAndQuery);
                context.Response.StatusCode=(int)response.StatusCode;
                context.Response.ContentType=response.Content.Headers.ContentType?.ToString()??"text/plain";
                if(response.Headers.Location is not null)context.Response.RedirectLocation=response.Headers.Location.ToString();
                await context.Response.OutputStream.WriteAsync(await response.Content.ReadAsByteArrayAsync());
            }catch(Exception failure){
                try{context.Response.StatusCode=500;context.Response.ContentType="text/plain";await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Synthetic audit fixture unavailable: "+failure.GetType().Name));}catch{}
            }finally{context.Response.Close();}
        }
    }
}
