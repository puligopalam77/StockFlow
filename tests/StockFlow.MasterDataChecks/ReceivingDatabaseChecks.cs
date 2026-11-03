using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

static class ReceivingDatabaseChecks
{
    public static async Task Run()
    {
        var db="receiving_test_"+Guid.NewGuid().ToString("N");
        var cs=$"Host=127.0.0.1;Port=55441;Database={db};Username=postgres";
        await using var admin=new NpgsqlConnection(cs.Replace($"Database={db}","Database=postgres"));await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db);
        try
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
            await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
            foreach(var f in Directory.GetFiles(Path.Combine(root!.FullName,"database/scripts"),"*.sql").Where(f=>!Path.GetFileName(f).StartsWith("001")&&!Path.GetFileName(f).StartsWith("006")).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
            foreach(var f in Directory.GetFiles(Path.Combine(root.FullName,"database/migrations"),"*.sql").Order())
            {
                if(Path.GetFileName(f).StartsWith("020"))await c.ExecuteAsync("INSERT INTO roles(role_name) VALUES('Admin'); INSERT INTO users(role_id,full_name,username,password_hash) VALUES(1,'Owner','owner','unused'); INSERT INTO categories(category_name) VALUES('General'); INSERT INTO products(category_id,product_name,sku,selling_price) VALUES(1,'Shirt','SHIRT',100);");
                await c.ExecuteAsync(await File.ReadAllTextAsync(f));
            }
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false);SELECT set_config('app.user_id','1',false);SELECT set_config('app.location_id','1',false); INSERT INTO suppliers(supplier_name,country_code) VALUES('Test supplier','IN');");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
            var factory=new DbConnectionFactory(config,scope);var repository=new StockIntakeRepository(factory);
            StockIntakeRequest Receipt()=>new(){RequestId=Guid.NewGuid(),ReceivedDate=new(2026,10,3),SupplierId=1,Items=[new(){ProductId=1,VariantId=1,Quantity=3,PurchasePrice=10,TaxPercentage=0}]};
            var r=Receipt();var result=await repository.PostAsync(r,false,1);Check(result.PurchaseId.HasValue,"guided receipt creates completed purchase");Check(await Stock()==3,"guided receipt adds stock");
            Check((await repository.PostAsync(r,false,1)).IntakeId==result.IntakeId&&await Stock()==3,"identical retry returns original receipt without more stock");
            var concurrent=Receipt();concurrent.Items[0].Quantity=2;
            var results=await Task.WhenAll(repository.PostAsync(concurrent,false,1),repository.PostAsync(new(){RequestId=concurrent.RequestId,ReceivedDate=concurrent.ReceivedDate,SupplierId=1,Items=[new(){ProductId=1,VariantId=1,Quantity=2,PurchasePrice=10}]},false,1));
            Check(results[0].IntakeId==results[1].IntakeId&&await Stock()==5,"concurrent retries post one receipt");
            r.Items[0].Quantity=4;await Reject(()=>repository.PostAsync(r,false,1),"changed retry payload rejected");Check(await Stock()==5,"changed retry cannot alter stock");
            var opening=Receipt();opening.SupplierId=null;opening.Items[0].Quantity=4;var opened=await repository.PostAsync(opening,true,1);
            Check(opened.PurchaseId is null&&await Stock()==9,"opening stock adds quantity without a purchase");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE reference_type='OPENING_STOCK' AND reference_id=@id",new{id=opened.IntakeId})==1,"opening movements have separate reference");
            var serialProduct=await c.ExecuteScalarAsync<int>("INSERT INTO products(category_id,product_name,sku,is_serial_tracked,selling_price) VALUES(1,'Battery','BAT',true,50) RETURNING product_id");
            var serialVariant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@serialProduct AND is_default",new{serialProduct});
            var serial=Receipt();serial.Items=[new(){ProductId=serialProduct,VariantId=serialVariant,Quantity=2,PurchasePrice=20,SerialNumbers=["BAT-01","BAT-02"]}];
            var serialResult=await repository.PostAsync(serial,false,1);await repository.PostAsync(serial,false,1);
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units WHERE product_id=@serialProduct",new{serialProduct})==2,"serialized receipt retry preserves exactly two physical units");
            var before=await c.ExecuteScalarAsync<int>("SELECT count(*) FROM stock_intakes");var bad=Receipt();bad.Items.Add(new(){ProductId=serialProduct,VariantId=serialVariant,Quantity=2,SerialNumbers=["BAT-NEW","BAT-01"]});
            await Reject(()=>repository.PostAsync(bad,false,1),"existing serial rejects entire receipt");
            Check(await Stock()==9&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM stock_intakes")==before&&!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE serial_number='BAT-NEW')"),"failed multi-line receipt rolls back header, stock and units");
            var serialOpening=Receipt();serialOpening.SupplierId=null;serialOpening.Items=[new(){ProductId=serialProduct,VariantId=serialVariant,Quantity=1,PurchasePrice=20,SerialNumbers=["BAT-OPEN"]}];await repository.PostAsync(serialOpening,true,1);
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units WHERE serial_number='BAT-OPEN' AND purchase_id IS NULL")==1,"serialized opening stock does not invent a purchase");
            var variants=new VariantRepository(factory);var scanner=new ScanRepository(factory);
            await c.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source) VALUES(1,'0000123','MANUFACTURER')");
            var labels=await repository.LabelsAsync(result.IntakeId);Check(labels is {Count:1}&&labels[0].Quantity==3&&labels[0].ExistingBarcode=="0000123"&&labels[0].UnitId is null,"quantity label manifest retains leading zeroes and received quantity");
            var generated=await variants.GenerateBarcodeAsync(1,1);Check(await variants.GenerateBarcodeAsync(1,1)==generated,"variant generation reuses stored internal identifier");
            Check((await scanner.FindByCodeAsync(generated))?.VariantId==1&&(await scanner.FindByCodeAsync("0000123"))?.VariantId==1,"both existing and generated variant labels scan to the same variant");
            var batteryLabels=await repository.LabelsAsync(serialResult.IntakeId);Check(batteryLabels is {Count:2}&&batteryLabels.All(l=>l.UnitId.HasValue&&l.Quantity==1&&l.ExistingBarcode==l.SerialNumber),"battery receipt emits individual serial targets rather than a shared model label");
            var unitId=batteryLabels![0].UnitId!.Value;
            var unitCode=await variants.SaveUnitBarcodeAsync(unitId,new(){GenerateInternal=true},1);Check(await variants.SaveUnitBarcodeAsync(unitId,new(){GenerateInternal=true},1)==unitCode,"unit generation reuses its stored identifier");
            await variants.SaveUnitBarcodeAsync(unitId,new(){ManufacturerBarcode="BAT-MFG-001"},1);
            Check((await scanner.FindByCodeAsync(unitCode!))?.ProductUnitId==unitId&&(await scanner.FindByCodeAsync("BAT-MFG-001"))?.ProductUnitId==unitId&&(await scanner.FindByCodeAsync(batteryLabels[0].SerialNumber!))?.ProductUnitId==unitId,"internal, manufacturer and serial identifiers resolve to one battery");
            await Reject(()=>variants.SaveUnitBarcodeAsync(unitId,new(){ManufacturerBarcode="REPLACEMENT"},1),"existing physical unit barcode cannot be replaced");
            var printLabels=await repository.LabelsAsync(serialResult.IntakeId,"PRINT",1);var reprintLabels=await repository.LabelsAsync(serialResult.IntakeId,"REPRINT",1);Check(printLabels![0].InternalBarcode==unitCode&&reprintLabels![0].InternalBarcode==unitCode&&await Stock()==9,"print and reprint preserve identity and stock");
            Check((await repository.LabelsAsync((await repository.PostAsync(serialOpening,true,1)).IntakeId)) is {Count:1},"opening stock exposes individual label targets");
            // The full receiving UI uses the commercial purchase endpoint. Its
            // request ID must protect every stock movement, including damage.
            var purchases=new PurchaseRepository(factory);
            CreatePurchaseRequest Direct(Guid requestId)=>new()
            {
                RequestId=requestId,LocationId=1,SupplierId=1,PurchaseDate=new(2026,10,3),ReceivedDate=new(2026,10,3),CompleteImmediately=true,
                Items=[new(){ProductId=1,VariantId=1,Quantity=5,ReceivedQuantity=3,DamagedQuantity=2,PurchasePrice=10,TaxPercentage=0}]
            };
            var directRequestId=Guid.NewGuid();var directId=await purchases.CreatePurchaseAsync(Direct(directRequestId),1);
            Check(await Stock()==12,"direct physical receipt adds accepted units and excludes damaged units");
            Check(await purchases.CreatePurchaseAsync(Direct(directRequestId),1)==directId&&await Stock()==12,"interrupted direct receipt retries return the same purchase without duplicate stock");
            var changed=Direct(directRequestId);changed.Items[0].Quantity=6;
            await Reject(()=>purchases.CreatePurchaseAsync(changed,1),"request ID cannot be reused with a changed direct receipt");
            var concurrentId=Guid.NewGuid();var directResults=await Task.WhenAll(purchases.CreatePurchaseAsync(Direct(concurrentId),1),purchases.CreatePurchaseAsync(Direct(concurrentId),1));
            Check(directResults.Distinct().Count()==1&&await Stock()==15,"concurrent direct receipt retries post stock once");
            var branches=new BranchRepository(factory,scope);var second=await branches.CreateStoreAsync(new(){StoreCode="SECOND",StoreName="Second store"});scope.LocationId=(await branches.LocationsAsync()).Single(l=>l.StoreId==second).LocationId;scope.StoreId=second;
            Check(await Stock()==0,"other store stock remains independent");await Reject(()=>repository.PostAsync(r,false,1),"retry cannot move receipt to a different store");
            Check(await repository.LabelsAsync(result.IntakeId) is null,"another store cannot read receipt label manifest even for owner review");
            await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/guided_receiving_checks.sql")));
            await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/barcode_label_checks.sql")));
            Console.WriteLine("PASS: manual reconciliation SQL executes successfully");
            Console.WriteLine("Receiving database checks passed on an isolated temporary database.");
            async Task<long> Stock(){await using var s=factory.CreateConnection();return await s.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=1");}
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
    }
    static void Check(bool ok,string msg){if(!ok)throw new Exception("FAIL: "+msg);Console.WriteLine("PASS: "+msg);}
    static async Task Reject(Func<Task> action,string msg){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: "+msg);return;}catch(PostgresException){Console.WriteLine("PASS: "+msg);return;}throw new Exception("FAIL: "+msg);}
}
