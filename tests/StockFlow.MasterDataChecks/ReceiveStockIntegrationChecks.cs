using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class ReceiveStockIntegrationChecks
{
 public static async Task Run(){
  const string prefix="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
  var db="receive_stock_test_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(prefix+"postgres");await admin.OpenAsync();
  await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{
   await using var setup=new NpgsqlConnection(prefix+db);await setup.OpenAsync();
   foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
   await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");
   var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",prefix+db}}).Build();
   var factory=new DbConnectionFactory(config,scope);var variants=new VariantRepository(factory);var products=new ProductRepository(factory);
   var service=new ProductService(products,new CategoryRepository(factory),new BrandRepository(factory),scope,null,variants);
   async Task<(int Id,VariantResponse[] Rows)> Register(string name,int count=2,bool tracked=false){
    await using var c=factory.CreateConnection();var request=Guid.NewGuid();var code=await c.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(4,@request,NULL)",new{request});
    var codes=count>1?(await c.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@code,@request,@op,@count,NULL)",new{code,request,op=Guid.NewGuid(),count}))!:[code!];
    var id=await service.CreateAsync(new(){RegistrationRequestId=request,ParentProductCode=code,ProductName=name,CategoryId=4,TrackingType=tracked?"SERIAL":"QUANTITY",HasVariants=count>1,PurchasePrice=100,SellingPrice=200,
     RegistrationDefaults=count>1?new(){VariantAxes=["edition"],PurchasePrice=100,SellingPrice=200}:null,
     Variants=count>1?codes.Select((sku,i)=>new RegistrationVariantRequest{Sku=sku,VariantName="Edition "+i,Attributes=new(){["edition"]=i.ToString()},PurchasePrice=100,SellingPrice=200}).ToList():null});
    return(id,(await variants.GetAsync(id,null,false)).OrderBy(v=>v.VariantId).ToArray());
   }
   var quantity=await Register("Receiving integration editions");var serial=await Register("Receiving integration serial",1,true);
   var v=quantity.Rows[0];Check(quantity.Rows.All(v=>v.CurrentStock==0),"product registration adds no stock");
   Check((await variants.SearchForReceiptAsync(v.ParentProductCode!)).Count(p=>p.ProductId==quantity.Id)==2,"parent product search exposes exact registered variant SKUs, even at zero stock");
   var supplier=await new SupplierRepository(factory).CreateAsync(new(){SupplierName="Receiving integration supplier",CountryCode="IN",PaymentTermsDays=30});
   var purchases=new PurchaseRepository(factory);var receipts=new PurchaseReceiptRepository(factory);
   var poId=await purchases.CreatePurchaseAsync(new(){LocationId=1,SupplierId=supplier,PurchaseDate=DateTime.Today,Items=[new(){ProductId=quantity.Id,VariantId=v.VariantId,Quantity=5,PurchasePrice=100,TaxPercentage=18}]},1);
   Check((await variants.GetAsync(quantity.Id,null,false)).Single(p=>p.VariantId==v.VariantId).CurrentStock==0,"creating a purchase order does not receive stock");
   var direct=new CreatePurchaseRequest{LocationId=1,SupplierId=supplier,RequestId=Guid.NewGuid(),PurchaseDate=DateTime.Today,ReceivedDate=DateTime.Today,SupplierInvoiceDate=DateTime.Today,SupplierInvoiceNumber="RS-INVOICE-01",PaymentTermsDays=30,FreightAmount=30,OtherLandedCost=20,DiscountAmount=5,CompleteImmediately=true,
    InvoiceAttachmentName="supplier.pdf",InvoiceAttachmentContentType="application/pdf",InvoiceAttachmentData=System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\nTest invoice"),
    Items=[new(){ProductId=quantity.Id,VariantId=v.VariantId,Quantity=5,ReceivedQuantity=3,DamagedQuantity=2,PurchasePrice=100,DiscountAmount=10,TaxPercentage=18,Mrp=200,BatchNumber="LOT-RECEIVE-01",ManufacturingDate=DateTime.Today.AddDays(-30),ExpiryDate=DateTime.Today.AddYears(1)}]};
   var ids=await Task.WhenAll(purchases.CreatePurchaseAsync(direct,1),purchases.CreatePurchaseAsync(direct,1));Check(ids[0]==ids[1],"concurrent direct receipt retry creates one purchase and receipt");
   var posted=(await purchases.GetByIdAsync(ids[0]))!;var lines=await receipts.GetAsync(ids[0]);
   Check(lines.Count()==1&&posted.TotalAmount==623.2m&&posted.PaidAmount==0&&posted.SupplierOutstanding==623.2m,"receipt taxes/charges/discount and unpaid supplier balance are consistent once");
   Check(posted.PaymentTermsDays==30&&posted.InvoiceAttachmentName=="supplier.pdf"&&(await purchases.GetAttachmentAsync(ids[0]))!.Data.SequenceEqual(direct.InvoiceAttachmentData),"supplier terms and invoice attachment survive posting");
   Check(posted.Items.Single().VariantId==v.VariantId&&posted.Items.Single().PurchasePrice==100&&posted.Items.Single().BatchNumber=="LOT-RECEIVE-01","exact SKU, purchase cost and batch metadata remain on the receipt purchase");
   Check((await variants.GetAsync(quantity.Id,null,false)).Single(p=>p.VariantId==v.VariantId).CurrentStock==3&&(await variants.GetAsync(quantity.Id,null,false)).Single(p=>p.VariantId!=v.VariantId).CurrentStock==0,"only accepted units increase the selected variant stock");
   await using var runtime=factory.CreateConnection();var movements=await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=@Id",new{quantity.Id});
   await purchases.CreatePurchaseAsync(direct,1);Check(await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=@Id",new{quantity.Id})==movements,"same receipt retry never increases stock twice");
   var po=(await purchases.GetByIdAsync(poId))!;var partial=new ReceivePurchaseRequest{LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=po.Items.Single().PurchaseItemId,Quantity=2,DamagedQuantity=1}]};
   var receipt=await receipts.ReceiveAsync(poId,partial,1);Check(await receipts.ReceiveAsync(poId,partial,1)==receipt,"partial PO receipt retains its request identity");
   po=(await purchases.GetByIdAsync(poId))!;Check(po.Status=="PARTIALLY_RECEIVED"&&po.Items.Single().ReceivedQuantity==2&&po.Items.Single().DamagedQuantity==1&&po.Items.Single().Quantity==5,"partial receipt preserves ordered quantity and leaves two units outstanding");
   await Reject(()=>receipts.ReceiveAsync(poId,new(){LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=po.Items.Single().PurchaseItemId,Quantity=3}]},1),"over-receipt is rejected without changing the order");
   partial.Items[0].Quantity=1;
   await Reject(()=>receipts.ReceiveAsync(poId,partial,1),"same receipt identity with changed quantity is rejected");
   Check((await variants.GetAsync(quantity.Id,null,false)).Single(p=>p.VariantId==v.VariantId).CurrentStock==5,"changed receipt retry leaves accepted stock unchanged");
   var atomicOrder=await purchases.CreatePurchaseAsync(new(){LocationId=1,SupplierId=supplier,PurchaseDate=DateTime.Today,Items=[new(){ProductId=quantity.Id,VariantId=v.VariantId,Quantity=2,PurchasePrice=100},new(){ProductId=quantity.Id,VariantId=quantity.Rows[1].VariantId,Quantity=1,PurchasePrice=100}]},1);
   var atomic=(await purchases.GetByIdAsync(atomicOrder))!;
   var invalidReceipt=new ReceivePurchaseRequest{LocationId=1,RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=atomic.Items.Single(i=>i.VariantId==v.VariantId).PurchaseItemId,Quantity=1},new(){PurchaseItemId=atomic.Items.Single(i=>i.VariantId!=v.VariantId).PurchaseItemId,Quantity=2}]};
   var movementCount=await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=@Id",new{quantity.Id});
   await Reject(()=>receipts.ReceiveAsync(atomicOrder,invalidReceipt,1),"late invalid receipt line rejects entire multi-line transaction");
   Check(!(await receipts.GetAsync(atomicOrder)).Any()&&(await purchases.GetByIdAsync(atomicOrder))!.Items.All(i=>i.ReceivedQuantity==0)&&await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=@Id",new{quantity.Id})==movementCount,"failed multi-line receipt retains no receipt, received quantity or inventory movement");
   var tracked=new CreatePurchaseRequest{LocationId=1,SupplierId=supplier,RequestId=Guid.NewGuid(),PurchaseDate=DateTime.Today,ReceivedDate=DateTime.Today,CompleteImmediately=true,Items=[new(){ProductId=serial.Id,VariantId=serial.Rows[0].VariantId,Quantity=2,ReceivedQuantity=1,DamagedQuantity=1,PurchasePrice=100,SerialNumbers=["RECEIVE-MFR-01"],DamagedSerialNumbers=["RECEIVE-DAMAGED-01"]}]};
   var serialPurchase=await purchases.CreatePurchaseAsync(tracked,1);var units=(await new InventoryRepository(factory).GetUnitsAsync(serial.Id,null,false,0)).ToArray();
   Check(units.Count(u=>u.UnitStatus=="IN_STOCK")==1&&units.Count(u=>u.UnitStatus=="DAMAGED")==1,"serialized receipt records accepted and damaged units separately");
   var accepted=units.Single(u=>u.UnitStatus=="IN_STOCK");var barcode=await variants.SaveUnitBarcodeAsync(accepted.ProductUnitId,new(){GenerateInternal=true},1);
   Check(await variants.SaveUnitBarcodeAsync(accepted.ProductUnitId,new(){GenerateInternal=true},1)==barcode&&(await new ScanService(new ScanRepository(factory)).LookupAsync(barcode!))?.ProductUnitId==accepted.ProductUnitId,"barcode generation/reuse resolves the original physical unit");
   Check((await variants.GetAsync(serial.Id,null,false)).Single().CurrentStock==1,"barcode preparation does not add stock");
   var operations=new OperationsRepository(factory);var payment=new StockFlow.Application.DTOs.Operations.MoneyReceiptRequest{LocationId=1,RequestId=Guid.NewGuid(),PaymentMode="CASH",Amount=100};
   Check(await operations.AddSupplierPaymentAsync(ids[0],payment,1)==await operations.AddSupplierPaymentAsync(ids[0],payment,1)&&(await purchases.GetByIdAsync(ids[0]))!.SupplierOutstanding==523.2m,"existing supplier partial-payment workflow reduces the payable once");
   scope.LocationId=2;Check(!(await receipts.GetAsync(ids[0])).Any(),"foreign location cannot read the receipt");scope.LocationId=1;
   scope.TenantId=99999;Check(!(await variants.SearchForReceiptAsync("Receiving integration")).Any(),"foreign tenant cannot select receiving variants");scope.TenantId=1;
   await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='BIKE_ACCESSORIES' WHERE tenant_id=1");Check(!(await variants.SearchForReceiptAsync(v.Sku)).Any(),"stale shop context cannot receive another shop catalog");
   Console.WriteLine("PASS: Receive Stock database integration; disposable clone only.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
 }
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
 static async Task Reject(Func<Task> action,string text){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){Check(true,text);return;}throw new Exception(text);}
}
