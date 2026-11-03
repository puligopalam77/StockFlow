using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class LegacySkuCorrectionChecks
{
 public static async Task Run()
 {
  const string cs="Host=127.0.0.1;Port=55439;Database=sku_correction_test;Username=postgres";
  await using var admin=new NpgsqlConnection(cs);await admin.OpenAsync();
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
  var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
  var factory=new DbConnectionFactory(config,scope);var scanner=new ScanRepository(factory);var products=new ProductRepository(factory);var variants=new VariantRepository(factory);
  var mappings=(await admin.QueryAsync<(string Profile,int ProductId,long VariantId,string OldSku,string NewSku,string NewProductCode)>("SELECT profile,product_id AS ProductId,variant_id AS VariantId,old_sku AS OldSku,new_sku AS NewSku,new_product_code AS NewProductCode FROM catalog_sku_corrections ORDER BY profile,variant_id")).ToList();
  Check(mappings.Count==8,"all eight audited SKU rows corrected");
  foreach(var shop in mappings.GroupBy(m=>m.Profile)){
   scope.BusinessProfileCode=shop.Key;await admin.ExecuteAsync("UPDATE tenants SET business_profile_code=@profile WHERE tenant_id=1",new{profile=shop.Key});
   foreach(var m in shop){
    var old=await scanner.FindByCodeAsync(m.OldSku.ToLowerInvariant());var current=await scanner.FindByCodeAsync(m.NewSku);
    Check(old?.VariantId==m.VariantId&&old?.ProductId==m.ProductId&&old?.Sku==m.NewSku&&current?.VariantId==m.VariantId,"old and new SKU scan to same saved variant: "+m.OldSku);
    Check(old!.CurrentStock==current!.CurrentStock,"old/new scans expose identical stock");
    Check((await products.GetAllAsync(false,m.OldSku)).Any(p=>p.ProductId==m.ProductId),"legacy SKU remains searchable");
    Check((await variants.GetAsync(null,m.OldSku,false)).Any(v=>v.VariantId==m.VariantId&&v.Sku==m.NewSku),"variant lookup accepts the old SKU");
   }
   await using var runtime=factory.CreateConnection();
   var hidden=mappings.First(m=>m.Profile!=shop.Key);
   Check(await scanner.FindByCodeAsync(hidden.OldSku)==null&&!(await products.GetAllAsync(false,hidden.OldSku)).Any(),"other shop's legacy SKU is hidden");
   try{await runtime.ExecuteAsync("UPDATE catalog_sku_corrections SET correction_reason='bad'");throw new Exception("Runtime can modify correction audit");}catch(PostgresException e)when(e.SqlState=="42501"){Check(true,"runtime cannot modify correction audit");}
   if(shop.Key=="BIKE_ACCESSORIES"){
    var family=shop.First(m=>m.ProductId==5).NewProductCode;
    var priorSuffix=await admin.ExecuteScalarAsync<long>("SELECT variant_counter FROM catalog_identifier_ledger WHERE tenant_id=1 AND profile='BIKE_ACCESSORIES' AND code=@family",new{family});
    var next=await runtime.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@family,@request,@operation,1,5)",new{family,request=Guid.NewGuid(),operation=Guid.NewGuid()});
    Check(priorSuffix>=4&&next!.Single()==family+"-"+(priorSuffix+1).ToString("D2"),"future variant allocation continues after corrected suffixes");
    try{await runtime.ExecuteAsync("UPDATE product_variants SET sku='ILLEGAL-CHANGE' WHERE variant_id=5");throw new Exception("SKU guard disabled");}catch(PostgresException e)when(e.SqlState=="23514"){Check(true,"normal permanent-SKU guard restored");}
   }
  }
  scope.TenantId=987654;Check(await scanner.FindByCodeAsync(mappings[0].OldSku)==null,"other tenant cannot scan aliases");
  Console.WriteLine("PASS: isolated correction alias/search/stock/isolation/guard/counter checks.");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
