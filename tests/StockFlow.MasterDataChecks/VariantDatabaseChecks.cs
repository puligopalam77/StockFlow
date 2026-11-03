using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class VariantDatabaseChecks
{
    public static async Task Run()
    {
        var database = "variants_test_" + Guid.NewGuid().ToString("N");
        var cs = $"Host=127.0.0.1;Port=55441;Database={database};Username=postgres";
        await using (var admin = new NpgsqlConnection(cs.Replace($"Database={database}", "Database=postgres")))
        { await admin.OpenAsync(); await admin.ExecuteAsync("CREATE DATABASE " + database); }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName,"database/scripts"),"*.sql").Where(f => !Path.GetFileName(f).StartsWith("001") && !Path.GetFileName(f).StartsWith("006")).Order())
            await c.ExecuteAsync(await File.ReadAllTextAsync(file));
        foreach (var file in Directory.GetFiles(Path.Combine(root.FullName,"database/migrations"),"*.sql").Where(f => string.CompareOrdinal(Path.GetFileName(f),"018")<0).Order())
            await c.ExecuteAsync(await File.ReadAllTextAsync(file));
        await c.ExecuteAsync("""
            INSERT INTO roles(role_name) VALUES('Variant tester');
            INSERT INTO users(role_id,full_name,username,password_hash) SELECT role_id,'Tester','variant-test','unused' FROM roles WHERE role_name='Variant tester';
            INSERT INTO categories(category_name) VALUES('General');
            INSERT INTO suppliers(supplier_name) VALUES('Supplier');
            INSERT INTO products(category_id,product_name,sku,barcode,selling_price) VALUES(1,'Shirt','SHIRT','LEGACY-SHIRT',100);
            INSERT INTO inventory_transactions(product_id,transaction_type,quantity,created_by) VALUES(1,'PURCHASE',5,1);
            """);
        await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/018_common_inventory_variants.sql")));
        await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/019_purchase_receipts.sql")));
        Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock") == 5,"legacy stock backfilled without changing balance");
        var factory = new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = cs }).Build());
        var variants = new VariantRepository(factory); var purchases = new PurchaseRepository(factory); var sales = new SalesRepository(factory); var scans = new ScanRepository(factory);
        var medium = await variants.SaveAsync(null,new VariantRequest {ProductId=1,VariantName="Black / M",Sku="SHIRT-M",SellingPrice=120,TaxPercentage=5,Attributes=new(){{"Size","M"},{"Color","Black"}}},1);
        var large = await variants.SaveAsync(null,new VariantRequest {ProductId=1,VariantName="Black / L",Sku="SHIRT-L",SellingPrice=150,TaxPercentage=12},1);
        var serial = await variants.SaveAsync(null,new VariantRequest {ProductId=1,VariantName="Collectible",Sku="SERIAL",IsSerialTracked=true,SellingPrice=200,WarrantyMonths=12},1);
        var code = await variants.GenerateBarcodeAsync(medium,1);
        Check((await variants.GetAsync(1,"M",true)).Any(v=>v.VariantId==medium&&v.Attributes["Size"]=="M"&&v.Barcodes.Contains(code)),"catalog returns attributes and authoritative barcode mappings");
        Check(code == await variants.GenerateBarcodeAsync(medium,1),"barcode reprint reuses identifier");
        var purchase = await purchases.CreatePurchaseAsync(new CreatePurchaseRequest {SupplierId=1,PurchaseDate=DateTime.Today,CompleteImmediately=true,Items=[
            new(){ProductId=1,VariantId=medium,Quantity=2,PurchasePrice=50,DiscountAmount=10,TaxPercentage=5},
            new(){ProductId=1,VariantId=large,Quantity=3,PurchasePrice=60},
            new(){ProductId=1,VariantId=serial,Quantity=2,PurchasePrice=80,SerialNumbers=["SER-1","SER-2"]}]},1);
        Check((await purchases.GetByIdAsync(purchase))!.TotalAmount==434.50m,"mixed receipt preserves variant and line discount");
        Check((await scans.FindByCodeAsync(code))!.VariantId==medium,"shared barcode resolves exact variant");
        var unit=(await scans.FindByCodeAsync("SER-1"))!;
        Check(unit.VariantId==serial && unit.ProductUnitId.HasValue,"serial resolves exact variant and physical unit");
        var unitCode=await variants.SaveUnitBarcodeAsync(unit.ProductUnitId!.Value,new(){ManufacturerBarcode="MANUFACTURER-1",GenerateInternal=true},1);
        Check((await scans.FindByCodeAsync(unitCode!))!.ProductUnitId==unit.ProductUnitId && (await scans.FindByCodeAsync("MANUFACTURER-1"))!.ProductUnitId==unit.ProductUnitId,"serial and separate manufacturer/internal labels identify same unit");
        var request=new CreateSaleRequest {RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,VariantId=medium,Quantity=1,SellingPrice=120},new(){ProductId=1,VariantId=large,Quantity=1,SellingPrice=150},new(){ProductId=1,VariantId=serial,ProductUnitId=unit.ProductUnitId,Quantity=1,SellingPrice=200}]};
        var quote=await sales.QuoteAsync(request); Check(quote.TotalAmount==494,"different variant taxes apply on same product");
        request.Payments=[new(){Amount=quote.TotalAmount}];
        var ids=await Task.WhenAll(sales.CreateSaleAsync(request,1),sales.CreateSaleAsync(request,1));
        Check(ids[0]==ids[1],"concurrent retry posts sale once");
        Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@medium",new{medium})==1,"sale deducts only selected variant");
        await Reject(async()=>await sales.CreateSaleAsync(new(){RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,VariantId=medium,Quantity=2,SellingPrice=120}],Payments=[new(){Amount=252}]},1),"cannot borrow stock from another variant");
        await Reject(async()=>await sales.CreateSaleAsync(new(){RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,VariantId=large,ProductUnitId=unit.ProductUnitId,Quantity=1,SellingPrice=150}],Payments=[new(){Amount=168}]},1),"wrong variant/unit rejected");
        var results=await Task.WhenAll(SellLast(),SellLast()); Check(results.Count(x=>x)==1,"concurrent sales cannot oversell last variant unit");
        await c.ExecuteAsync("UPDATE product_variants SET variant_name='Renamed',selling_price=999 WHERE variant_id=@medium",new{medium});
        var invoice=await new InvoiceRepository(factory).GetInvoiceAsync(ids[0]);
        Check(invoice!.Items[0].ProductName.Contains("Black / M") && invoice.Items[0].SellingPrice==120,"invoice retains variant and price snapshots");
        var balances=(await new InventoryRepository(factory).GetBalancesAsync(null,false,false)).ToList();
        Check(balances.Count==4 && balances.Single(x=>x.VariantId==medium).CurrentStock==0,"inventory lists independent balances");
        var receipts=new PurchaseReceiptRepository(factory);
        var draft=await purchases.CreatePurchaseAsync(new(){SupplierId=1,PurchaseDate=DateTime.Today,Items=[new(){ProductId=1,VariantId=large,Quantity=10,PurchasePrice=50,DiscountAmount=1}]},1);
        var draftLine=(await purchases.GetByIdAsync(draft))!.Items.Single();
        var receive=new ReceivePurchaseRequest {RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=draftLine.PurchaseItemId,Quantity=4}]};
        var receiptIds=await Task.WhenAll(receipts.ReceiveAsync(draft,receive,1),receipts.ReceiveAsync(draft,receive,1));
        Check(receiptIds[0]==receiptIds[1] && (await purchases.GetByIdAsync(draft))!.Status=="PARTIALLY_RECEIVED","partial receipt and concurrent retry post once");
        await Reject(()=>purchases.UpdateDraftAsync(draft,new(){SupplierId=1,PurchaseDate=DateTime.Today,Items=[new(){ProductId=1,Quantity=10}]},1),"partially received order cannot be rewritten");
        await Reject(()=>receipts.ReceiveAsync(draft,new(){RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=draftLine.PurchaseItemId,Quantity=7}]},1),"over-receipt rejected");
        await receipts.ReceiveAsync(draft,new(){RequestId=Guid.NewGuid(),ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=draftLine.PurchaseItemId,Quantity=6}]},1);
        Check((await purchases.GetByIdAsync(draft))!.Status=="COMPLETED" && (await receipts.GetAsync(draft)).Sum(r=>r.Quantity)==10,"remaining receipt completes order with exact received count");
        Check(await c.ExecuteScalarAsync<decimal>("SELECT SUM(discount_amount) FROM purchase_receipt_items WHERE purchase_item_id=@id",new{id=draftLine.PurchaseItemId})==1,"partial receipts preserve total supplier discount");
        var inventory=new InventoryRepository(factory);
        await inventory.AdjustAsync(new(){RequestId=Guid.NewGuid(),ProductId=1,VariantId=large,TransactionType="DAMAGED",Quantity=1,Reason="Torn"},1);
        Check((await inventory.GetHistoryAsync(1,null,null,0,large)).All(m=>m.VariantId==large),"history filters by exact variant");
        Check((await inventory.GetUnitsAsync(1,null,true,0,serial)).All(u=>u.VariantId==serial),"unit picker filters before pagination");
        var dashboard=await new DashboardRepository(factory).GetAsync(true);
        Check(dashboard.LowStock.Any(p=>p.ProductName.Contains("Renamed")) && dashboard.RecentInflow.Any(p=>p.ProductName.Contains("Black / L")),"dashboard low stock and movements show variants");
        await Reject(()=>variants.SaveAsync(serial,new(){ProductId=1,VariantName="Collectible",Sku="SERIAL",IsSerialTracked=false},1),"used variant tracking cannot change");
        var customer=await c.ExecuteScalarAsync<int>("INSERT INTO customers(customer_name) VALUES('Service customer') RETURNING customer_id");
        var warranties=new WarrantyRepository(factory);
        var services=new ServiceRepository(factory,warranties);
        var ticket=await services.CreateAsync(new(){RequestId=Guid.NewGuid(),CustomerId=customer,ProductId=1,VariantId=serial,ProductUnitId=unit.ProductUnitId,Complaint="Inspect unit"},1);
        Check((await services.GetByIdAsync(ticket))!.VariantId==serial,"service intake retains mixed-product serialized variant");
        await services.UpdateStatusAsync(ticket,new(){RequestId=Guid.NewGuid(),ExpectedVersion=1,Status="CANCELLED",Remarks="Test cancellation"},1);
        var quantityTicket=await services.CreateAsync(new(){RequestId=Guid.NewGuid(),CustomerId=customer,ProductId=1,VariantId=large,Complaint="Quantity item repair"},1);
        await services.UpdateStatusAsync(quantityTicket,new(){RequestId=Guid.NewGuid(),ExpectedVersion=1,Status="INSPECTION",Remarks="Inspect"},1);
        var largeStock=await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@large",new{large});
        await services.AddPartAsync(quantityTicket,new(){RequestId=Guid.NewGuid(),ExpectedVersion=2,ProductId=1,VariantId=large,Quantity=1,UnitPrice=50},1);
        Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@large",new{large})==largeStock-1,"service parts deduct the chosen variant");
        var warranty=(await warranties.GetByUnitAsync(unit.ProductUnitId.Value))!;
        var claim=await warranties.CreateClaimAsync(new(){RequestId=Guid.NewGuid(),WarrantyId=warranty.WarrantyId,Complaint="Failed unit"},1);
        await warranties.ResolveClaimAsync(claim,new(){RequestId=Guid.NewGuid(),ExpectedStatus="OPEN",ClaimStatus="APPROVED",ResolutionNotes="Confirmed"},1);
        var replacement=(await scans.FindByCodeAsync("SER-2"))!;
        await warranties.ResolveClaimAsync(claim,new(){RequestId=Guid.NewGuid(),ExpectedStatus="APPROVED",ClaimStatus="REPLACED",ResolutionType="REPLACEMENT",ResolutionNotes="Replaced",ReplacementProductUnitId=replacement.ProductUnitId,ReplacementWarrantyRule="CONTINUE_ORIGINAL"},1);
        Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@serial",new{serial})==0,"warranty replacement consumes exact serialized variant");
        await c.ExecuteAsync("INSERT INTO product_units(product_id,variant_id,serial_number) VALUES(1,@serial,@code)",new{serial,code});
        await Reject(()=>scans.FindByCodeAsync(code),"ambiguous variant and unit scan rejected");
        Console.WriteLine("All variant database checks passed. Only isolated variants_test was changed.");
        async Task<bool> SellLast() {try{await sales.CreateSaleAsync(new(){RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,VariantId=medium,Quantity=1,SellingPrice=120}],Payments=[new(){Amount=126}]},1);return true;}catch(ValidationException){return false;}}
    }
    private static void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
    private static async Task Reject(Func<Task> action,string name){try{await action();}catch(ValidationException){Console.WriteLine("PASS: "+name);return;}throw new Exception("FAIL: "+name);}
}
