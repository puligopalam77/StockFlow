using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using System.Text.Json;

namespace StockFlow.MasterDataChecks;
internal static class ProductEditIntegrityChecks
{
 public static async Task Run()
 {
  var cs=Environment.GetEnvironmentVariable("STOCKFLOW_PRODUCT_EDIT_TEST_CONNECTION")??"Host=127.0.0.1;Port=55439;Database=product_edit_069_test;Username=postgres";
  await using var admin=new NpgsqlConnection(cs);await admin.OpenAsync();
  await admin.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1; SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false)");
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
  var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true,ProductImageGalleryAvailable=true};
  var factory=new DbConnectionFactory(config,scope);var repo=new ProductRepository(factory);var variants=new VariantRepository(factory);var categories=new CategoryRepository(factory);
  var service=new ProductService(repo,categories,new BrandRepository(factory),scope,null,variants);
  const int category=4;
  await using var runtime=factory.CreateConnection();
  var requestId=Guid.NewGuid();var code=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@category,@requestId,NULL)",new{category,requestId});
  var id=await service.CreateAsync(new(){RegistrationRequestId=requestId,ParentProductCode=code,CategoryId=category,ProductName="Edit integrity "+Guid.NewGuid().ToString("N")[..8],TrackingType="QUANTITY",PurchasePrice=100,SellingPrice=200});
  // Seed a grandfathered spelling only on this new, unused test product in the isolated clone.
  // Production updates must never use this fixture-only trigger bypass.
  await using(var seed=await admin.BeginTransactionAsync()){
   await admin.ExecuteAsync("ALTER TABLE products DISABLE TRIGGER USER; ALTER TABLE product_variants DISABLE TRIGGER USER; UPDATE products SET parent_product_code=lower(parent_product_code),sku=lower(sku) WHERE product_id=@id; UPDATE product_variants SET sku=lower(sku),attributes=jsonb_build_object('legacy_note','  keep this text  ') WHERE product_id=@id; ALTER TABLE products ENABLE TRIGGER USER; ALTER TABLE product_variants ENABLE TRIGGER USER",new{id},seed);await seed.CommitAsync();
  }
  code=code!.ToLowerInvariant();
  var p=(await service.GetByIdAsync(id))!;var vs=(await variants.GetAsync(id,null,false)).ToList();var originalVariant=vs.Single();
  Check(p.EditVersion?.Length==32&&!p.HasInventoryHistory,"unused product exposes an edit version and unlocked inventory configuration");
  UpdateProductRequest Edit(ProductResponse product,IEnumerable<VariantResponse> saved){var body=JsonSerializer.Deserialize<UpdateProductRequest>(JsonSerializer.Serialize(product))!;body.ExpectedEditVersion=product.EditVersion;body.RegistrationBusinessProfile="ELECTRICAL";body.Variants=saved.Select(v=>JsonSerializer.Deserialize<RegistrationVariantRequest>(JsonSerializer.Serialize(v))!).ToList();return body;}
  var simpleConversion=Edit(p,vs);simpleConversion.HasVariants=true;
  try{await service.UpdateAsync(id,simpleConversion);throw new Exception("Simple SKU conversion was accepted");}
  catch(System.ComponentModel.DataAnnotations.ValidationException error){Check(error.Message.Contains("migration 080"),"simple Product Code/SKU conversion returns actionable validation instead of a database 500");}
  Check(!(await service.GetByIdAsync(id))!.HasVariants,"rejected conversion preserves the saved simple product");
  Check(!await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE product_id=@id)",new{id}),"catalog registration and rejected edit do not invent stock receipts");
  var migration=await File.ReadAllTextAsync("database/migrations/080_unused_product_variants.sql");
  var beforeMigration=await History();await admin.ExecuteAsync(migration);await admin.ExecuteAsync(migration);
  Check(await History()==beforeMigration,"migration 080 is rerunnable and preserves financial and stock records");
  await service.UpdateAsync(id,simpleConversion);
  p=(await service.GetByIdAsync(id))!;vs=(await variants.GetAsync(id,null,false)).ToList();
  Check(p.HasVariants&&p.ParentProductCode==code&&vs.Single().VariantId==originalVariant.VariantId&&vs.Single().Sku==originalVariant.Sku,"unused simple product enables variants without renaming or replacing its existing SKU");
  Check(!await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE product_id=@id)",new{id}),"enabling variants does not add stock");
  var stockUnit=Edit(p,vs);stockUnit.UnitOfMeasure="PAIR";await service.UpdateAsync(id,stockUnit);
  p=(await service.GetByIdAsync(id))!;Check(p.UnitOfMeasure=="PAIR","unused product can change stock unit without receiving stock");
  var old=Edit(p,vs);var name=Edit(p,vs);name.ProductName+=" renamed";await service.UpdateAsync(id,name);
  await Reject(()=>service.UpdateAsync(id,old),"stale edit cannot overwrite a newer product change");
  p=(await service.GetByIdAsync(id))!;vs=(await variants.GetAsync(id,null,false)).ToList();
  var staleVariant=Edit(p,vs);await admin.ExecuteAsync("UPDATE product_variants SET selling_price=selling_price+1 WHERE variant_id=@VariantId",originalVariant);
  await Reject(()=>service.UpdateAsync(id,staleVariant),"concurrent variant price change invalidates the product edit version");
  var otherLocation=await admin.ExecuteScalarAsync<long>("INSERT INTO store_locations(tenant_id,location_name,location_type) VALUES(1,@name,'SALES_FLOOR') RETURNING location_id",new{name="Edit remote "+Guid.NewGuid().ToString("N")[..8]});
  await admin.ExecuteAsync("INSERT INTO inventory_transactions(tenant_id,location_id,product_id,variant_id,transaction_type,quantity,created_by) VALUES(1,@otherLocation,@id,@variant,'ADJUSTMENT_IN',5,1)",new{otherLocation,id,variant=originalVariant.VariantId});
  p=(await service.GetByIdAsync(id))!;vs=(await variants.GetAsync(id,null,false)).ToList();Check(p.HasInventoryHistory,"history at another location locks catalog inventory identity");
  var beforeHistory=await History();
  foreach(var kind in new[]{"unit","tracking","structure","code","sku","pack"}){
   var edit=Edit(p,vs);
   switch(kind){case "unit":edit.UnitOfMeasure="BOX";break;case "tracking":edit.TrackingType="SERIAL";break;case "structure":edit.HasVariants=!p.HasVariants;break;case "code":edit.ParentProductCode="UNSAFE-EDIT";break;case "sku":edit.Variants![0].Sku="UNSAFE-SKU";break;case "pack":edit.Variants![0].PackQuantity=10;break;}
   await Reject(()=>service.UpdateAsync(id,edit),kind+" identity change rejected by the backend");
  }
  await Reject(()=>admin.ExecuteAsync("UPDATE products SET unit_of_measure='BOX' WHERE product_id=@id",new{id}),"direct SQL cannot bypass cross-location stock unit guard");
  await Reject(()=>admin.ExecuteAsync("UPDATE product_variants SET pack_quantity=12 WHERE variant_id=@VariantId",originalVariant),"direct SQL cannot reinterpret historical pack quantities");
  var price=Edit(p,vs);price.ProductName+=" priced";price.Variants![0].SellingPrice=250;price.Description="Editable metadata";price.TaxPercentage=18;price.Variants[0].TaxPercentage=18;price.Variants[0].MinimumStockLevel=9;
  await service.UpdateAsync(id,price);
  p=(await service.GetByIdAsync(id))!;vs=(await variants.GetAsync(id,null,false)).ToList();Check(vs.Single().SellingPrice==250&&p.Description=="Editable metadata"&&vs.Single().MinimumStockLevel==9,"used product permits prospective prices, tax, description and reorder changes");
  Check(vs.Single().VariantId==originalVariant.VariantId&&vs.Single().Sku==originalVariant.Sku&&p.ParentProductCode==code,"price edit preserves stable product, variant and SKU identities");
  Check(vs.Single().Attributes["legacy_note"]=="  keep this text  ","unchanged legacy attribute spelling and whitespace survive price edits");
  Check(beforeHistory==await History(),"catalog edits leave inventory, sales, purchase, receipt and unit snapshots unchanged");
  await Reject(()=>admin.ExecuteAsync("UPDATE product_variants SET is_active=false WHERE variant_id=@VariantId",originalVariant),"stock at any location blocks variant deactivation");
  var multiRequest=Guid.NewGuid();var multiCode=await runtime.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@category,@multiRequest,NULL)",new{category,multiRequest});
  var pair=await runtime.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@multiCode,@multiRequest,@op,2,NULL)",new{multiCode,multiRequest,op=Guid.NewGuid()});
  var multi=await service.CreateAsync(new(){RegistrationRequestId=multiRequest,ParentProductCode=multiCode,ProductName="Edit variant additions "+Guid.NewGuid().ToString("N")[..6],CategoryId=category,TrackingType="QUANTITY",HasVariants=true,RegistrationDefaults=new(){PurchasePrice=100,SellingPrice=200,VariantAxes=["edition"]},Variants=[new(){Sku=pair![0],VariantName="Retail",Attributes=new(){["edition"]="Retail"},PurchasePrice=100,SellingPrice=200},new(){Sku=pair[1],VariantName="Trade",Attributes=new(){["edition"]="Trade"},PurchasePrice=100,SellingPrice=200}]});
  var originalMulti=(await variants.GetAsync(multi,null,false)).ToList();
  await admin.ExecuteAsync("INSERT INTO inventory_transactions(tenant_id,location_id,product_id,variant_id,transaction_type,quantity,created_by) VALUES(1,@otherLocation,@multi,@variant,'ADJUSTMENT_IN',2,1)",new{otherLocation,multi,variant=originalMulti[0].VariantId});
  var next=await runtime.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@multiCode,@rid,@op,1,@multi)",new{multiCode,rid=Guid.NewGuid(),op=Guid.NewGuid(),multi});
  var addVariant=Edit((await service.GetByIdAsync(multi))!,originalMulti);addVariant.Variants!.Add(new(){Sku=next![0],VariantName="Touring",Attributes=new(){["edition"]="Touring"},PurchasePrice=100,SellingPrice=200});await service.UpdateAsync(multi,addVariant);
  var expanded=(await variants.GetAsync(multi,null,false)).ToList();Check(expanded.Count==3&&originalMulti.All(old=>expanded.Any(v=>v.VariantId==old.VariantId&&v.Sku==old.Sku))&&next[0].EndsWith("-03"),"new combinations use the next suffix while existing stock identities remain stable");
  var relabel=Edit((await service.GetByIdAsync(multi))!,expanded);relabel.Variants![0].Attributes["edition"]="Different physical item";await Reject(()=>service.UpdateAsync(multi,relabel),"saved defining variant combination cannot be repurposed");
  var badOwnership=Edit((await service.GetByIdAsync(multi))!,expanded);badOwnership.Variants![0].VariantId=originalVariant.VariantId;await Reject(()=>service.UpdateAsync(multi,badOwnership),"foreign product variant ID cannot be reused");
  await admin.ExecuteAsync("SELECT set_config('app.tenant_id','999999',false)");
  await Reject(()=>admin.ExecuteScalarAsync<bool>("SELECT sf_product_edit_history(@id,NULL)",new{id}),"history function rejects cross-tenant record probing");
  await admin.ExecuteAsync("SELECT set_config('app.tenant_id','1',false)");
  var otherScope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="BIKE_ACCESSORIES",ProfileDataVisibilityAvailable=true};
  Check(await new ProductRepository(new DbConnectionFactory(config,otherScope)).GetByIdAsync(id) is null,"other shop cannot load the product for editing");
  var count=await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE table_name='products' AND record_id=@id AND action_name='UPDATE'",new{id});Check(count>0,"existing audit triggers retain product update history");
  async Task<string> History(){return (await admin.ExecuteScalarAsync<string>("SELECT md5(COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY inventory_transaction_id)::text FROM inventory_transactions t),'')||COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY sale_item_id)::text FROM sale_items t),'')||COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY purchase_item_id)::text FROM purchase_items t),'')||COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY product_unit_id)::text FROM product_units t),'')||COALESCE((SELECT jsonb_agg(to_jsonb(t))::text FROM purchase_receipts t),''))"))!;}
  Console.WriteLine("PASS: isolated product_edit_069_test; runtime and SQL guards, remote-location history, safe prices, optimistic conflicts, tenant/profile isolation, exact historical snapshots and audit history.");
 }
 private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 private static async Task Reject(Func<Task> action,string message){try{await action();}catch(Exception e) when(e is System.ComponentModel.DataAnnotations.ValidationException or PostgresException or System.Data.DBConcurrencyException){Console.WriteLine("PASS: "+message);return;}throw new Exception("Expected rejection: "+message);}
}
