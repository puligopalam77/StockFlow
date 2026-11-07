using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class ReplenishmentChecks
{
 static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
 static async Task Reject(Func<Task> f,string name){try{await f();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException||e is PostgresException p&&p.SqlState is "42501" or "23514" or "40001"){Check(true,name);return;}throw new Exception("FAIL expected rejection: "+name);}
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";var db="replenish_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{
   await using var setup=new NpgsqlConnection(host+db);await setup.OpenAsync();foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(file));
   await setup.ExecuteAsync("SET app.tenant_id='1';SET app.user_id='1';SET app.store_id='1';SET app.location_id='1';SET app.business_profile_code='ELECTRICAL'");
   var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true,IsOwner=true};
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db},{"Features:Replenishment","true"}}).Build();var factory=new DbConnectionFactory(config,scope);var repo=new ReplenishmentRepository(factory);
   var product=await setup.ExecuteScalarAsync<int>("SELECT p.product_id FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.is_active AND p.owner_store_id=1 AND p.identifier_profile='ELECTRICAL' AND NOT v.is_serial_tracked LIMIT 1");
   var ids=new List<long>();for(var n=0;n<15;n++)ids.Add(await setup.ExecuteScalarAsync<long>("INSERT INTO product_variants(product_id,variant_name,sku,is_serial_tracked,purchase_price,selling_price,mrp,minimum_stock_level,is_active) VALUES(@product,@name,@sku,false,50,100,150,5,true) RETURNING variant_id",new{product,name="Planning variant "+n,sku="RP-TEST-"+n.ToString("D2")}));
   var supplierRepository=new SupplierRepository(factory);
   var beforeSupplier=await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT count(*) FROM supplier_payments)::text||':'||(SELECT count(*) FROM purchases)::text");
   var createdSuppliers=await Task.WhenAll(Enumerable.Range(0,2).Select(n=>supplierRepository.CreateAsync(new(){SupplierName="Planning registration "+n,CountryCode="IN"})));
   var savedSuppliers=await Task.WhenAll(createdSuppliers.Select(id=>supplierRepository.GetByIdAsync(id)));
   Check(savedSuppliers.All(s=>!string.IsNullOrWhiteSpace(s!.SupplierCode))&&savedSuppliers.Select(s=>s!.SupplierCode).Distinct().Count()==2,"concurrent supplier registrations receive distinct database-generated codes");
   var savedSupplier=savedSuppliers[0]!;var supplierCode=savedSupplier.SupplierCode;savedSupplier.SupplierName+=" edited";savedSupplier.SupplierCode="FORGED-CODE";await supplierRepository.UpdateAsync(savedSupplier);
   Check((await supplierRepository.GetByIdAsync(savedSupplier.SupplierId))!.SupplierCode==supplierCode,"supplier edit preserves its generated code");
   Check(await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT count(*) FROM supplier_payments)::text||':'||(SELECT count(*) FROM purchases)::text")==beforeSupplier,"supplier registration/edit do not post stock, liability payments or purchases");
   var supplier=createdSuppliers[0];
   await setup.ExecuteAsync("UPDATE products SET registration_defaults=coalesce(registration_defaults,'{}'::jsonb)||jsonb_build_object('preferredSupplierId',@supplier) WHERE product_id=@product",new{product,supplier});
   Check(!(await repo.SuppliersAsync()).Any(s=>ids.Contains(s.VariantId)),"product preferred supplier alone is not a replenishment variant mapping");
   var variantRepository=new VariantRepository(factory);
   var mappingRequest=JsonSerializer.Deserialize<StockFlow.Application.DTOs.Products.VariantRequest>(JsonSerializer.Serialize((await variantRepository.GetAsync(product,null,false)).Single(v=>v.VariantId==ids[1])))!;
   mappingRequest.SupplierIdentifiers=[new(){SupplierId=supplier,SupplierSku="RP-ITEM"}];
   await variantRepository.SaveAsync(ids[1],mappingRequest,1);
   Check((await repo.SuppliersAsync()).Any(s=>s.VariantId==ids[1]&&s.SupplierId==supplier&&s.SupplierCode=="RP-ITEM"),"existing variant save supplies the exact mapping used by replenishment");
   Check(!(await repo.SuppliersAsync()).Any(s=>s.VariantId==ids[0]),"supplier mapping does not leak to a sibling variant");
   await new StockIntakeRepository(factory).PostAsync(new(){RequestId=Guid.NewGuid(),LocationId=1,ReceivedDate=DateTime.Today,Items=[new(){ProductId=product,VariantId=ids[1],Quantity=1,PurchasePrice=50}]},true,1);
   var policy=new ReplenishmentPolicy{VariantId=ids[1],SupplierId=supplier,LeadDays=5,SafetyStock=2,CoverageDays=10,Moq=6,OrderMultiple=4};Check(await repo.SavePolicyAsync(policy)==1,"owner scoped policy saved");await Reject(()=>repo.SavePolicyAsync(policy),"stale policy version");
   await setup.ExecuteAsync("UPDATE products SET created_at=now()-interval '120 days' WHERE product_id=@product",new{product});
   for(var day=1;day<=3;day++){var sale=await setup.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,sale_date,created_by,status,subtotal,total_amount) VALUES(@number,now()-make_interval(days=>@day),1,'COMPLETED',100,100) RETURNING sale_id",new{number="RP-SALE-"+day,day});await setup.ExecuteAsync("INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,line_total) VALUES(@sale,@product,@vid,10,10,100)",new{sale,product,vid=ids[1]});}
   var saleId=await setup.ExecuteScalarAsync<long>("SELECT sale_id FROM sales WHERE invoice_number='RP-SALE-1'");var itemId=await setup.ExecuteScalarAsync<long>("SELECT sale_item_id FROM sale_items WHERE sale_id=@saleId",new{saleId});
   var ret=await setup.ExecuteScalarAsync<long>("INSERT INTO sale_returns(sale_id,return_number,return_total,refund_mode,notes,request_id,request_payload,created_by) VALUES(@saleId,'RP-RETURN',0,'NONE','fixture',gen_random_uuid(),'{}',1) RETURNING sale_return_id",new{saleId});
   await setup.ExecuteAsync("INSERT INTO sale_return_items(sale_return_id,sale_item_id,product_id,variant_id,quantity,amount,disposition) VALUES(@ret,@itemId,@product,@vid,2,0,'RESTOCK'),(@ret,@itemId,@product,@vid,1,0,'DAMAGED')",new{ret,itemId,product,vid=ids[1]});
   var draftSale=await setup.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,created_by,status) VALUES('RP-DRAFT',1,'DRAFT') RETURNING sale_id");await setup.ExecuteAsync("INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,line_total) VALUES(@draftSale,@product,@vid,100,10,1000)",new{draftSale,product,vid=ids[1]});
   var cancelled=await setup.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,created_by,status) VALUES('RP-CANCEL',1,'CANCELLED') RETURNING sale_id");await setup.ExecuteAsync("INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,line_total) VALUES(@cancelled,@product,@vid,200,10,2000)",new{cancelled,product,vid=ids[1]});
   await setup.ExecuteAsync("INSERT INTO payments(sale_id,payment_mode,amount,created_by) VALUES(@saleId,'CASH',1,1)",new{saleId});
   async Task<string> State()=> (await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT coalesce(sum(quantity),0) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM payments)::text||':'||(SELECT count(*) FROM supplier_payments)::text"))!;
   var baseline=await State();var plan=await repo.PlanAsync("RP-TEST",null,null,30,1,100);var r=plan.Rows.Single(r=>r.VariantId==ids[1]);
   Check(r.Sold==30&&r.RestockedReturns==2&&r.NonSaleableReturns==1&&r.DailyDemand==28m/30,"final sales units, restock-only net adjustment, damaged return and draft exclusion");Check(r.Suggested==16&&r.RawSuggestion==15&&r.Target==16,"target/MOQ/multiple formula");
   Check(r.CoverDays.HasValue&&plan.Rows.Single(r=>r.VariantId==ids[0]).CoverDays is null,"positive observed cover versus unknown history");
   // Two genuine read snapshots around the existing policy writer for popup tests.
   Directory.CreateDirectory("tests/bin/replenishment-fixtures");
   await setup.ExecuteAsync("DELETE FROM replenishment_policies WHERE variant_id=@vid",new{vid=ids[1]});
   await File.WriteAllTextAsync("tests/bin/replenishment-fixtures/records-unconfigured.json",JsonSerializer.Serialize(await repo.PlanAsync("RP-TEST",null,null,30,1,100),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
   await repo.SavePolicyAsync(policy);
   await File.WriteAllTextAsync("tests/bin/replenishment-fixtures/records-configured-after.json",JsonSerializer.Serialize(await repo.PlanAsync("RP-TEST",null,null,30,1,100),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
   Check(r.Sold==30,"cancelled sales and later collections do not inflate demand");
   foreach(var days in new[]{7,30,60,90})Check((await repo.PlanAsync("RP-TEST-01",null,null,days,1,10)).Rows.Single().DailyDemand==28m/days,"lookback "+days);
   foreach(var size in new[]{10,25,50,100})Check((await repo.PlanAsync("RP-TEST",null,null,30,1,size)).Total==15,"pagination "+size);
   var summary=JsonSerializer.SerializeToElement(plan.Summary,new JsonSerializerOptions(JsonSerializerDefaults.Web));Check(summary.GetProperty("review").GetInt32()==15&&summary.GetProperty("risk").GetInt32()==1&&summary.GetProperty("riskUnassessed").GetInt32()==14&&summary.GetProperty("suggested").GetInt64()==16,"KPI totals and unassessed risk reflect all filtered variants");
   var noPolicy=await repo.PlanAsync("RP-TEST","SUPPLIER_NOT_CONFIGURED",null,30,1,10);Check(JsonSerializer.SerializeToElement(noPolicy.Summary,new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("riskUnassessed").GetInt32()==noPolicy.Total,"unknown-risk filter does not imply confirmed zero risk");
   var request=new ReplenishmentDraftRequest(Guid.NewGuid(),supplier,30,[new(ids[1],r.Fingerprint,r.Suggested,55,0)]);var id=await repo.PrepareAsync(request);
   Check(id==await repo.PrepareAsync(request),"exact request retry");Check(id==await repo.PrepareAsync(request with{OperationId=Guid.NewGuid()}),"same recommendation new click reuses draft");await Reject(()=>repo.PrepareAsync(request with{Lines=[request.Lines[0] with{PurchasePrice=56}]}),"changed retry payload");
   Check(await setup.ExecuteScalarAsync<string>("SELECT status FROM purchases WHERE purchase_id=@id",new{id})=="DRAFT","existing purchase draft lifecycle");Check(await setup.ExecuteScalarAsync<long>("SELECT variant_id FROM purchase_items WHERE purchase_id=@id",new{id})==ids[1],"exact variant preserved");Check(await State()==baseline,"policy/read/draft no stock or financial posting");
   await Reject(()=>repo.PrepareAsync(request with{OperationId=Guid.NewGuid(),Lines=[request.Lines[0] with{Fingerprint="STALE"}]}),"stale recommendation");
   var fresh=(await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single();Check(fresh.DraftPo==16&&fresh.OpenPo==0,"draft supply excluded");await setup.ExecuteAsync("UPDATE purchases SET status='PARTIALLY_RECEIVED' WHERE purchase_id=@id;UPDATE purchase_items SET received_quantity=3,damaged_quantity=2 WHERE purchase_id=@id",new{id});fresh=(await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single();Check(fresh.OpenPo==11,"partial order deducts good and damaged arrivals");await setup.ExecuteAsync("UPDATE purchases SET status='COMPLETED' WHERE purchase_id=@id",new{id});Check((await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single().OpenPo==0,"completed order excluded");
   var source=await setup.ExecuteScalarAsync<long>("INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) VALUES(1,1,'Planning source','SALES_FLOOR') RETURNING location_id");
   var transfer=await setup.ExecuteScalarAsync<long>("INSERT INTO stock_transfers(tenant_id,transfer_number,from_location_id,to_location_id,transit_location_id,status,requested_by) VALUES(1,'RP-TRANSFER',@source,1,@source,'DISPATCHED',1) RETURNING transfer_id",new{source});
   await setup.ExecuteAsync("INSERT INTO stock_transfer_items(tenant_id,transfer_id,variant_id,requested_quantity,dispatched_quantity,received_quantity,damaged_quantity,lost_quantity) VALUES(1,@transfer,@vid,20,20,2,1,1)",new{transfer,vid=ids[1]});
   fresh=(await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single();Check(fresh.IncomingTransfer==16&&fresh.Status=="COVERED"&&fresh.Suggested==0,"dispatched transfer remainder and covered status");
   await setup.ExecuteAsync("UPDATE stock_transfers SET status='RECEIVED' WHERE transfer_id=@transfer",new{transfer});Check((await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single().IncomingTransfer==0,"received transfer not double counted");
   var beforeStock=(await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single();await new StockIntakeRepository(factory).PostAsync(new(){RequestId=Guid.NewGuid(),LocationId=1,ReceivedDate=DateTime.Today,Items=[new(){ProductId=product,VariantId=ids[1],Quantity=1,PurchasePrice=50}]},true,1);
   await Reject(()=>repo.PrepareAsync(new(Guid.NewGuid(),supplier,30,[new(ids[1],beforeStock.Fingerprint,beforeStock.Suggested,55,0)])),"real receipt after snapshot rejects stale order");baseline=await State();
   await Reject(()=>repo.PrepareAsync(new(Guid.NewGuid(),supplier,30,[request.Lines[0],request.Lines[0]])),"duplicate variant selection denied");
   scope.LocationId=source;var otherLocation=(await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single();Check(otherLocation.Policy is null&&otherLocation.Sold==0&&otherLocation.Stock==0,"location-private policy, stock and demand");scope.LocationId=1;
   scope.BusinessProfileCode=BusinessProfiles.BikeAccessories;Check((await repo.PlanAsync("RP-TEST",null,null,30,1,10)).Total==0,"different profile isolation");scope.BusinessProfileCode="ELECTRICAL";
   await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();Check(await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM replenishment_policies")==1,"runtime RLS policy read");await Reject(()=>runtime.ExecuteAsync("UPDATE replenishment_policies SET lead_days=999"),"direct policy table writes denied");}
   var other=await setup.QuerySingleAsync<(long Store,long Location)>("WITH s AS(INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'RP-OTHER','Private planning shop') RETURNING store_id) INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) SELECT 1,store_id,'Other planning','SALES_FLOOR' FROM s RETURNING store_id,location_id");
   scope.StoreId=other.Store;scope.LocationId=other.Location;Check((await repo.PlanAsync("RP-TEST",null,null,30,1,10)).Total==0,"same-profile private shop rows/counts isolated");await Reject(()=>repo.SavePolicyAsync(policy),"cross-shop supplier/policy denied");scope.StoreId=1;await Reject(()=>repo.PlanAsync(null,null,null,30,1,10),"forged shop/location");scope.LocationId=1;scope.TenantId=99999;await Reject(()=>repo.PlanAsync(null,null,null,30,1,10),"cross-tenant");scope.TenantId=1;scope.UserId=99999;await Reject(()=>repo.PlanAsync(null,null,null,30,1,10),"restricted actor");scope.UserId=1;
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE module_name='Replenishment'")>=2,"existing audit history used");
   await setup.ExecuteAsync("DELETE FROM variant_supplier_identifiers WHERE variant_id=@vid AND supplier_id=@supplier",new{vid=ids[1],supplier});Check((await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single().Policy is null,"removed mapping invalidates policy without blocking existing catalog edits");await setup.ExecuteAsync("INSERT INTO variant_supplier_identifiers(variant_id,supplier_id,supplier_sku) VALUES(@vid,@supplier,'RP-ITEM')",new{vid=ids[1],supplier});
   Directory.CreateDirectory("tests/bin/replenishment-fixtures");await File.WriteAllTextAsync("tests/bin/replenishment-fixtures/records.json",JsonSerializer.Serialize(plan,new JsonSerializerOptions(JsonSerializerDefaults.Web)));await File.WriteAllTextAsync("tests/bin/replenishment-fixtures/suppliers.json",JsonSerializer.Serialize(await repo.SuppliersAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web)));
   using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IReplenishmentRepository>();s.AddSingleton<IReplenishmentRepository>(repo);s.RemoveAll<IBranchRepository>();s.AddSingleton<IBranchRepository>(new FakeBranchRepository{State=new(){IsOwner=true,IsActive=true,CatalogRegistrationEnhancementsAvailable=true}});s.AddSingleton<IConfiguration>(config);}));using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
   Check((await client.GetAsync("/api/replenishment?days=30")).IsSuccessStatusCode,"actual planning HTTP");await File.WriteAllTextAsync("tests/bin/replenishment-fixtures/Index.html",await client.GetStringAsync("/Replenishment"));Check((await client.GetAsync("/api/replenishment?days=2")).StatusCode==System.Net.HttpStatusCode.BadRequest,"invalid API lookback rejected");
   Check((await client.GetAsync("/api/replenishment/export?term=RP-TEST")).IsSuccessStatusCode,"scoped export HTTP");
   var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
   policy.Version=1;policy.LeadDays=6;Check((await client.PutAsJsonAsync("/api/replenishment/policy",policy)).IsSuccessStatusCode,"actual policy API with antiforgery");
   var apiRow=(await repo.PlanAsync("RP-TEST-01",null,null,30,1,10)).Rows.Single();var apiRequest=new ReplenishmentDraftRequest(Guid.NewGuid(),supplier,30,[new(ids[1],apiRow.Fingerprint,apiRow.Suggested,55,0)]);var apiDraft=await client.PostAsJsonAsync("/api/replenishment/draft",apiRequest);Check(apiDraft.IsSuccessStatusCode,"actual draft API persists through existing engine: "+await apiDraft.Content.ReadAsStringAsync());
   Check((await client.PostAsJsonAsync("/api/replenishment/draft",apiRequest)).IsSuccessStatusCode,"actual draft API retry");Check(await State()==baseline,"API policies/drafts do not post stock/payments");
   using(var noCsrf=web.CreateClient(new(){AllowAutoRedirect=false})){noCsrf.DefaultRequestHeaders.Add("Test-Role","Admin");Check((await noCsrf.PostAsJsonAsync("/api/replenishment/draft",apiRequest)).StatusCode==System.Net.HttpStatusCode.BadRequest,"draft mutation requires antiforgery");}
   client.DefaultRequestHeaders.Add("Test-Deny-Action","REPLENISHMENT:POLICY_EDIT");Check((await client.PutAsJsonAsync("/api/replenishment/policy",policy)).StatusCode==System.Net.HttpStatusCode.Forbidden,"policy API action denial");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
   client.DefaultRequestHeaders.Add("Test-Deny","REPLENISHMENT");Check((await client.GetAsync("/api/replenishment")).StatusCode==System.Net.HttpStatusCode.Forbidden,"planning API denied without module permission");client.DefaultRequestHeaders.Remove("Test-Deny");
   client.DefaultRequestHeaders.Add("Test-Deny-Action","REPLENISHMENT:EXPORT");Check((await client.GetAsync("/api/replenishment/export")).StatusCode==System.Net.HttpStatusCode.Forbidden,"export action permission denied");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
   config["Features:Replenishment"]="false";Check((await client.GetAsync("/api/replenishment")).StatusCode==System.Net.HttpStatusCode.NotFound,"feature-off API");Check((await client.GetAsync("/Replenishment")).StatusCode==System.Net.HttpStatusCode.NotFound,"feature-off Razor");config["Features:Replenishment"]="true";
   Console.WriteLine("PASS Replenishment PostgreSQL/API integration in disposable clone.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH(FORCE)");}
 }
}
