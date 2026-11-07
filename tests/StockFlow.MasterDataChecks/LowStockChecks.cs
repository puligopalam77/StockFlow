using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class LowStockChecks
{
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
 static async Task Reject(Func<Task> action,string name){try{await action();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException||e is PostgresException p&&p.SqlState is "42501" or "23514"){Check(true,name);return;}throw new Exception("FAIL: "+name);}
 public static async Task Run(){
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
  var db="lowstock_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{
   await using var setup=new NpgsqlConnection(host+db);await setup.OpenAsync();
   foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(f));
   await setup.ExecuteAsync("SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
   var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true,IsOwner=true,CanReviewStores=true};
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db}}).Build();var factory=new DbConnectionFactory(config,scope);var repo=new InventoryRepository(factory);
   var product=await setup.ExecuteScalarAsync<int>("SELECT p.product_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.is_active AND p.owner_store_id=1 AND p.identifier_profile='ELECTRICAL' AND NOT v.is_serial_tracked LIMIT 1");
   Check(product>0,"isolated fixture has an owned active registered product");
   var ids=new List<long>();
   for(var n=0;n<29;n++)ids.Add(await setup.ExecuteScalarAsync<long>("INSERT INTO product_variants(product_id,variant_name,sku,is_serial_tracked,purchase_price,selling_price,mrp,minimum_stock_level,is_active,attributes) VALUES(@product,@name,@sku,@serial,50,100,150,@minimum,true,'{\"Color\":\"Black\"}') RETURNING variant_id",new{product,name="Shortage variant "+n,sku="LS-TEST-"+n.ToString("D2"),serial=n==4,minimum=n==0?0:5}));
   var intake=new StockIntakeRepository(factory);
   for(var n=1;n<=4;n++)await intake.PostAsync(new(){RequestId=Guid.NewGuid(),LocationId=1,ReceivedDate=DateTime.Today,Items=[new(){ProductId=product,VariantId=ids[n],Quantity=n==1?1:n==2?5:n==3?6:2,PurchasePrice=50,ReceivedQuantity=n==4?1:null,SerialNumbers=n==4?["LS-SERIAL-GOOD"]:[],DamagedQuantity=n==4?1:0,DamagedSerialNumbers=n==4?["LS-SERIAL-DAMAGE"]:[]}]},true,1);
   await setup.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source,is_primary,is_active) VALUES(@variant,'LS-BARCODE-001','MANUFACTURER',true,true)",new{variant=ids[1]});
   await setup.ExecuteAsync("INSERT INTO variant_location_settings(tenant_id,variant_id,location_id,minimum_stock_level) VALUES(1,@variant,1,3) ON CONFLICT(tenant_id,variant_id,location_id) DO UPDATE SET minimum_stock_level=3",new{variant=ids[1]});
   async Task<string> State()=> (await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT coalesce(sum(quantity),0) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM product_units)::text||':'||(SELECT count(*) FROM sales)::text"))!;
   var baseline=await State();var all=await repo.GetLowStockPageAsync("LS-TEST",null,1,100);
   Check(all.Total==28&&all.Summary.LowStockVariants==28,"shortages include zero/zero and exact-minimum, exclude above-minimum");
   Check(all.Rows.Single(r=>r.VariantId==ids[0]).QuantityNeeded==0&&all.Rows.Single(r=>r.VariantId==ids[2]).QuantityNeeded==0,"existing minimum-minus-ledger rule preserved at boundaries");
   var overrideRow=all.Rows.Single(r=>r.VariantId==ids[1]);Check(overrideRow.MinimumStockLevel==3&&overrideRow.CurrentStock==1&&overrideRow.QuantityNeeded==2,"variant/location minimum override retained");
   Check(all.Rows.Single(r=>r.VariantId==ids[4]).CurrentStock==1&&all.Rows.Single(r=>r.VariantId==ids[4]).AvailableUnits==1,"damaged serialized receipt excluded from existing ledger and available-unit count");
   Check((await repo.GetBalancesAsync("LS-TEST",true,false)).Select(r=>r.VariantId).Order().SequenceEqual(all.Rows.Select(r=>r.VariantId).Order()),"legacy inventory and paged shortage sets agree");
   Check(all.Summary.UnitsToReplenish==all.Rows.Sum(r=>r.QuantityNeeded),"summary sums existing quantity-needed facts once per variant");
   var outOnly=await repo.GetLowStockPageAsync("LS-TEST","OUT",1,100);var lowOnly=await repo.GetLowStockPageAsync("LS-TEST","LOW",1,100);
   Check(outOnly.Rows.All(r=>r.CurrentStock==0)&&lowOnly.Rows.All(r=>r.CurrentStock>0&&r.CurrentStock<=r.MinimumStockLevel)&&outOnly.Total+lowOnly.Total==all.Total,"disjoint zero vs positive at/below filters and consistent summaries");
   foreach(var term in new[]{"Shortage variant 1","LS-TEST-01","LS-BARCODE-001","LS-SERIAL-GOOD"})Check((await repo.GetLowStockPageAsync(term,null,1,100)).Total>0,"authorized existing search: "+term);
   Check((await repo.GetLowStockPageAsync("%",null,1,10)).Total==0,"literal search wildcard does not expose all records");
   foreach(var size in new[]{10,25,50,100}){var first=await repo.GetLowStockPageAsync("LS-TEST",null,1,size);var second=await repo.GetLowStockPageAsync("LS-TEST",null,2,size);Check(first.Total==all.Total&&first.Rows.Count==Math.Min(size,all.Total)&&!first.Rows.Select(r=>r.VariantId).Intersect(second.Rows.Select(r=>r.VariantId)).Any(),"accurate server paging size "+size);}
   await Reject(()=>repo.GetLowStockPageAsync(null,null,0,10),"invalid page denied");await Reject(()=>repo.GetLowStockPageAsync(null,null,1,20),"unsupported page size denied");await Reject(()=>repo.GetLowStockPageAsync(null,"BAD",1,10),"invalid stock status denied");
   var otherLocation=await setup.ExecuteScalarAsync<long>("INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) VALUES(1,1,'Low stock second location','SALES_FLOOR') RETURNING location_id");
   scope.LocationId=otherLocation;var local=await repo.GetLowStockPageAsync("LS-TEST",null,1,100);Check(local.Total==29&&local.Rows.All(r=>r.CurrentStock==0)&&local.Rows.Single(r=>r.VariantId==ids[1]).MinimumStockLevel==5,"same-shop second location has independent stock and minimum fallback");
   var other=await setup.QuerySingleAsync<(long Store,long Location)>("WITH s AS(INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'LS-OTHER','Private low-stock shop') RETURNING store_id) INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) SELECT 1,store_id,'Other low stock','SALES_FLOOR' FROM s RETURNING store_id,location_id");
   scope.StoreId=other.Store;scope.LocationId=other.Location;Check((await repo.GetLowStockPageAsync("LS-TEST",null,1,100)).Total==0,"independent same-profile shop cannot see private variants or summary");
   scope.StoreId=1;scope.LocationId=other.Location;await Reject(()=>repo.GetLowStockPageAsync(null,null,1,10),"forged shop/location pair denied");
   scope.LocationId=1;scope.TenantId=999999;await Reject(()=>repo.GetLowStockPageAsync(null,null,1,10),"cross-tenant search/count access denied");
   scope.TenantId=1;scope.BusinessProfileCode=BusinessProfiles.BikeAccessories;Check((await repo.GetLowStockPageAsync("LS-TEST",null,1,100)).Total==0,"different profile does not leak shortage rows/counts");scope.BusinessProfileCode="ELECTRICAL";
   scope.UserId=999999;await Reject(()=>repo.GetLowStockPageAsync(null,null,1,10),"inactive/forged user cannot read shortage information");scope.UserId=1;
   Check(await State()==baseline,"all shortage searches, counts and summaries create no inventory or financial movements");
   using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IInventoryRepository>();s.AddSingleton<IInventoryRepository>(repo);}));using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
   var response=await client.GetAsync("/api/inventory?paged=true&lowStockOnly=true&term=LS-TEST&pageSize=25");Check(response.IsSuccessStatusCode,"actual scoped paged Inventory API responds");
   Check((await client.GetAsync("/api/inventory?paged=true")).StatusCode==HttpStatusCode.BadRequest,"paged contract rejects ordinary-balance misuse");
   Directory.CreateDirectory("tests/bin/low-stock-fixtures");await File.WriteAllTextAsync("tests/bin/low-stock-fixtures/LowStock.html",await client.GetStringAsync("/Inventory/LowStock"));
   await File.WriteAllTextAsync("tests/bin/low-stock-fixtures/records.json",JsonSerializer.Serialize(all,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
   client.DefaultRequestHeaders.Add("Test-Deny","INVENTORY");Check((await client.GetAsync("/api/inventory?paged=true&lowStockOnly=true")).StatusCode==HttpStatusCode.Forbidden,"restricted user paged API denied by permission middleware");
   Console.WriteLine("Low Stock PostgreSQL/API checks passed in disposable clone.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
 }
}
