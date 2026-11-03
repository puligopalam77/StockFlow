using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
namespace StockFlow.MasterDataChecks;
internal static class MultiStoreDatabaseChecks
{
 public static async Task Run(bool through025Only=false,bool brandCategoriesOnly=false,bool sharedSessionsOnly=false,bool staffAccountsOnly=false)
 {
  var db="stores_test_"+Guid.NewGuid().ToString("N");
  var cs=$"Host=127.0.0.1;Port=55441;Database={db};Username=postgres";
  await using var admin=new NpgsqlConnection(cs.Replace($"Database={db}","Database=postgres"));await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db);
  try {
   var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
   await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
   foreach(var f in Directory.GetFiles(Path.Combine(root!.FullName,"database/scripts"),"*.sql").Where(f=>!Path.GetFileName(f).StartsWith("001")&&!Path.GetFileName(f).StartsWith("006")).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   foreach(var f in Directory.GetFiles(Path.Combine(root.FullName,"database/migrations"),"*.sql").Order()) {
    if(through025Only && string.CompareOrdinal(Path.GetFileName(f),"026")>=0)continue;
    if(Path.GetFileName(f).StartsWith("020"))await c.ExecuteAsync("""
     INSERT INTO roles(role_name) VALUES('Admin');
     INSERT INTO users(role_id,full_name,username,password_hash) SELECT role_id,'Owner','owner','unused' FROM roles WHERE role_name='Admin';
     INSERT INTO categories(category_name) VALUES('General');
     INSERT INTO products(category_id,product_name,sku,selling_price) VALUES(1,'Shirt','SHIRT',100);
     INSERT INTO inventory_transactions(product_id,variant_id,transaction_type,quantity,created_by) VALUES(1,1,'PURCHASE',10,1);
     """);
    if(brandCategoriesOnly && Path.GetFileName(f).StartsWith("032"))await c.ExecuteAsync("INSERT INTO brands(tenant_id,brand_name) VALUES(1,'Legacy brand'); UPDATE products SET brand_id=(SELECT brand_id FROM brands WHERE brand_name='Legacy brand') WHERE product_id=1;");
    Console.WriteLine(Path.GetFileName(f));await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   }
   await c.ExecuteAsync("""
    SELECT set_config('app.tenant_id','1',false);
    INSERT INTO user_module_permissions(tenant_id,user_id,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export) SELECT 1,1,module_id,true,true,true,true,true,true FROM modules ON CONFLICT DO NOTHING;
    INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,1,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE a.is_active AND m.is_active ON CONFLICT DO NOTHING;
    INSERT INTO tenants(tenant_code,business_name) VALUES('SECOND','Second business');
    INSERT INTO categories(tenant_id,category_name) VALUES(2,'General');
    """);
   var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true};
   var factory=new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build(),scope);
   if(sharedSessionsOnly){await SharedSessionDatabaseChecks.Run(c,cs);return;}
   if(brandCategoriesOnly){await BrandCategoryDatabaseChecks.Run(c,factory);return;}
   await ProductRegistrationDatabaseChecks.Run(factory);
   await using(var scoped=factory.CreateConnection()){
    Check(await scoped.ExecuteScalarAsync<string>("SELECT current_user")=="stockflow_runtime","runtime uses restricted role");
    Check(await scoped.ExecuteScalarAsync<int>("SELECT count(*) FROM categories")==1,"tenant RLS isolates catalog");
    await Reject(()=>scoped.ExecuteAsync("INSERT INTO products(category_id,product_name) VALUES(2,'forged')"),"cross-tenant parent rejected");
   }
   var branches=new BranchRepository(factory,scope);var second=await branches.CreateStoreAsync(new(){StoreCode="SECOND",StoreName="Second store"});
   Check(await Stock()==10,"legacy stock backfilled once into main location");
   var loc=(await branches.LocationsAsync()).Single(l=>l.StoreId==second).LocationId;
   var transfers=new TransferRepository(factory);var id=await transfers.CreateAsync(new(){RequestId=Guid.NewGuid(),FromLocationId=1,ToLocationId=loc,Items=[new(){VariantId=1,Quantity=4}]});
   await transfers.ActAsync(id,"request",new(){RequestId=Guid.NewGuid()});await transfers.ActAsync(id,"approve",new(){RequestId=Guid.NewGuid()});
   var line=(await transfers.GetAsync(id))!.Items.Single();
   var dispatch=new TransferActionRequest{RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=line.TransferItemId,Quantity=4}]};
   await c.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=false FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='STOCK_TRANSFER' AND a.action_code='DISPATCH'");
   await Reject(()=>transfers.ActAsync(id,"dispatch",dispatch),"legacy module grant cannot bypass revoked dispatch action");
   await c.ExecuteAsync("UPDATE user_action_permissions p SET is_allowed=true FROM module_actions a JOIN modules m USING(module_id) WHERE p.module_action_id=a.module_action_id AND p.user_id=1 AND m.module_code='STOCK_TRANSFER' AND a.action_code='DISPATCH'");
   await transfers.ActAsync(id,"dispatch",dispatch);await transfers.ActAsync(id,"dispatch",dispatch);
   Check(await Stock()==6,"dispatch deducts source once");
   scope.LocationId=loc;scope.StoreId=second;Check(await Stock()==0,"dispatch does not credit destination");
   await transfers.ActAsync(id,"receive",new(){RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=line.TransferItemId,Quantity=2}]});
   Check(await Stock()==2&&(await transfers.GetAsync(id))!.Status=="PARTIALLY_RECEIVED","partial receipt credits actual quantity");
   await Reject(()=>transfers.ActAsync(id,"receive",new(){RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=line.TransferItemId,Quantity=3}]}),"over receipt rejected");
   await transfers.ActAsync(id,"discrepancy",new(){RequestId=Guid.NewGuid(),Notes="Damaged in delivery",Items=[new(){TransferItemId=line.TransferItemId,Quantity=1,Resolution="DAMAGED"}]});
   await transfers.ActAsync(id,"discrepancy",new(){RequestId=Guid.NewGuid(),Notes="Missing package",Items=[new(){TransferItemId=line.TransferItemId,Quantity=1,Resolution="LOST"}]});
   Check(await Stock()==2&&(await transfers.GetAsync(id))!.Status=="RECEIVED","damage/loss reconciled without saleable destination stock");
   Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE reference_type='TRANSFER' AND transaction_type IN('PURCHASE','SALE')")==0,"transfers are excluded from purchase and sale movements");
   Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM purchases")==0&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM sales")==0,"transfers do not create purchase or invoice headers");
   scope.LocationId=0;scope.StoreId=0;scope.Consolidated=true;Check(await Stock()==8,"company total reconciles transfer and losses");
   await c.ExecuteAsync("""
    INSERT INTO users(tenant_id,role_id,full_name,username,password_hash) SELECT 1,role_id,'Employee','employee','unused' FROM roles WHERE role_name='Admin';
    INSERT INTO user_store_access(tenant_id,user_id,store_id,is_default) VALUES(1,2,1,true);
    INSERT INTO user_module_permissions SELECT 2,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export,1 FROM user_module_permissions WHERE user_id=1;
    INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT tenant_id,2,module_action_id,is_allowed FROM user_action_permissions WHERE user_id=1;
    """);
   scope.UserId=2;scope.IsOwner=false;
   Check((await branches.LocationsAsync()).Count()==1,"employee sees only assigned store");
   scope.LocationId=loc;scope.StoreId=second;Check(await Stock()==0,"forged unassigned location cannot reveal stock");
   scope.LocationId=1;scope.StoreId=1;scope.Consolidated=false;
   await Reject(()=>transfers.ActAsync(id,"receive",new(){RequestId=Guid.NewGuid()}),"source-only employee cannot receive destination");
   scope.UserId=1;scope.IsOwner=true;
   var battery=await c.ExecuteScalarAsync<int>("INSERT INTO products(tenant_id,category_id,product_name,sku,is_serial_tracked,selling_price) VALUES(1,1,'Battery','BATTERY',true,100) RETURNING product_id");
   var variant=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@battery",new{battery});
   var unit=await c.ExecuteScalarAsync<long>("INSERT INTO product_units(tenant_id,location_id,product_id,variant_id,serial_number,internal_barcode,unit_status) VALUES(1,1,@battery,@variant,'SERIAL-ONE','UNIT-LABEL','IN_STOCK') RETURNING product_unit_id",new{battery,variant});
   await c.ExecuteAsync("INSERT INTO inventory_transactions(tenant_id,location_id,product_id,variant_id,product_unit_id,transaction_type,quantity,created_by) VALUES(1,1,@battery,@variant,@unit,'PURCHASE',1,1)",new{battery,variant,unit});
   var serialTransfer=await transfers.CreateAsync(new(){RequestId=Guid.NewGuid(),FromLocationId=1,ToLocationId=loc,Items=[new(){VariantId=variant,Quantity=1}]});
   foreach(var action in new[]{"request","approve"})await transfers.ActAsync(serialTransfer,action,new(){RequestId=Guid.NewGuid()});
   var serialLine=(await transfers.GetAsync(serialTransfer))!.Items.Single();
   Check((await transfers.DispatchUnitsAsync(serialTransfer,serialLine.TransferItemId,"UNIT-LABEL",0)).Single().ProductUnitId==unit,"source can select dispatch units by label");
   await transfers.ActAsync(serialTransfer,"dispatch",new(){RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=serialLine.TransferItemId,Quantity=1,UnitIds=[unit]}]});
   Check(await new ScanRepository(factory).FindByCodeAsync("SERIAL-ONE") is null,"dispatched serial unavailable at source");
   var inventory=new InventoryRepository(factory);
   Check((await inventory.GetHistoryAsync(battery,null,null,0,variant)).Any(m=>m.TransactionType=="TRANSFER_OUT"&&m.SerialNumber=="SERIAL-ONE"),"source history preserves serial while unit is in transit");
   scope.LocationId=loc;scope.StoreId=second;
   Check(await new ScanRepository(factory).FindByCodeAsync("SERIAL-ONE") is null,"transit serial unavailable at destination");
   Check((await transfers.GetAsync(serialTransfer))!.Items.Single().Units.Single().Barcodes.Contains("UNIT-LABEL"),"destination can scan incoming unit labels without source inventory access");
   await transfers.ActAsync(serialTransfer,"receive",new(){RequestId=Guid.NewGuid(),Items=[new(){TransferItemId=serialLine.TransferItemId,Quantity=1,UnitIds=[unit]}]});
   Check((await new ScanRepository(factory).FindByCodeAsync("SERIAL-ONE"))?.ProductUnitId==unit,"receipt moves exact serial to destination");
   Check((await inventory.GetBalancesAsync("UNIT-LABEL",false,false)).Single().VariantId==variant,"store inventory search accepts individual barcode");
   var adjustment=new StockAdjustmentRequest{RequestId=Guid.NewGuid(),LocationId=loc,ProductId=battery,VariantId=variant,TransactionType="DAMAGED",Quantity=1,Reason="Counted damaged unit",ProductUnitIds=[unit]};
   var audit=await inventory.AdjustAsync(adjustment,1);
   Check(await inventory.AdjustAsync(adjustment,1)==audit,"adjustment retry returns original audit receipt");
   Check((await inventory.GetBalancesAsync(null,true,false)).Any(b=>b.VariantId==variant&&b.CurrentStock==0),"zero stock appears in scoped low stock list");
   try{await inventory.AdjustAsync(new(){RequestId=Guid.NewGuid(),LocationId=loc,ProductId=battery,VariantId=variant,TransactionType="ADJUSTMENT_OUT",Quantity=1,Reason="Invalid repeat removal",ProductUnitIds=[unit]},1);throw new Exception("stock underflow permitted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS insufficient stock adjustment blocked");}
   var sales=new SalesRepository(factory);
   var sale=await sales.CreateSaleAsync(new(){LocationId=loc,RequestId=Guid.NewGuid(),Items=[new(){ProductId=1,VariantId=1,Quantity=1,SellingPrice=100}],Payments=[new(){Amount=100}]},1);
   Check(await c.ExecuteScalarAsync<long>("SELECT store_id FROM sales WHERE sale_id=@sale",new{sale})==second,"sale records selected store");
   scope.LocationId=1;scope.StoreId=1;
   Check(await sales.GetByIdAsync(sale) is null,"sale inaccessible from another stock location");
   scope.UserId=2;scope.IsOwner=false;
   await c.ExecuteAsync("DELETE FROM user_store_access WHERE user_id=2");
   Check(!(await branches.LocationsAsync()).Any(),"revoked store membership takes effect immediately");
   var password="Temporary-Only-123!";var hash=new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword("",password);
   if(!through025Only){await PeriodReportingDatabaseChecks.Run(c,factory,scope,second,loc);await ShopOperationsDatabaseChecks.Run(c,factory);}
   scope.UserId=1;scope.IsOwner=true;
   var pilotUser=through025Only?0:await AdministrationRolloutDatabaseChecks.CreatePilot(c,factory);
   if(staffAccountsOnly){await AdministrationRolloutDatabaseChecks.ChangeRole(c,factory,pilotUser);return;}
   await c.ExecuteAsync("UPDATE users SET password_hash=@hash,must_change_password=false WHERE user_id=1",new{hash});
   var loginScope=new BusinessScope();var loginFactory=new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build(),loginScope);
   var auth=new SecurityRepository(loginFactory);var login=await auth.LoginAsync(new(){TenantCode="DEFAULT",Username="owner",Password=password});
   Check(login.Success&&login.TenantId==1&&login.LocationId==1,"login returns validated tenant and default location");
   Check(!(await auth.LoginAsync(new(){TenantCode="SECOND",Username="owner",Password=password})).Success,"same username cannot authenticate against another tenant");
   var provision=(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/inserts/004_provision_tenant.sql")))
       .Replace("REPLACE_BUSINESS_CODE","THIRD").Replace("REPLACE_BUSINESS_NAME","Third business").Replace("REPLACE_OWNER_NAME","Third owner")
       .Replace("REPLACE_USERNAME","owner").Replace("REPLACE_ASPNET_PASSWORD_HASH",hash);
   await c.ExecuteAsync(provision);
   Check((await auth.LoginAsync(new(){TenantCode="THIRD",Username="owner",Password=password})).Success,"manual provisioning template creates independent working owner login");
   using(var web=new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder=>{
    builder.UseContentRoot(Path.Combine(root.FullName,"src/StockFlow.Web"));builder.UseEnvironment("Development");
    builder.ConfigureServices(services=>{services.RemoveAll<DbConnectionFactory>();services.AddScoped(sp=>new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build(),sp.GetRequiredService<BusinessScope>()));});
   })){
    using var client=web.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
    var html=await client.GetStringAsync("/Account/Login");
    var token=System.Net.WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    var response=await client.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.TenantCode","DEFAULT"},{"Input.Username","owner"},{"Input.Password",password},{"__RequestVerificationToken",token}}));
    Check((int)response.StatusCode==302,"real web login issues cookie");
    var reportResponse=await client.GetAsync("/Reports/Stores");Check(reportResponse.IsSuccessStatusCode,"store review page accessible after login: "+reportResponse.Headers.Location);
    html=await reportResponse.Content.ReadAsStringAsync();token=Regex.Match(html,"name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value;
    client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token);client.DefaultRequestHeaders.Add("X-StockFlow-Location","1");
    var registration = await client.PostAsJsonAsync("/api/products",new {productName="HTTP pilot battery",categoryId=1,trackingType="SERIAL",hasVariants=false,variants=new[]{new{variantName="DEFAULT",sku="HTTP-PILOT-BAT",barcodeSource="INTERNAL",attributes=new Dictionary<string,string>{{"Capacity","150 Ah"},{"Voltage","12 V"}}}}});
    Check((int)registration.StatusCode==201,"combined registration binds and saves through the real authenticated HTTP API: "+await registration.Content.ReadAsStringAsync());
    var registered=await registration.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
    var receivedProductId=registered!["productId"]!.GetValue<int>();
    var receivedVariantId=await c.ExecuteScalarAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@receivedProductId AND tenant_id=1",new{receivedProductId});
    var receivedSupplierId=await c.ExecuteScalarAsync<int>("INSERT INTO suppliers(tenant_id,supplier_name) VALUES(1,'Inline receipt check supplier') RETURNING supplier_id");
    var order=await client.PostAsJsonAsync("/api/purchases",new{supplierId=receivedSupplierId,locationId=1,purchaseDate="2026-10-03",receivedDate="2026-10-03",completeImmediately=false,items=new[]{new{productId=receivedProductId,variantId=receivedVariantId,quantity=1,purchasePrice=4000,serialNumbers=new[]{"HTTP-RECEIPT-SERIAL"}}}});
    Check((int)order.StatusCode==201,"inline registered product is accepted by purchase draft API: "+await order.Content.ReadAsStringAsync());
    var receivedPurchaseId=(await order.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>())!["purchaseId"]!.GetValue<long>();
    Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=@receivedProductId",new{receivedProductId})==0,"saving receipt draft adds no stock");
    Check((int)(await client.PostAsJsonAsync("/api/purchases/"+receivedPurchaseId+"/complete",new{})).StatusCode==204,"saving stock receipt completes the same saved purchase");
    Check((int)(await client.PostAsJsonAsync("/api/purchases/"+receivedPurchaseId+"/complete",new{})).StatusCode==204,"interrupted completion retry accepts the original purchase ID");
    Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM inventory_transactions WHERE product_id=@receivedProductId",new{receivedProductId})==1 && await c.ExecuteScalarAsync<int>("SELECT count(*) FROM product_units WHERE product_id=@receivedProductId",new{receivedProductId})==1,"receipt completion retry posts exactly one stock movement and physical unit");
    await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=false WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='PRODUCT_VARIANTS' AND a.action_code='CREATE')");
    Check((int)(await client.PostAsJsonAsync("/api/products",new {productName="Forbidden configuration",categoryId=1,trackingType="SERIAL",variants=new[]{new{variantName="DEFAULT",sku="HTTP-FORBIDDEN"}}})).StatusCode==403,"product create cannot bypass revoked variant creation permission");
    await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=true WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='PRODUCT_VARIANTS' AND a.action_code='CREATE')");
    Check((int)(await client.PostAsJsonAsync("/api/branches/context",new{locationId=loc,storeId=second})).StatusCode==204,"authorized cookie location switch succeeds");
    Check((int)(await client.PostAsJsonAsync("/api/inventory/adjustments",new{})).StatusCode==409,"stale browser tab rejected before mutation");
    client.DefaultRequestHeaders.Remove("X-StockFlow-Location");client.DefaultRequestHeaders.Add("X-StockFlow-Location",loc.ToString());
    Check((int)(await client.PostAsJsonAsync("/api/branches/context",new{locationId=999999,storeId=0})).StatusCode==403,"forged context rejected by HTTP API");
    Check((int)(await client.GetAsync("/api/branches/summary")).StatusCode==200,"location summary executes with real RLS");
    if(pilotUser>0){
     using var cashier=web.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
     async Task SignIn(){var loginHtml=await cashier.GetStringAsync("/Account/Login");var csrf=System.Net.WebUtility.HtmlDecode(Regex.Match(loginHtml,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);var loginResponse=await cashier.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.TenantCode","DEFAULT"},{"Input.Username","pilot.cashier"},{"Input.Password","Pilot-only passphrase 123"},{"__RequestVerificationToken",csrf}}));Check((int)loginResponse.StatusCode==302,"pilot cashier authenticates with a real cookie");}
     await SignIn();
     Check((int)(await cashier.GetAsync("/api/inventory")).StatusCode==200,"pilot cashier checks assigned store stock");
     Check((int)(await cashier.GetAsync("/api/reports/activity?from=2026-03-01&to=2026-03-31&locationId="+loc)).StatusCode==200,"pilot report uses session store despite a supplied foreign location");
     await AdministrationRolloutDatabaseChecks.Revoke(factory,pilotUser);
     Check((int)(await cashier.GetAsync("/api/inventory")).StatusCode==401,"actual cookie is revoked after an action permission change");
     await SignIn();
     Check((int)(await cashier.PostAsJsonAsync("/api/sales",new{})).StatusCode==403,"fresh cashier session cannot use revoked billing action");
     await new SecurityRepository(factory).SetStatusAsync(pilotUser,new(){ExpectedVersion=2,IsActive=false},1);
     Check((int)(await cashier.GetAsync("/api/inventory")).StatusCode is 401 or 403,"deactivation immediately blocks an existing cookie");
    }
   }
   Console.WriteLine("Multi-store database checks passed.");
   if(!through025Only){await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/inventory_transfer_checks.sql")));Console.WriteLine("PASS optional read-only inventory/transfer query script executes");}
   if(!through025Only){await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/period_reporting_checks.sql")));Console.WriteLine("PASS optional read-only period reporting query script executes");}
   if(!through025Only){await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/administration_rollout_checks.sql")));Console.WriteLine("PASS optional read-only administration rollout query script executes");}
   if(!through025Only){await c.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/queries/module_hierarchy_checks.sql")));Console.WriteLine("PASS optional read-only module hierarchy query script executes");}
   async Task<long> Stock(){await using var s=factory.CreateConnection();return await s.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=1");}
  } finally {NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)");}
 }
 static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);Console.WriteLine("PASS "+msg);}
 static async Task Reject(Func<Task> f,string msg){try{await f();}catch(PostgresException){Console.WriteLine("PASS "+msg);return;}throw new Exception(msg);}
}
