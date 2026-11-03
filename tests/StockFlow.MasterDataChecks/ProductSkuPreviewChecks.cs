using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class ProductSkuPreviewChecks
{
 public static async Task Run()
 {
  const string cs="Host=127.0.0.1;Port=55439;Database=product_sku_previews_test;Username=postgres";
  await using var admin=new NpgsqlConnection(cs);await admin.OpenAsync();await admin.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
  var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
  var factory=new DbConnectionFactory(config,scope);var products=new ProductRepository(factory);var variants=new VariantRepository(factory);
  var service=new ProductService(products,new CategoryRepository(factory),new BrandRepository(factory),scope,null,variants);
  await using var runtime=factory.CreateConnection();
  async Task<(int Id,string Name,string[] Codes)> Create(int count,bool configured){
   var request=Guid.NewGuid();var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(4,@request,NULL)",new{request});
   var codes=configured?(await runtime.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@code,@request,@op,@count,NULL)",new{code,request,op=Guid.NewGuid(),count}))!:[code!];
   var name="SKU preview "+Guid.NewGuid().ToString("N")[..8];
   var id=await service.CreateAsync(new(){RegistrationRequestId=request,ParentProductCode=code,ProductName=name,CategoryId=4,TrackingType="QUANTITY",HasVariants=configured,PurchasePrice=100,SellingPrice=200,
    RegistrationDefaults=configured?new(){VariantAxes=["edition"],PurchasePrice=100,SellingPrice=200}:null,
    Variants=configured?codes.Select((sku,i)=>new RegistrationVariantRequest{Sku=sku,VariantName="Edition "+(i+1),Attributes=new(){["edition"]=(i+1).ToString()},PurchasePrice=100,SellingPrice=200}).ToList():null});
   return(id,name,codes);
  }
  var simple=await Create(1,false);var pair=await Create(2,true);var five=await Create(5,true);var single=await Create(1,true);
  var fifth=(await variants.GetAsync(five.Id,null,false)).OrderBy(v=>v.VariantId).Last();await runtime.ExecuteAsync("UPDATE product_variants SET is_active=false WHERE variant_id=@VariantId",fifth);
  async Task<ProductResponse> Get(string name)=> (await service.GetAllAsync(false,name)).Single();
  var p=await Get(simple.Name);Check(p.ConfiguredVariantCount==0&&p.DefaultVariantSku==simple.Codes[0],"simple default SKU is not a configured variation");
  p=await Get(pair.Name);Check(p.ConfiguredVariantCount==2&&p.SkuPreviews.Select(v=>v.Sku).SequenceEqual(pair.Codes.Take(1)),"two configured variants retain their count with one initial preview");
  p=await Get(single.Name);Check(p.ConfiguredVariantCount==1&&p.SkuPreviews.Single().Sku==single.Codes[0],"one explicitly configured variant is counted even when is_default");
  p=await Get(five.Name);Check(p.ConfiguredVariantCount==5&&p.SkuCount==5&&p.SkuPreviews.Select(v=>v.Sku).SequenceEqual(five.Codes.Take(1)),"one preview; inactive variants remain in the saved total");
  p=await Get(five.Codes[4].ToLowerInvariant());Check(p.ProductId==five.Id&&p.SkuPreviews[0].Sku==five.Codes[4]&&p.ConfiguredVariantCount==5,"search across every SKU prioritizes a hidden inactive matching SKU");
  p=await Get(five.Name);Check(p.SkuPreviews[0].Sku==five.Codes[0],"search does not persist a changed SKU order");
  // Fixture-only legacy inconsistency on a newly created isolated test product.
  await admin.ExecuteAsync("ALTER TABLE products DISABLE TRIGGER USER; UPDATE products SET has_variants=false WHERE product_id=@Id; ALTER TABLE products ENABLE TRIGGER USER",new{pair.Id});
  p=await Get(pair.Name);Check(p.ConfiguredVariantCount==null&&p.SkuPreviews.Count==1,"ambiguous legacy configuration uses an unknown count rather than inventing one");
  var before=await admin.ExecuteScalarAsync<string>("SELECT md5(string_agg(j::text,'' ORDER BY j::text)) FROM (SELECT to_jsonb(p) j FROM products p UNION ALL SELECT to_jsonb(v) FROM product_variants v) q");
  var all=(await service.GetAllAsync()).ToList();Check(all.Select(p=>p.ProductId).Distinct().Count()==all.Count,"one parent row per product");
  foreach(var product in all){Check(product.SkuPreviews.Count<=1,"bounded single SKU preview");foreach(var preview in product.SkuPreviews)Check(await runtime.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_variants WHERE tenant_id=1 AND product_id=@ProductId AND variant_id=@VariantId AND sku=@Sku)",new{product.ProductId,preview.VariantId,preview.Sku}),"preview belongs to its parent in the selected shop");}
  scope.TenantId=987654;Check(!(await service.GetAllAsync(false,five.Codes[4])).Any(),"foreign tenant cannot search SKU previews");scope.TenantId=1;
  scope.BusinessProfileCode="BIKE_ACCESSORIES";Check(!(await service.GetAllAsync(false,five.Codes[4])).Any(),"other shop cannot search the SKU");scope.BusinessProfileCode="ELECTRICAL";
  Check(before==await admin.ExecuteScalarAsync<string>("SELECT md5(string_agg(j::text,'' ORDER BY j::text)) FROM (SELECT to_jsonb(p) j FROM products p UNION ALL SELECT to_jsonb(v) FROM product_variants v) q"),"listing and search leave product and variant data unchanged");
  Console.WriteLine("PASS: actual PostgreSQL list preview/count/search/isolation checks; isolated database only.");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
