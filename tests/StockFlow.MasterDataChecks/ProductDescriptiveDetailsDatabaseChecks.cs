using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class ProductDescriptiveDetailsDatabaseChecks
{
 public static async Task Run()
 {
  const string cs="Host=127.0.0.1;Port=55439;Database=sequential_sku_067_v2_test;Username=postgres";
  await using var admin=new NpgsqlConnection(cs);await admin.OpenAsync();
  Check(await admin.ExecuteScalarAsync<string>("SELECT current_database()") == "sequential_sku_067_v2_test","isolated test database only");
  await admin.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','BIKE_ACCESSORIES',false)");
  var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="BIKE_ACCESSORIES",CatalogRegistrationEnhancementsAvailable=true,ProductImageGalleryAvailable=true,ProfileDataVisibilityAvailable=true};
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
  var factory=new DbConnectionFactory(config,scope);var products=new ProductRepository(factory);var variants=new VariantRepository(factory);var categories=new CategoryRepository(factory);var service=new ProductService(products,categories,new BrandRepository(factory),scope,new CatalogSchemaRepository(factory,categories),variants);
  var category=await categories.CreateAsync(new(){CategoryName="Descriptive details test "+Guid.NewGuid().ToString("N")[..8],IsActive=true});
  await using var c=factory.CreateConnection();var requestId=Guid.NewGuid();var code=await c.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@category,@requestId,NULL)",new{category,requestId});var codes=await c.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@code,@requestId,@operation,2,NULL)",new{code,requestId,operation=Guid.NewGuid()});
  var movementsBefore=await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions");
  var defaults=new ProductRegistrationDefaults{PurchasePrice=100,SellingPrice=200,Mrp=250,VariantAxes=["wattage"]};var barcode="DETAILS-MFR-"+Guid.NewGuid().ToString("N")[..12];
  var id=await service.CreateAsync(new(){RegistrationRequestId=requestId,ParentProductCode=code,ProductName="Descriptive headlight",CategoryId=category,HasVariants=true,TrackingType="QUANTITY",Description="Basic product description",DetailedDescription="Independent detailed description",RegistrationDefaults=defaults,Variants=[new(){VariantName="35W",Sku=codes![0],PurchasePrice=100,SellingPrice=200,Attributes=new(){["wattage"]="35W",["Material"]="ABS",["Position"]="Front",["Thickness"]="5 mm"},PrimaryBarcode=barcode,BarcodeSource="MANUFACTURER"},new(){VariantName="55W",Sku=codes[1],PurchasePrice=100,SellingPrice=200,Attributes=new(){["wattage"]="55W",["Material"]="ABS",["Position"]="Front",["Thickness"]="5 mm"}}]});
  var saved=(await variants.GetAsync(id,null,false)).OrderBy(v=>v.VariantId).ToList();
  Check(saved.Count==2&&saved.All(v=>v.Attributes["Material"]=="ABS"&&v.Attributes["Position"]=="Front"&&v.Attributes["Thickness"]=="5 mm"),"descriptive fields round-trip in existing attributes JSON");
  Check(saved.Select(v=>v.Sku).SequenceEqual(codes!)&&saved.All(v=>v.CurrentStock==0),"specifications create no extra variants, SKUs or stock");
  var barcodeRowsBefore=await admin.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(b) ORDER BY variant_barcode_id)::text FROM variant_barcodes b JOIN product_variants v USING(tenant_id,variant_id) WHERE v.product_id=@id",new{id});
  var counterBefore=await admin.ExecuteScalarAsync<long>("SELECT last_number FROM catalog_prefix_registry WHERE tenant_id=1 AND profile='BIKE_ACCESSORIES' AND category_id=@category AND prefix=split_part(@code,'-',1)",new{category,code});
  var rows=saved.Select(v=>new RegistrationVariantRequest{VariantId=v.VariantId,VariantName=v.VariantName,Sku=v.Sku,PurchasePrice=v.PurchasePrice,SellingPrice=v.SellingPrice,Mrp=v.Mrp,PackQuantity=v.PackQuantity,TaxPercentage=v.TaxPercentage,WarrantyMonths=v.WarrantyMonths,IsActive=v.IsActive,PrimaryBarcode=v.PrimaryBarcode,BarcodeSource=v.BarcodeSource,Barcodes=v.Barcodes,Attributes=new(v.Attributes)}).ToList();
  foreach(var row in rows){row.Attributes.Remove("Material");row.Attributes.Remove("Position");row.Attributes["Shell Material"]="Ceramic";}rows[0].Attributes["Legacy Note"]="Keep with 35W only";
  await service.UpdateAsync(id,new(){ParentProductCode=code,ProductName="Descriptive headlight",CategoryId=category,HasVariants=true,TrackingType="QUANTITY",IsActive=true,Description="Basic product description",DetailedDescription="Independent detailed description",RegistrationDefaults=defaults,Variants=rows});
  var edited=(await variants.GetAsync(id,null,false)).OrderBy(v=>v.VariantId).ToList();var product=(await products.GetByIdAsync(id))!;
  Check(edited.All(v=>v.Attributes["Shell Material"]=="Ceramic"&&!v.Attributes.ContainsKey("Material")&&!v.Attributes.ContainsKey("Position"))&&edited[0].Attributes["Legacy Note"]=="Keep with 35W only"&&!edited[1].Attributes.ContainsKey("Legacy Note"),"renaming/removing details persists without mixing variant-specific legacy values");
  Check(edited.Select(v=>v.VariantId).SequenceEqual(saved.Select(v=>v.VariantId))&&edited.Select(v=>v.Sku).SequenceEqual(saved.Select(v=>v.Sku))&&edited.Select(v=>v.Attributes["wattage"]).SequenceEqual(new[]{"35W","55W"}),"descriptive edits preserve variant IDs, axes and saved SKUs");
  Check(edited.All(v=>v.PurchasePrice==100&&v.SellingPrice==200&&v.CurrentStock==0)&&product.ParentProductCode==code&&product.Description=="Basic product description"&&product.DetailedDescription=="Independent detailed description","prices, stock, product code and independent descriptions remain unchanged");
  Check(await admin.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(b) ORDER BY variant_barcode_id)::text FROM variant_barcodes b JOIN product_variants v USING(tenant_id,variant_id) WHERE v.product_id=@id",new{id})==barcodeRowsBefore,"editing details preserves every saved barcode row and ID");
  Check((await new ScanRepository(factory).FindByCodeAsync(barcode))?.VariantId==saved[0].VariantId&&edited[0].PrimaryBarcode==saved[0].PrimaryBarcode,"manufacturer barcode retains its exact variant mapping");
  Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions")==movementsBefore&&await admin.ExecuteScalarAsync<long>("SELECT last_number FROM catalog_prefix_registry WHERE tenant_id=1 AND profile='BIKE_ACCESSORIES' AND category_id=@category AND prefix=split_part(@code,'-',1)",new{category,code})==counterBefore,"specification edits neither post inventory nor allocate new SKU numbers");
  rows[0].Attributes["shell material"]="Duplicate";
  await Reject(()=>service.UpdateAsync(id,new(){ParentProductCode=code,ProductName="Descriptive headlight",CategoryId=category,HasVariants=true,TrackingType="QUANTITY",IsActive=true,RegistrationDefaults=defaults,Variants=rows}),"backend rejects case-insensitive duplicate detail names");rows[0].Attributes.Remove("shell material");
  rows[0].Attributes["Unfinished"]="";await Reject(()=>service.UpdateAsync(id,new(){ParentProductCode=code,ProductName="Descriptive headlight",CategoryId=category,HasVariants=true,TrackingType="QUANTITY",IsActive=true,RegistrationDefaults=defaults,Variants=rows}),"backend rejects incomplete details");rows[0].Attributes.Remove("Unfinished");
  var staleProfile=new DbConnectionFactory(config,new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL"});Check(!(await new VariantRepository(staleProfile).GetAsync(id,null,false)).Any(),"another shop profile cannot read specification data");
  var otherScope=new BusinessScope{TenantId=999999,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="BIKE_ACCESSORIES"};var otherFactory=new DbConnectionFactory(config,otherScope);
  Check(!(await new VariantRepository(otherFactory).GetAsync(id,null,false)).Any(),"foreign tenant cannot read descriptive specifications");
  var otherService=new ProductService(new ProductRepository(otherFactory),new CategoryRepository(otherFactory),new BrandRepository(otherFactory));Check(!await otherService.UpdateAsync(id,new(){ProductName="Foreign edit",CategoryId=category}),"foreign tenant cannot update the product or its details");
  var emptyRequest=Guid.NewGuid();var emptyCode=await c.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@category,@emptyRequest,NULL)",new{category,emptyRequest});var emptyId=await service.CreateAsync(new(){RegistrationRequestId=emptyRequest,ParentProductCode=emptyCode,ProductName="Product without optional details",CategoryId=category,TrackingType="QUANTITY"});Check((await variants.GetAsync(emptyId,null,false)).Single().Attributes.Count==0,"optional empty specifications save without records or mandatory fields");
  Console.WriteLine("PASS: real PostgreSQL specification create/edit round trips use the existing product API/repositories; no live database touched.");
 }
 static async Task Reject(Func<Task> action,string message){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){Check(true,message);return;}throw new Exception("Expected validation rejection: "+message);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
