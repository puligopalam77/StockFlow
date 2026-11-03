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
internal static class ProductIdentityDatabaseChecks
{
 public static async Task Run()
 {
  const string cs="Host=127.0.0.1;Port=55439;Database=product_registration_066_test;Username=postgres";
  await using var admin=new NpgsqlConnection(cs);await admin.OpenAsync();
  if(await admin.ExecuteScalarAsync<string>("SELECT current_database()")!="product_registration_066_test")throw new Exception("Isolated test database required");
  await admin.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','BIKE_ACCESSORIES',false)");
  var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="BIKE_ACCESSORIES",CatalogRegistrationEnhancementsAvailable=true,ProductImageGalleryAvailable=true,ProfileDataVisibilityAvailable=true};
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
  var factory=new DbConnectionFactory(config,scope);var repo=new ProductRepository(factory);var variants=new VariantRepository(factory);var categories=new CategoryRepository(factory);var service=new ProductService(repo,categories,new BrandRepository(factory),scope,new CatalogSchemaRepository(factory,categories),variants);
  var category=await admin.ExecuteScalarAsync<int>("INSERT INTO categories(tenant_id,business_profile_code,category_name) VALUES(1,'BIKE_ACCESSORIES','Registration identity checks') RETURNING category_id");
  var supplier=await admin.ExecuteScalarAsync<int>("INSERT INTO suppliers(tenant_id,business_profile_code,supplier_name,country_code) VALUES(1,'BIKE_ACCESSORIES','Registration supplier checks','IN') RETURNING supplier_id");
  var original=JsonSerializer.Serialize(new CreateProductRequest{RegistrationRequestId=Guid.NewGuid(),ParentProductCode="SF-BRK-066",ProductName="Registration brake item",CategoryId=category,TrackingType="QUANTITY",ProductStatus="ACTIVE",RegistrationDefaults=new(){PurchasePrice=100,SellingPrice=200,Mrp=250,PreferredSupplierId=supplier}});
  var id=await service.CreateAsync(JsonSerializer.Deserialize<CreateProductRequest>(original)!);
  var item=(await variants.GetAsync(id,null,false)).Single();
  Check(item.Sku=="SF-BRK-066"&&item.IsDefault&&item.CurrentStock==0,"simple product creates a matching default SKU with zero stock");
  Check(item.PurchasePrice==100&&item.SellingPrice==200&&item.UseDefaultSellingPrice,"new default SKU inherits product reference prices");
  Check(await service.CreateAsync(JsonSerializer.Deserialize<CreateProductRequest>(original)!)==id,"same request retry returns original product without duplicate variants");
  var changed=JsonSerializer.Deserialize<CreateProductRequest>(original)!;changed.ProductName="Changed payload";await Reject(()=>service.CreateAsync(changed),"request ID cannot be reused with a different payload");
  var variantRequest=new CreateProductRequest{RegistrationRequestId=Guid.NewGuid(),ParentProductCode="SF-HELM-066",ProductName="Registration variant item",CategoryId=category,TrackingType="QUANTITY",HasVariants=true,RegistrationDefaults=new(){PurchasePrice=50,SellingPrice=150,Mrp=250},Variants=[new(){VariantName="Black / M",Sku="SF-HELM-066-BLK-M",UseDefaultPurchasePrice=true,UseDefaultSellingPrice=true,Attributes=new(){["color"]="Black",["size"]="M"},PrimaryBarcode="REG-066-MANUFACTURER",BarcodeSource="MANUFACTURER",SupplierIdentifiers=[new(){SupplierId=supplier,SupplierSku="VENDOR-066-M",ExpectedPurchaseCost=45,PackConversion=10,PurchaseUnit="BOX",LeadTimeDays=3,IsPreferred=true}]},new(){VariantName="Black / L",Sku="SF-HELM-066-BLK-L",PurchasePrice=60,SellingPrice=175,Attributes=new(){["color"]="Black",["size"]="L"}}]};
  variantRequest.RegistrationDefaults!.VariantAxes=["color","size"];
  variantRequest.VehicleFitments=[new(){Manufacturer="Honda",Model="Activa 6G",YearFrom=2020,YearTo=2024,Notes="Verified common mounting"},new(){Manufacturer="Honda",Model="Dio",Notes="Verified common mounting"}];
  variantRequest.Variants![0].VehicleFitments=[new(){Manufacturer="Honda",Model="Activa 6G",Variant="Deluxe",YearFrom=2022,Notes="Verified M-only fitment"}];
  var multi=await service.CreateAsync(variantRequest);var configured=(await variants.GetAsync(multi,null,false)).ToList();
  Check(configured.Count==2&&configured.All(v=>v.CurrentStock==0),"variant registration creates exactly the requested SKUs and no stock");
  Check(configured.Single(v=>v.Sku.EndsWith("-M")).SupplierIdentifiers?.Single().PackConversion==10,"supplier purchasing terms round-trip on the existing mapping");
  Check((await service.GetByIdAsync(multi))?.VehicleFitments?.Count==2&&configured.Single(v=>v.Sku.EndsWith("-M")).VehicleFitments?.Single().Variant=="Deluxe","verified product and SKU fitments persist separately from categories");
  Check((await variants.GetAsync(null,"Honda Activa",true)).Count(v=>v.ProductId==multi)==2,"vehicle search resolves every exact SKU covered by a product fitment");
  var scan=await new ScanRepository(factory).FindByCodeAsync("REG-066-MANUFACTURER");Check(scan?.VariantId==configured.Single(v=>v.Sku.EndsWith("-M")).VariantId,"manufacturer barcode scan resolves the exact saved SKU");
  var beforeIds=configured.Select(v=>v.VariantId).Order().ToArray();
  await service.UpdateAsync(multi,new(){ProductName=variantRequest.ProductName,ParentProductCode=variantRequest.ParentProductCode,CategoryId=category,TrackingType="QUANTITY",HasVariants=true,IsActive=true,RegistrationDefaults=new(){PurchasePrice=55,SellingPrice=160,Mrp=250,VariantAxes=["color","size"]}});
  configured=(await variants.GetAsync(multi,null,false)).ToList();
  Check(configured.Single(v=>v.Sku.EndsWith("-M")).SellingPrice==160&&configured.Single(v=>v.Sku.EndsWith("-L")).SellingPrice==175,"default price edit updates inherited prices and preserves explicit overrides");
  Check(configured.Select(v=>v.VariantId).Order().SequenceEqual(beforeIds)&&configured.Single(v=>v.Sku.EndsWith("-M")).Barcodes.Contains("REG-066-MANUFACTURER"),"metadata/default changes preserve variant IDs, SKUs and barcode mappings");
  var inherited=configured.Single(v=>v.Sku.EndsWith("-M"));await service.UpdateAsync(multi,new(){ProductName=variantRequest.ProductName,ParentProductCode=variantRequest.ParentProductCode,CategoryId=category,TrackingType="QUANTITY",HasVariants=true,IsActive=true,Variants=[new(){VariantId=inherited.VariantId,VariantName=inherited.VariantName,Sku=inherited.Sku,Attributes=inherited.Attributes,UseDefaultPurchasePrice=true,UseDefaultSellingPrice=true,PurchasePrice=999,SellingPrice=999}]});
  Check((await variants.GetAsync(multi,null,false)).Single(v=>v.VariantId==inherited.VariantId).SellingPrice==160,"partial variant edits resolve stored defaults instead of trusting supplied inherited prices");
  await Reject(()=>repo.SaveRegistrationAsync(new(){ProductName="Atomic failure 066",CategoryId=category,HasVariants=true},[new(){VariantName="one",Sku="ATOMIC-066-ONE"},new(){VariantName="duplicate",Sku=item.Sku}]),"failed second variant rolls back the complete registration");
  Check(!await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM products WHERE product_name='Atomic failure 066')"),"failed registration leaves no product or default SKU behind");
  var found=(await variants.GetAsync(null,"Registration supplier",true)).ToList();
  Check((await variants.GetAsync(null,"VENDOR-066-M",true)).Any(v=>v.ProductId==multi)&& (await variants.GetAsync(null,"SF-HELM-066",true)).Count(v=>v.ProductId==multi)==2,"purchase/billing search returns exact variants for SKU and supplier references");
  await Reject(()=>service.CreateAsync(new(){ParentProductCode="FOREIGN-SUPPLIER-066",ProductName="Foreign supplier test",CategoryId=category,TrackingType="QUANTITY",RegistrationDefaults=new(){PreferredSupplierId=int.MaxValue}}),"invalid preferred supplier cannot be attached");
  Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=ANY(@Ids)",new{Ids=new[]{id,multi}})==0,"registration and price edits leave stock movements unchanged");
  var sample=await admin.QuerySingleAsync<(int ProductId,long VariantId)>("SELECT p.product_id ProductId,v.variant_id VariantId FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE EXISTS(SELECT 1 FROM inventory_transactions it WHERE it.tenant_id=v.tenant_id AND it.variant_id=v.variant_id) LIMIT 1");
  await Reject(()=>admin.ExecuteAsync("UPDATE product_variants SET sku='FORBIDDEN-SKU-066' WHERE variant_id=@VariantId",new{sample.VariantId}),"used SKU cannot be overwritten even by direct SQL");
  await Reject(()=>admin.ExecuteAsync("UPDATE products SET parent_product_code='FORBIDDEN-FAMILY-066',unit_of_measure='BOX' WHERE product_id=@ProductId",new{sample.ProductId}),"used parent product code and stock unit are protected");
  var schema=await new CatalogSchemaRepository(factory,categories).GetAsync();var rules=CategoryFieldInheritance.Fields(schema,38);var attrs=new Dictionary<string,string>();foreach(var rule in rules.Where(r=>r.Required)){var definition=schema.Attributes.Single(a=>a.Code==rule.Code);attrs[rule.Code]=rule.DefaultValue??(definition.Type=="SELECT"?definition.Options.First():definition.Type=="NUMBER"?"1":definition.Type=="BOOLEAN"?"true":"Verified");}
  var configuredAxes=rules.Where(r=>r.VariantAxis).Select(r=>r.Code).ToList();configuredAxes.Add("edition");
  var custom=await service.CreateAsync(new(){ParentProductCode="SF-CUSTOM-066",ProductName="Configured custom edition",CategoryId=38,TrackingType="QUANTITY",HasVariants=true,RegistrationDefaults=new(){VariantAxes=configuredAxes},Variants=[new(){VariantName="Verified / Retail",Sku="CUSTOM-066-RETAIL",Attributes=new(attrs){["edition"]="Retail"}},new(){VariantName="Verified / Touring",Sku="CUSTOM-066-TOURING",Attributes=new(attrs){["edition"]="Touring"}}]});
  Check((await variants.GetAsync(custom,null,false)).Count()==2,"custom axis extends category axes without turning common specifications into variants");
  var otherScope=new BusinessScope{TenantId=999999,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="BIKE_ACCESSORIES"};await using var other=new DbConnectionFactory(config,otherScope).CreateConnection();
  Check(await other.ExecuteScalarAsync<int>("SELECT count(*) FROM products WHERE product_id=@id",new{id=multi})==0&&await other.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_models")==0&&await other.ExecuteScalarAsync<int>("SELECT count(*) FROM product_vehicle_fitments")==0,"foreign shop cannot see product, SKU fitments or vehicle master rows");
  await Reject(()=>admin.ExecuteAsync("INSERT INTO inventory_transactions(product_id,transaction_type,quantity,created_by) VALUES(@multi,'ADJUSTMENT_IN',1,1)",new{multi}),"parent family cannot silently choose a SKU for stock posting");
  var purchasing=new PurchaseRepository(factory);var orderId=await purchasing.CreatePurchaseAsync(new(){SupplierId=supplier,LocationId=1,PurchaseDate=DateTime.Today,Items=[new(){ProductId=id,VariantId=item.VariantId,Quantity=3,PurchasePrice=90}]},1);
  Check((await variants.GetAsync(id,null,false)).Single().CurrentStock==0,"purchase order creation does not add stock");
  var order=(await purchasing.GetByIdAsync(orderId))!;var receiving=new PurchaseReceiptRepository(factory);var receiptRequest=new StockFlow.Application.DTOs.Purchases.ReceivePurchaseRequest{RequestId=Guid.NewGuid(),LocationId=1,ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=order.Items.Single().PurchaseItemId,Quantity=3}]};
  var receipt=await receiving.ReceiveAsync(orderId,receiptRequest,1);Check(await receiving.ReceiveAsync(orderId,receiptRequest,1)==receipt&&(await variants.GetAsync(id,null,false)).Single().CurrentStock==3,"posted receipt updates the exact default SKU once, including retry");
  var internalBarcode=await variants.GenerateBarcodeAsync(item.VariantId,1);Check(await variants.GenerateBarcodeAsync(item.VariantId,1)==internalBarcode&&(await variants.GetAsync(id,null,false)).Single().CurrentStock==3,"barcode generation/reuse preserves stock and the saved code");
  var billing=new SalesRepository(factory);var saleRequest=new StockFlow.Application.DTOs.Sales.CreateSaleRequest{LocationId=1,RequestId=Guid.NewGuid(),Items=[new(){ProductId=id,VariantId=item.VariantId,Quantity=1,SellingPrice=200}]};var quote=await billing.QuoteAsync(saleRequest);saleRequest.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];var saleId=await billing.CreateSaleAsync(saleRequest,1);
  Check((await variants.GetAsync(id,null,false)).Single().CurrentStock==2,"finalized billing deducts only the selected default SKU");
  await service.UpdateAsync(id,new(){ParentProductCode="SF-BRK-066",ProductName="Registration brake item",CategoryId=category,TrackingType="QUANTITY",IsActive=true,RegistrationDefaults=new(){PurchasePrice=110,SellingPrice=210,Mrp=250}});
  Check((await billing.GetByIdAsync(saleId))!.Lines.Single().SellingPrice==200&&(await purchasing.GetByIdAsync(orderId))!.Items.Single().PurchasePrice==90&&(await variants.GetAsync(id,null,false)).Single().CurrentStock==2,"later catalog price changes preserve invoice/purchase snapshots and stock quantities");
  Console.WriteLine("PASS: isolated PostgreSQL product registration integration checks; no live database accessed.");
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 static async Task Reject(Func<Task> action,string message){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){Check(true,message);return;}catch(PostgresException ex) when(ex.SqlState is "23514" or "23505" or "42501"){Check(true,message);return;}throw new Exception("Expected rejection: "+message);}
}

