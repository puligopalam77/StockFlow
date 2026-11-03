using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using System.Text.Json;

namespace StockFlow.MasterDataChecks;
internal static class SequentialSkuDatabaseChecks
{
    const string Cs="Host=127.0.0.1;Port=55439;Database=sequential_sku_067_v2_test;Username=postgres";
    public static async Task Run()
    {
        await using var admin=new NpgsqlConnection(Cs);await admin.OpenAsync();
        Check(await admin.ExecuteScalarAsync<string>("SELECT current_database()") == "sequential_sku_067_v2_test","isolated database only");
        await admin.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false),set_config('app.business_profile_code','BIKE_ACCESSORIES',false)");
        foreach(var (table,id,exclude) in new[]{("products","product_id","ARRAY['identifier_profile']"),("product_variants","variant_id","ARRAY['identifier_profile']"),("categories","category_id","ARRAY['sku_prefix','sku_prefix_used']"),("inventory_transactions","inventory_transaction_id","ARRAY[]::text[]"),("variant_barcodes","variant_barcode_id","ARRAY[]::text[]")})
            Check(await admin.ExecuteScalarAsync<int>($"SELECT count(*) FROM sku067_baseline b LEFT JOIN {table} t ON t.{id}=b.id WHERE b.tab=@table AND (t.{id} IS NULL OR to_jsonb(t)-{exclude}<>b.value)",new{table})==0,"migration preserves every existing "+table+" value");
        foreach(var table in new[]{"purchases","purchase_items","sales","sale_items","product_units","variant_supplier_identifiers"})
            Check(await admin.ExecuteScalarAsync<int>($"SELECT count(*) FROM sku067_baseline b WHERE b.tab=@table AND NOT EXISTS(SELECT 1 FROM {table} t WHERE to_jsonb(t)=b.value)",new{table})==0,"migration preserves historical "+table);
        var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="BIKE_ACCESSORIES",CatalogRegistrationEnhancementsAvailable=true,ProductImageGalleryAvailable=true,ProfileDataVisibilityAvailable=true};
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",Cs}}).Build();
        var factory=new DbConnectionFactory(config,scope);var repo=new ProductRepository(factory);var variants=new VariantRepository(factory);var categories=new CategoryRepository(factory);
        var service=new ProductService(repo,categories,new BrandRepository(factory),scope,null,variants);
        async Task<string> Reserve(int cat,Guid request,string? desired=null){await using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@cat,@request,@desired)",new{cat,request,desired})??throw new Exception("Empty reservation");}
        async Task<string[]> Suffixes(string parent,Guid request,Guid operation,int amount,int? pid=null){await using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<string[]>("SELECT sf_reserve_variant_codes(@parent,@request,@operation,@amount,@pid)",new{parent,request,operation,amount,pid})??throw new Exception("Empty suffix reservation");}
        var synthetic=await admin.ExecuteScalarAsync<int>("SELECT category_id FROM categories WHERE category_name='Synthetic initialization fixture'");
        Check(await Reserve(synthetic,Guid.NewGuid())=="SYN-0007","initial counter scans existing parent and numeric variant-style legacy identifiers");
        Check(await Reserve(12,Guid.NewGuid())=="BRK-0001"&&await Reserve(12,Guid.NewGuid())=="BRK-0002","independent category product sequence starts 0001,0002");
        var concurrent=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>Reserve(12,Guid.NewGuid())));
        Check(concurrent.Distinct().Count()==20&&concurrent.Contains("BRK-0022"),"twenty concurrent connections reserve distinct committed codes");
        var retry=Guid.NewGuid();var reserved=await Reserve(12,retry);Check(await Reserve(12,retry)==reserved,"reservation retry is idempotent");
        Check(await Reserve(12,Guid.NewGuid(),"BRK-0025")=="BRK-0025"&&await Reserve(12,Guid.NewGuid())=="BRK-0026","manual reservation advances high-water mark");
        await Reject(()=>Reserve(12,Guid.NewGuid(),"BRK-0025"),"cannot steal abandoned reservations");
        await Reject(()=>Reserve(8,Guid.NewGuid()),"parent with subcategories cannot allocate a product code");
        await Reserve(38,Guid.NewGuid(),"HLM-0006");var helmetRequest=Guid.NewGuid();var helmet=await Reserve(38,helmetRequest);Check(helmet=="HLM-0007","initialized helmet sequence uses next product number");
        var suffixOperation=Guid.NewGuid();var helmetCodes=await Suffixes(helmet,helmetRequest,suffixOperation,2);Check(helmetCodes.SequenceEqual(new[]{"HLM-0007-01","HLM-0007-02"}),"server allocates short numeric suffixes");
        Check((await Suffixes(helmet,helmetRequest,suffixOperation,2)).SequenceEqual(helmetCodes),"variant reservation retry returns same suffixes");
        for(var i=0;i<3;i++)await Suffixes(helmet,helmetRequest,Guid.NewGuid(),30);
        Check((await Suffixes(helmet,helmetRequest,Guid.NewGuid(),10)).Last()=="HLM-0007-102","variant suffix expands beyond 99");
        await Reserve(11,Guid.NewGuid(),"MIR-9999");Check(await Reserve(11,Guid.NewGuid())=="MIR-10000","product sequence expands beyond 9999");
        var cat=await categories.CreateAsync(new(){CategoryName="Sequential SKU integration",SkuPrefix="QSK",IsActive=true,BusinessProfileCode="BIKE_ACCESSORIES"});
        var requestId=Guid.NewGuid();var code=await Reserve(cat,requestId);
        CreateProductRequest Request(string codeValue,Guid id,bool multi=false,List<RegistrationVariantRequest>? rows=null)=>new(){RegistrationRequestId=id,ParentProductCode=codeValue,ProductName="Sequential integration item "+codeValue,CategoryId=cat,TrackingType="QUANTITY",HasVariants=multi,RegistrationDefaults=new(){PurchasePrice=100,SellingPrice=200,Mrp=250},Variants=rows};
        var request=Request(code,requestId);var original=JsonSerializer.Serialize(request);var productId=await service.CreateAsync(request);var item=(await variants.GetAsync(productId,null,false)).Single();
        Check(item.Sku==code&&item.IsDefault&&item.CurrentStock==0,"simple product has matching product/default SKU and adds no stock");
        Check(await service.CreateAsync(JsonSerializer.Deserialize<CreateProductRequest>(original)!)==productId,"retried save returns original product");
        var failureId=Guid.NewGuid();var failureCode=await Reserve(cat,failureId);
        await Reject(()=>service.CreateAsync(Request(failureCode,failureId,true,[new(){VariantName="Invalid family stock item",Sku=failureCode}])),"failed save rolls back product and variant together");
        Check(!await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM products WHERE parent_product_code=@failureCode)",new{failureCode})&&await Reserve(cat,Guid.NewGuid())!=failureCode,"failed save leaves its reservation consumed without a partial product");
        var multiRequest=Guid.NewGuid();var family=await Reserve(cat,multiRequest);var codes=await Suffixes(family,multiRequest,Guid.NewGuid(),2);
        var multi=await service.CreateAsync(Request(family,multiRequest,true,[new(){VariantName="Black / M",Sku=codes[0],PurchasePrice=100,SellingPrice=200,Attributes=new(){["size"]="M"},PrimaryBarcode="SKU067-MANUFACTURER",BarcodeSource="MANUFACTURER"},new(){VariantName="Black / L",Sku=codes[1],PurchasePrice=100,SellingPrice=200,Attributes=new(){["size"]="L"}}]));
        var before=(await variants.GetAsync(multi,null,false)).ToList();var added=(await Suffixes(family,Guid.NewGuid(),Guid.NewGuid(),1,multi)).Single();
        await variants.SaveAsync(null,new(){ProductId=multi,VariantName="Black / XL",Sku=added,PurchasePrice=100,SellingPrice=200,Attributes=new(){["size"]="XL"}},1);
        var after=(await variants.GetAsync(multi,null,false)).ToList();Check(added==family+"-03"&&after.Count==3&&before.All(v=>after.Any(saved=>saved.VariantId==v.VariantId&&saved.Sku==v.Sku)),"later variant receives next suffix; saved variants stay intact");
        await Reject(()=>admin.ExecuteAsync("UPDATE product_variants SET sku='QSK-RENAME' WHERE variant_id=@id",new{id=item.VariantId}),"saved SKU immutable even before transactions");
        await Reject(()=>admin.ExecuteAsync("UPDATE products SET parent_product_code='QSK-RENAME' WHERE product_id=@productId",new{productId}),"saved family identifier immutable");
        await Reject(()=>admin.ExecuteAsync("UPDATE categories SET sku_prefix='QXX' WHERE category_id=@cat",new{cat}),"issued category prefix immutable");
        await admin.ExecuteAsync("UPDATE categories SET category_name='Renamed sequential category' WHERE category_id=@cat",new{cat});
        Check((await categories.GetByIdAsync(cat))!.SkuPrefix=="QSK","category rename preserves prefix");
        await Reject(()=>categories.CreateAsync(new(){CategoryName="Duplicate prefix",SkuPrefix="QSK",IsActive=true}),"duplicate category prefixes rejected");
        await using(var runtime=factory.CreateConnection())Check((await runtime.ExecuteScalarAsync<string>("SELECT sf_suggest_sku_prefix('QSK sample')"))=="QSK1","prefix suggestion avoids assigned and retired namespaces");
        var group=await categories.CreateAsync(new(){CategoryName="SKU hierarchy parent",SkuPrefix="QGP",IsActive=true});
        var child=await categories.CreateAsync(new(){CategoryName="SKU hierarchy child",SkuPrefix="QGC",ParentCategoryId=group,IsActive=true});
        await Reject(()=>categories.CreateAsync(new(){CategoryName="SKU forbidden third level",SkuPrefix="QGT",ParentCategoryId=child,IsActive=true}),"third-level categories remain forbidden");
        var destination=await categories.CreateAsync(new(){CategoryName="New destination",SkuPrefix="QDS",IsActive=true});
        await service.UpdateAsync(productId,new(){ParentProductCode=code,CategoryId=destination,ProductName="Sequential moved item",TrackingType="QUANTITY",IsActive=true});
        Check((await repo.GetByIdAsync(productId))!.ParentProductCode==code&&(await variants.GetAsync(productId,null,false)).Single().Sku==code,"category reassignment keeps all identifiers");
        await admin.ExecuteAsync("UPDATE product_variants SET is_active=false WHERE product_id=@multi AND sku=@added",new{multi,added});
        Check((await Suffixes(family,Guid.NewGuid(),Guid.NewGuid(),1,multi)).Single()==family+"-04","inactive variant suffix is never reused");
        var counterBefore=await admin.ExecuteScalarAsync<long>("SELECT last_number FROM catalog_prefix_registry WHERE prefix='QSK'");
        await repo.DeleteAsync(productId);Check(await Reserve(cat,Guid.NewGuid())=="QSK-"+(counterBefore+1).ToString("D4"),"deactivation never reuses a number");
        var supplier=await admin.ExecuteScalarAsync<int>("INSERT INTO suppliers(tenant_id,business_profile_code,supplier_name,country_code) VALUES(1,'BIKE_ACCESSORIES','SKU067 integration supplier','IN') RETURNING supplier_id");
        var exact=before[0];var purchases=new PurchaseRepository(factory);var purchase=await purchases.CreatePurchaseAsync(new(){SupplierId=supplier,LocationId=1,PurchaseDate=DateTime.Today,Items=[new(){ProductId=multi,VariantId=exact.VariantId,Quantity=3,PurchasePrice=90}]},1);
        Check((await variants.GetAsync(multi,null,false)).All(v=>v.CurrentStock==0),"purchase order leaves stock unchanged");
        var po=(await purchases.GetByIdAsync(purchase))!;var receiving=new PurchaseReceiptRepository(factory);var receiptRequest=new StockFlow.Application.DTOs.Purchases.ReceivePurchaseRequest{RequestId=Guid.NewGuid(),LocationId=1,ReceivedDate=DateTime.Today,Items=[new(){PurchaseItemId=po.Items.Single().PurchaseItemId,Quantity=3}]};var receipt=await receiving.ReceiveAsync(purchase,receiptRequest,1);
        Check(await receiving.ReceiveAsync(purchase,receiptRequest,1)==receipt&&(await variants.GetAsync(multi,null,false)).Single(v=>v.VariantId==exact.VariantId).CurrentStock==3,"receipt posts to exact variant once");
        var barcode=await variants.GenerateBarcodeAsync(exact.VariantId,1);Check(await variants.GenerateBarcodeAsync(exact.VariantId,1)==barcode,"barcode generation/reprint reuses stored manufacturer code");
        Check((await variants.GetAsync(null,exact.Sku,true)).Any(v=>v.VariantId==exact.VariantId),"purchasing and billing lookup finds numeric SKU");
        var scanner=new ScanRepository(factory);Check((await scanner.FindByCodeAsync(exact.Sku))?.VariantId==exact.VariantId&&(await scanner.FindByCodeAsync(barcode))?.VariantId==exact.VariantId,"SKU and manufacturer barcode scanning resolve the correct variant");
        await admin.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source,is_primary) VALUES(@VariantId,@Sku,'MANUFACTURER',false)",exact);
        Check((await scanner.FindByCodeAsync(exact.Sku))?.VariantId==exact.VariantId,"one variant matching both SKU and barcode is not falsely ambiguous");
        var billing=new SalesRepository(factory);var saleRequest=new StockFlow.Application.DTOs.Sales.CreateSaleRequest{LocationId=1,RequestId=Guid.NewGuid(),Items=[new(){ProductId=multi,VariantId=exact.VariantId,Quantity=1,SellingPrice=200}]};var quote=await billing.QuoteAsync(saleRequest);saleRequest.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];var sale=await billing.CreateSaleAsync(saleRequest,1);
        Check((await variants.GetAsync(multi,null,false)).Single(v=>v.VariantId==exact.VariantId).CurrentStock==2&&(await billing.GetByIdAsync(sale))!.Lines.Single().VariantId==exact.VariantId,"billing deducts correct variant with stable invoice reference");
        Check(await admin.ExecuteScalarAsync<long>("SELECT last_number FROM catalog_prefix_registry WHERE prefix='QSK'")==counterBefore+1,"purchase, receipt and sale do not advance SKU counters");
        // Same tenant, independent profile/catalog: stale requests must fail and new
        // profile may intentionally issue the same human-readable SKU text.
        await admin.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");
        await Reject(()=>Reserve(cat,Guid.NewGuid()),"stale shop request cannot reserve into another profile");
        var electricScope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};var electricFactory=new DbConnectionFactory(config,electricScope);
        await using var electric=electricFactory.CreateConnection();var otherCategory=await electric.ExecuteScalarAsync<int>("INSERT INTO categories(category_name,sku_prefix,business_profile_code) VALUES('Independent electrical SKU','QSK','ELECTRICAL') RETURNING category_id");
        var otherRequest=Guid.NewGuid();var otherCode=await electric.ExecuteScalarAsync<string>("SELECT sf_reserve_product_code(@otherCategory,@request,NULL)",new{otherCategory,request=otherRequest});Check(otherCode=="QSK-0001","shop profile counter and prefix are independent");
        var electricService=new ProductService(new ProductRepository(electricFactory),new CategoryRepository(electricFactory),new BrandRepository(electricFactory));
        var otherProduct=await electricService.CreateAsync(new(){RegistrationRequestId=otherRequest,ParentProductCode=otherCode,ProductName="Independent electrical product",CategoryId=otherCategory,TrackingType="QUANTITY"});
        Check((await new VariantRepository(electricFactory).GetAsync(otherProduct,null,false)).Single().Sku==code,"identical persisted SKU text is permitted in independent profiles");
        async Task RenameLegacy(){await using var connection=electricFactory.CreateConnection();await connection.OpenAsync();await using var tx=await connection.BeginTransactionAsync();await connection.ExecuteAsync("UPDATE products SET product_name=product_name WHERE product_id=1",transaction:tx);await connection.ExecuteAsync("UPDATE product_variants SET sku='FORBIDDEN-LEGACY-RENAME' WHERE variant_id=1",transaction:tx);await tx.CommitAsync();}
        await Reject(RenameLegacy,"updating an old product cannot unlock its saved LEGACY SKU");
        Check((await new ScanRepository(electricFactory).FindByCodeAsync(code))?.ProductId==otherProduct,"identical SKU scan stays inside its original shop profile");
        Check(await electric.ExecuteScalarAsync<int>("SELECT count(*) FROM products WHERE product_id=@multi",new{multi})==0,"other shop cannot resolve original catalog IDs");
        await admin.ExecuteAsync("UPDATE tenants SET business_profile_code='BIKE_ACCESSORIES' WHERE tenant_id=1");
        Console.WriteLine("PASS: sequential identifiers tested against isolated PostgreSQL, including actual purchase/receipt/billing writes. Live database untouched.");
    }
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    static async Task Reject(Func<Task> action,string text){try{await action();}catch(System.ComponentModel.DataAnnotations.ValidationException){Check(true,text);return;}catch(PostgresException ex) when(ex.SqlState is "23514" or "23505" or "42501" or "P0001"){Check(true,text);return;}throw new Exception("Expected rejection: "+text);}
}
