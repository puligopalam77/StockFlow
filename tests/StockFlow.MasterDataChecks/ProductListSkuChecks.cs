using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Application.DTOs.Security;
using StockFlow.Web.Security;
using StockFlow.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace StockFlow.MasterDataChecks;
internal static class ProductListSkuChecks
{
    public static async Task Run()
    {
        const string cs="Host=127.0.0.1;Port=55439;Database=product_list_skus_test;Username=postgres";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
        var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
        var factory=new DbConnectionFactory(config,scope);var repo=new ProductRepository(factory);var variants=new VariantRepository(factory);
        var service=new ProductService(repo,new CategoryRepository(factory),new BrandRepository(factory),scope,null,variants);
        await using var runtime=factory.CreateConnection();
        var request=Guid.NewGuid();var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(4,@request,NULL)",new{request});
        var name="List search "+Guid.NewGuid().ToString("N")[..10];
        var id=await service.CreateAsync(new(){RegistrationRequestId=request,ParentProductCode=code,CategoryId=4,ProductName=name,HsnCode="6501",TrackingType="QUANTITY",PurchasePrice=100,SellingPrice=200});
        var before=await runtime.ExecuteScalarAsync<string>("SELECT md5(string_agg(to_jsonb(v)::text,'' ORDER BY variant_id)) FROM product_variants v");
        foreach(var term in new[]{name.ToUpperInvariant(),"  "+code!.ToLowerInvariant()+"  ","6501",code[1..]}){
            var results=(await service.GetAllAsync(false,term)).ToList();Check(results.Any(p=>p.ProductId==id),"name/code/default SKU/HSN/partial search: "+term);
            Check(results.Select(p=>p.ProductId).Distinct().Count()==results.Count,"one parent per search result");
        }
        var simple=(await service.GetAllAsync(false,name)).Single();Check(simple.SkuCount==1&&simple.DefaultVariantSku==code,"simple SKU uses the actual default variant");
        var multi=(await runtime.QueryAsync<(int Id,string Sku)>("SELECT p.product_id,v.sku FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.has_variants AND NULLIF(btrim(v.sku),'') IS NOT NULL ORDER BY v.variant_id")).First();
        var found=(await service.GetAllAsync(false,"  "+multi.Sku.ToLowerInvariant()+"  ")).Single(p=>p.ProductId==multi.Id);
        var actual=await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM product_variants WHERE product_id=@Id AND NULLIF(btrim(sku),'') IS NOT NULL",new { multi.Id });
        Check(found.SkuCount==actual&&found.HasMatchingSku,"variant search exposes its parent and actual SKU count");
        var permission=new PermissionContext(scope){User=new UserAccess{IsActive=true,Permissions=[new(){ModuleCode="PRODUCTS",CanView=true}]}};
        var controller=new ProductController(service,permission,variants);
        async Task<JsonElement> Skus(int productId,int offset=0){var result=(OkObjectResult)await controller.Skus(productId,multi.Sku,offset,factory);return JsonSerializer.SerializeToElement(result.Value);}
        var details=await Skus(multi.Id);
        Check(details.GetProperty("items")[0].GetProperty("sku").GetString()==multi.Sku,"SKU detail endpoint puts a searched SKU first and serializes named fields");
        Check((await Skus(multi.Id,1)).GetProperty("items").GetArrayLength()==Math.Max(0,actual-1),"SKU detail endpoint honors offset");
        Check(await controller.Skus(multi.Id,null,-1,factory) is BadRequestObjectResult,"negative SKU offset rejected");
        permission.User.IsActive=false;Check(await controller.Skus(multi.Id,null,0,factory) is ForbidResult,"SKU details require Products view permission");permission.User.IsActive=true;
        var active=(await service.GetAllAsync(true)).ToList();Check(active.All(p=>p.IsActive),"active status filter preserved");
        await using var admin=new NpgsqlConnection(cs);
        var expected=(await admin.QueryAsync<int>("SELECT p.product_id FROM products p JOIN categories c USING(tenant_id,category_id) WHERE p.tenant_id=1 AND c.business_profile_code='ELECTRICAL' ORDER BY p.product_name,p.product_id")).ToArray();
        Check((await repo.GetAllAsync()).Select(p=>p.ProductId).SequenceEqual(expected),"selected tenant/profile isolation and original ordering");
        scope.TenantId=987654;Check(!(await repo.GetAllAsync(false,code)).Any(),"foreign tenant cannot search local product code or SKU");scope.TenantId=1;
        scope.TenantId=987654;Check((await Skus(multi.Id)).GetProperty("items").GetArrayLength()==0,"SKU expansion hides foreign-tenant variants");scope.TenantId=1;
        scope.BusinessProfileCode="BIKE_ACCESSORIES";Check(!(await repo.GetAllAsync(false,name)).Any(),"other shop cannot search product identity");Check((await Skus(multi.Id)).GetProperty("items").GetArrayLength()==0,"SKU expansion hides the other shop's variants");scope.BusinessProfileCode="ELECTRICAL";
        Check(before==await runtime.ExecuteScalarAsync<string>("SELECT md5(string_agg(to_jsonb(v)::text,'' ORDER BY variant_id)) FROM product_variants v"),"list and searches preserve all variant data");
        Console.WriteLine("PASS: actual PostgreSQL runtime-role list/search checks; isolated database only.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
