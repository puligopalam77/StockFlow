using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class IdentifierIntegrationDatabaseChecks
{
 const string Cs="Host=127.0.0.1;Port=55439;Database=identifier_integration_test;Username=postgres";
 public static async Task Run()
 {
  await using var admin=new NpgsqlConnection(Cs);await admin.OpenAsync();
  Check(await admin.ExecuteScalarAsync<string>("SELECT current_database()") == "identifier_integration_test","isolated database only");
  // This database is an exact clone of the prior isolated 067 database, with only 068 applied.
  await using(var original=new NpgsqlConnection(Cs.Replace("identifier_integration_test","sequential_sku_067_v2_test")))
  {
   await original.OpenAsync();
   foreach(var table in new[]{"products","product_variants","variant_barcodes","product_units","inventory_transactions","purchases","purchase_items","sales","sale_items"})
   {
    var expression=table=="product_units"?"to_jsonb(t)-'serial_source'":"to_jsonb(t)";
    Check(await original.ExecuteScalarAsync<string>($"SELECT jsonb_agg({expression} ORDER BY {expression})::text FROM {table} t")==await admin.ExecuteScalarAsync<string>($"SELECT jsonb_agg({expression} ORDER BY {expression})::text FROM {table} t"),"068 preserves all historical "+table+" values");
   }
  }
  Check(!await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE serial_source<>'LEGACY_UNKNOWN')"),"existing serial provenance is unverified, never guessed from a barcode");
  await admin.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1; SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false)");
  var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true,ProductImageGalleryAvailable=true};
  var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",Cs}}).Build();
  var factory=new DbConnectionFactory(config,scope);var products=new ProductRepository(factory);var variants=new VariantRepository(factory);var categories=new CategoryRepository(factory);
  var service=new ProductService(products,categories,new BrandRepository(factory),scope,null,variants);
  var cat=await categories.CreateAsync(new(){CategoryName="Identifier integration "+Guid.NewGuid().ToString("N")[..6],IsActive=true});
  async Task<(int Id,VariantResponse Variant)> Product(string name,bool tracked)
  {
   await using var c=factory.CreateConnection();var rid=Guid.NewGuid();var code=await c.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@cat,@rid,NULL)",new{cat,rid});
   var id=await service.CreateAsync(new(){RegistrationRequestId=rid,ParentProductCode=code,ProductName=name,CategoryId=cat,TrackingType=tracked?"SERIAL":"QUANTITY",PurchasePrice=100,SellingPrice=200,WarrantyMonths=tracked?12:0});
   var variant=(await variants.GetAsync(id,null,false)).Single();Check(variant.Sku==code&&variant.CurrentStock==0&&variant.IsSerialTracked==tracked,"registration preserves tracking and simple product/default SKU, with zero stock");return(id,variant);
  }
  var beforeUnits=await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units");var beforeMovements=await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions");
  var serial=await Product("Serial identifier integration",true);var quantity=await Product("Quantity identifier integration",false);
  Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units")==beforeUnits&&await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions")==beforeMovements,"registration creates no physical units or inventory movements");
  await admin.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source) VALUES(@VariantId,'SFN0000000001','MANUFACTURER')",quantity.Variant);
  async Task<string[]> Allocate(Guid rid,int count=2,long? variant=null)
  {await using var c=factory.CreateConnection();return (await c.ExecuteScalarAsync<string[]>("SELECT sf_reserve_unit_identifiers(@variant,@rid,@count)",new{variant=variant??serial.Variant.VariantId,rid,count}))!;}
  var request=Guid.NewGuid();var codes=await Allocate(request);
  Check(!codes.Contains("SFN0000000001")&&(await Allocate(request)).SequenceEqual(codes),"server allocator detects existing identifiers and retries return the same committed IDs");
  await Reject(()=>Allocate(request,3),"changed allocation retry rejected");
  await Reject(()=>Allocate(Guid.NewGuid(),1,quantity.Variant.VariantId),"quantity product cannot allocate physical unit IDs");
  var parallel=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Allocate(Guid.NewGuid())));
  Check(parallel.SelectMany(x=>x).Distinct().Count()==24,"concurrent server requests allocate distinct permanent unit IDs");
  Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units")==beforeUnits&&await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions")==beforeMovements,"identifier allocation does not receive stock or create physical units");
  var supplier=await admin.ExecuteScalarAsync<int>("INSERT INTO suppliers(tenant_id,business_profile_code,supplier_name,country_code) VALUES(1,'ELECTRICAL','Identifier integration supplier','IN') RETURNING supplier_id");
  var purchases=new PurchaseRepository(factory);var receipts=new PurchaseReceiptRepository(factory);
  async Task<(long Id,PurchaseResponse Po)> Order((int Id,VariantResponse Variant) item,int count)
  {var id=await purchases.CreatePurchaseAsync(new(){LocationId=1,SupplierId=supplier,PurchaseDate=DateTime.Today,Items=[new(){ProductId=item.Id,VariantId=item.Variant.VariantId,Quantity=count,PurchasePrice=100}]},1);return(id,(await purchases.GetByIdAsync(id))!);}
  var order=await Order(serial,3);var simpleOrder=await Order(quantity,3);
  Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units")==beforeUnits,"purchase orders create neither serial units nor stock");
  var receiptRequest=new ReceivePurchaseRequest{LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=order.Po.Items.Single().PurchaseItemId,Quantity=2,DamagedQuantity=1,SerialNumbers=["MFR-REAL-145",codes[0]],DamagedSerialNumbers=[codes[1]]}]};
  var incomplete=new ReceivePurchaseRequest{LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=order.Po.Items.Single().PurchaseItemId,Quantity=2,SerialNumbers=["MFR-REAL-145"]}]};
  await Reject(()=>receipts.ReceiveAsync(order.Id,incomplete,1),"missing serials reject receipt atomically");
  Check((await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock==0,"failed receipt leaves stock unchanged");
  var receipt=await receipts.ReceiveAsync(order.Id,receiptRequest,1);Check(await receipts.ReceiveAsync(order.Id,receiptRequest,1)==receipt,"receipt retry cannot receive the same units twice");
  var inventory=new InventoryRepository(factory);var units=(await inventory.GetUnitsAsync(serial.Id,null,false,0)).ToArray();
  var manufacturer=units.Single(u=>u.SerialNumber=="MFR-REAL-145");var internalUnit=units.Single(u=>u.SerialNumber==codes[0]);
  Check(manufacturer.ManufacturerSerialNumber=="MFR-REAL-145"&&internalUnit.ManufacturerSerialNumber is null&&internalUnit.InternalUnitIdentifier==codes[0],"manufacturer serial and generated internal unit ID have distinct provenance");
  Check(units.Length==3&&units.Single(u=>u.SerialNumber==codes[1]).UnitStatus=="DAMAGED"&&(await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock==2,"accepted and damaged physical units reconcile with exact SKU stock");
  var unitBarcode=await variants.SaveUnitBarcodeAsync(manufacturer.ProductUnitId,new(){GenerateInternal=true},1);
  Check(await variants.SaveUnitBarcodeAsync(manufacturer.ProductUnitId,new(){GenerateInternal=true},1)==unitBarcode,"unit barcode allocation is stable and idempotent");
  var scan=new ScanRepository(factory);var scanned=(await scan.FindByCodeAsync(unitBarcode!))!;
  Check(scanned.ProductUnitId==manufacturer.ProductUnitId&&scanned.SerialNumber=="MFR-REAL-145"&&scanned.ManufacturerSerialNumber=="MFR-REAL-145"&&unitBarcode!=scanned.SerialNumber,"printing/internal barcode generation never overwrites the manufacturer serial");
  await Reject(()=>admin.ExecuteAsync("UPDATE product_units SET serial_number='REPLACED' WHERE product_unit_id=@ProductUnitId",manufacturer),"saved serial number cannot be silently replaced");
  await Reject(()=>admin.ExecuteAsync("UPDATE product_units SET internal_barcode='REPLACED' WHERE product_unit_id=@ProductUnitId",manufacturer),"saved unit barcode cannot be silently replaced");
  await Reject(()=>service.UpdateAsync(serial.Id,new(){CategoryId=cat,ProductName="Tracking change",TrackingType="QUANTITY",IsActive=true}),"tracking cannot change after stock or transaction history");
  var duplicateOrder=await Order(serial,1);await Reject(()=>receipts.ReceiveAsync(duplicateOrder.Id,new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=duplicateOrder.Po.Items.Single().PurchaseItemId,Quantity=1,SerialNumbers=["mfr-real-145"]}]},1),"duplicate manufacturer serial rejected across receipts");
  await receipts.ReceiveAsync(simpleOrder.Id,new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=simpleOrder.Po.Items.Single().PurchaseItemId,Quantity=3}]},1);
  Check(!(await inventory.GetUnitsAsync(quantity.Id,null,false,0)).Any()&&(await variants.GetAsync(quantity.Id,null,false)).Single().CurrentStock==3,"quantity receipt adds SKU quantity without physical serial records");
  var noBarcode=await Product("Quantity SKU barcode allocation",false);var noBarcodeOrder=await Order(noBarcode,2);
  var noBarcodeReceipt=await receipts.ReceiveAsync(noBarcodeOrder.Id,new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=noBarcodeOrder.Po.Items.Single().PurchaseItemId,Quantity=2}]},1);
  var management=new BarcodeManagementRepository(factory,new BarcodeLotRepository(factory));var workspace=(await management.ReadAsync("receipt",receipt))!;
  var missing=(await management.ReadAsync("receipt",noBarcodeReceipt))!;var prepared=(await management.PrepareAsync("receipt",noBarcodeReceipt,missing.Rows.Select(r=>r.Key).ToArray(),1))!;
  var skuBarcode=prepared.Rows.First().Barcode;
  Check(skuBarcode is not null&&prepared.Rows.All(r=>r.Barcode==skuBarcode&&r.SerialNumber is null&&r.BarcodeSource=="INTERNAL")&&(await variants.GetAsync(noBarcode.Id,null,false)).Single().CurrentStock==2,"new quantity labels share one saved SKU barcode without creating serial numbers or stock");
  Check((await management.PrepareAsync("receipt",noBarcodeReceipt,prepared.Rows.Select(r=>r.Key).ToArray(),1))!.Rows.All(r=>r.Barcode==skuBarcode)&&(await scan.FindByCodeAsync(skuBarcode!))?.VariantId==noBarcode.Variant.VariantId,"quantity barcode preparation is idempotent and scans the exact SKU");
  Check(workspace.Rows.Count==2&&workspace.Rows.Any(r=>r.SerialSource=="MANUFACTURER"&&r.SerialNumber=="MFR-REAL-145"&&r.Barcode==unitBarcode)&&workspace.Rows.Any(r=>r.SerialSource=="INTERNAL"),"barcode workspace distinguishes exact received units, their serial source and saved barcode");
  var stockBeforePrint=(await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock;
  var first=workspace.Rows.First();var print=new StockFlow.Application.Interfaces.BarcodePrintRequest{RequestId=Guid.NewGuid(),Items=[new(){Key=first.Key,Barcode=first.Barcode!,Copies=2}]};
  await management.PrintAsync("receipt",receipt,print,1);await management.PrintAsync("receipt",receipt,print,1);print.RequestId=Guid.NewGuid();print.Action="REPRINT";await management.PrintAsync("receipt",receipt,print,1);
  Check((await management.ReadAsync("receipt",receipt))!.Rows.First(r=>r.Key==first.Key).Barcode==first.Barcode&&(await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock==stockBeforePrint,"bulk copies and reprints reuse saved unit mapping and never change stock");
  Check((await products.GetAllAsync(true,codes[0])).Any(p=>p.ProductId==serial.Id)&&(await products.GetAllAsync(true,"SFN0000000001")).Any(p=>p.ProductId==quantity.Id),"product list finds serial/unit IDs and manufacturer barcode aliases without duplicate unit rows");
  var sales=new SalesRepository(factory);
  CreateSaleRequest Sale(long? unit)=>new(){LocationId=1,RequestId=Guid.NewGuid(),Items=[new(){ProductId=serial.Id,VariantId=serial.Variant.VariantId,ProductUnitId=unit,Quantity=1,SellingPrice=200}]};
  await Reject(()=>Sell(Sale(null)),"serial-tracked SKU requires exact physical unit");
  async Task<long> Sell(CreateSaleRequest sale){var quote=await sales.QuoteAsync(sale);sale.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];return await sales.CreateSaleAsync(sale,1);}
  var saleId=await Sell(Sale(manufacturer.ProductUnitId));await Reject(()=>Sell(Sale(manufacturer.ProductUnitId)),"same physical unit cannot be sold twice");
  var invoice=(await new InvoiceRepository(factory).GetInvoiceAsync(saleId))!;
  Check(invoice.Items.Single().SerialNumber=="MFR-REAL-145"&&invoice.Items.Single().ManufacturerSerialNumber=="MFR-REAL-145"&&(await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock==1,"finalized invoice keeps original serial and atomically deducts exact unit stock");
  var warranty=await new WarrantyRepository(factory).GetBySerialAsync("MFR-REAL-145");Check(warranty?.ProductUnitId==manufacturer.ProductUnitId&&warranty.ManufacturerSerialNumber=="MFR-REAL-145","warranty keeps the actual manufacturer serial and original unit reference");
  Check((await new WarrantyRepository(factory).GetBySerialAsync(unitBarcode!))?.ProductUnitId==manufacturer.ProductUnitId,"warranty barcode lookup resolves the original unit without replacing its serial");
  var internalSale=await Sell(Sale(internalUnit.ProductUnitId));var internalInvoice=(await new InvoiceRepository(factory).GetInvoiceAsync(internalSale))!;
  Check(internalInvoice.Items.Single().ManufacturerSerialNumber is null&&internalInvoice.Items.Single().InternalUnitIdentifier==codes[0],"invoice never presents generated unit ID as manufacturer serial");
  var line=(await sales.GetByIdAsync(saleId))!.Lines.Single();var returns=new OperationsRepository(factory);
  var returnRequest=new SaleReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="Identifier integration restock",Items=[new(){SaleItemId=line.SaleItemId,Quantity=1}]};var returned=await returns.ReturnSaleAsync(saleId,returnRequest,1);
  Check(await returns.ReturnSaleAsync(saleId,returnRequest,1)==returned&&(await inventory.GetUnitsAsync(serial.Id,null,true,0)).Single().ProductUnitId==manufacturer.ProductUnitId,"accepted return restores original physical unit once");
  returnRequest.RequestId=Guid.NewGuid();await Reject(()=>returns.ReturnSaleAsync(saleId,returnRequest,1),"duplicate physical unit return rejected");
  var damagedReturn=new SaleReturnRequest{LocationId=1,RequestId=Guid.NewGuid(),Notes="Identifier integration damaged",Items=[new(){SaleItemId=(await sales.GetByIdAsync(internalSale))!.Lines.Single().SaleItemId,Quantity=1,Disposition="DAMAGED"}]};await returns.ReturnSaleAsync(internalSale,damagedReturn,1);
  Check((await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock==1&&(await inventory.GetUnitsAsync(serial.Id,null,true,0)).Count()==1,"damaged return does not restore sellable stock");
  await Sell(new(){LocationId=1,RequestId=Guid.NewGuid(),Items=[new(){ProductId=quantity.Id,VariantId=quantity.Variant.VariantId,Quantity=2,SellingPrice=200}]});
  Check((await variants.GetAsync(quantity.Id,null,false)).Single().CurrentStock==1,"standard SKU billing deducts quantity without serial selection");
  scope.LocationId=2;Check(!(await inventory.GetUnitsAsync(serial.Id,null,false,0)).Any()&&await management.ReadAsync("receipt",receipt) is null&&await scan.FindByCodeAsync(unitBarcode!) is null,"foreign location cannot scan units or mix barcode print lots");scope.LocationId=1;
  scope.TenantId=999999;Check(await scan.FindByCodeAsync(unitBarcode!) is null,"foreign tenant cannot resolve physical identifiers");scope.TenantId=1;
  await admin.ExecuteAsync("UPDATE tenants SET business_profile_code='BIKE_ACCESSORIES' WHERE tenant_id=1");
  Check(await scan.FindByCodeAsync(unitBarcode!) is null,"stale profile cannot resolve other shop physical units");await Reject(()=>Allocate(Guid.NewGuid()),"stale shop cannot allocate new physical identifiers");
  Console.WriteLine("PASS: real receipt, barcode, billing, invoice, return and warranty integration. Only isolated database used.");
 }
 static async Task Reject(Func<Task> action,string message){try{await action();}catch(Exception e) when(e is System.ComponentModel.DataAnnotations.ValidationException or PostgresException or KeyNotFoundException){Check(true,message);return;}throw new Exception("Expected rejection: "+message);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
