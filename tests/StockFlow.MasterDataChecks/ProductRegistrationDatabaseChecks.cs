using Dapper;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class ProductRegistrationDatabaseChecks
{
 public static async Task Run(DbConnectionFactory factory)
 {
  var repo = new ProductRepository(factory);
  var service = new ProductService(repo,new CategoryRepository(factory),new BrandRepository(factory));
  var variants = new VariantRepository(factory);
  var request = new CreateProductRequest { ProductName="Pilot inverter", CategoryId=1, TrackingType="SERIAL", HasVariants=true, WarrantyMonths=24, Variants=[
   new(){VariantName="1100 VA / 12 V",Sku="PILOT-INV-1100",SellingPrice=6000,PurchasePrice=5000,WarrantyMonths=24,BarcodeSource="MANUFACTURER",PrimaryBarcode="PILOT-INV-1100-EXISTING",Attributes=new(){["Rating"]="1100 VA",["Voltage"]="12 V"}},
   new(){VariantName="1500 VA / 24 V",Sku="PILOT-INV-1500",SellingPrice=8000,BarcodeSource="INTERNAL",Attributes=new(){["Rating"]="1500 VA",["Voltage"]="24 V"}}] };
  var id = await service.CreateAsync(request);
  var rows = (await variants.GetAsync(id,null,false)).ToList();
  Check(rows.Count==2 && rows.All(v=>v.IsSerialTracked) && rows.Any(v=>v.Attributes.GetValueOrDefault("Rating")=="1100 VA"),"one registration saves exactly two serialized electronic variants and specifications");
  Check(rows.All(v=>v.CurrentStock==0),"catalog registration does not receive stock");
  var original = rows.Single(v=>v.Sku=="PILOT-INV-1100");
  var edit = new UpdateProductRequest {ProductName="Updated pilot inverter",CategoryId=1,TrackingType="SERIAL",HasVariants=true,IsActive=true,Variants=[
   new(){VariantId=original.VariantId,VariantName=original.VariantName,Sku=original.Sku,BarcodeSource="MANUFACTURER",PrimaryBarcode=original.PrimaryBarcode,SellingPrice=6100,Attributes=original.Attributes},
   new(){VariantName="Duplicate SKU",Sku=original.Sku,BarcodeSource="INTERNAL"}]};
  await Reject(()=>service.UpdateAsync(id,edit));
  Check((await repo.GetByIdAsync(id))!.ProductName=="Pilot inverter" && (await variants.GetAsync(id,null,false)).Single(v=>v.VariantId==original.VariantId).SellingPrice==6000,"failed variant saves roll back parent metadata and prices");
  request.ProductName="Failed inverter";
  await Reject(()=>service.CreateAsync(request));
  Check(!(await repo.GetAllAsync()).Any(p=>p.ProductName=="Failed inverter"),"failed new registration leaves no partial product");
  var single = await service.CreateAsync(new(){ProductName="Pilot geyser",CategoryId=1,TrackingType="SERIAL",Variants=[new(){VariantName="DEFAULT",Sku="PILOT-GEYSER",BarcodeSource="INTERNAL",Attributes=new(){["Tank capacity"]="25 L",["Power"]="2000 W"}}]});
  var geyser = (await variants.GetAsync(single,null,false)).Single();
  var barcode = geyser.PrimaryBarcode;
  await service.UpdateAsync(single,new(){ProductName="Pilot geyser",CategoryId=1,TrackingType="SERIAL",IsActive=true,Variants=[new(){VariantId=geyser.VariantId,VariantName="DEFAULT",Sku=geyser.Sku,BarcodeSource="INTERNAL",Attributes=geyser.Attributes,SellingPrice=7000}]});
  Check((await variants.GetAsync(single,null,false)).Single().PrimaryBarcode==barcode,"editing registration retains generated identifiers");
  await using var c = factory.CreateConnection();
  Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id IN (@id,@single)",new{id,single})==0,"electronic catalog edits leave movement ledger unchanged");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static async Task Reject(Func<Task> action){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){return;}catch(Npgsql.PostgresException){return;}throw new Exception("Expected registration rejection");}
}
