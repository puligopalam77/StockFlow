using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;
internal static class StoreManagementChecks
{
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
 static async Task Reject(Func<Task> call,string label){try{await call();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or KeyNotFoundException or System.Data.DBConcurrencyException or PostgresException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
  var db="store_management_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{
   await using var setup=new NpgsqlConnection(host+db);await setup.OpenAsync();
   foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order())await setup.ExecuteAsync(await File.ReadAllTextAsync(f));
   await setup.ExecuteAsync("UPDATE users SET must_change_password=false WHERE tenant_id=1 AND user_id=1; UPDATE user_action_permissions SET is_allowed=true WHERE tenant_id=1 AND user_id=1; SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
   var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true,ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true,BusinessProfileCode="ELECTRICAL"};
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db}}).Build();var factory=new DbConnectionFactory(config,scope);var repo=new BranchRepository(factory,scope);
   var history=await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM purchases)::text||':'||(SELECT count(*) FROM gst_documents)::text");
   StoreRequest New()=>new(){StoreCode="NEW-RETRY",StoreName="Synthetic retry store",AddressLine1="12 Explicit Street",City="Mumbai",State="Maharashtra",PinCode="400001",Email="store@example.test"};
   var ids=await Task.WhenAll(repo.CreateStoreAsync(New()),repo.CreateStoreAsync(New()));var id=ids[0];Check(ids.Distinct().Count()==1,"Concurrent identical creation returns the same store");
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM store_locations WHERE store_id=@id AND location_type='SALES_FLOOR'",new{id})==1,"Store creation commits exactly one Main stock location");
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE module_name='Stores' AND action_name='CREATE' AND record_id=@id",new{id})==1,"Retry does not duplicate the creation audit");
   var inactiveRequest=new StoreRequest{StoreCode="NEW-INACTIVE",StoreName="Inactive head office",IsActive=false,IsHeadOffice=true};
   var inactiveId=await repo.CreateStoreAsync(inactiveRequest);
   var inactiveDetail=await repo.StoreDetailsAsync(inactiveId);
   Check(!inactiveDetail.Store.IsActive&&inactiveDetail.Store.IsHeadOffice&&inactiveDetail.Locations.Count()==1,"Initial status and existing head-office type are saved with one default sales location");
   Check(await repo.CreateStoreAsync(inactiveRequest)==inactiveId,"Inactive shop creation retry returns the same shop");
   inactiveRequest.IsActive=true;await Reject(()=>repo.CreateStoreAsync(inactiveRequest),"Same code with different initial status is rejected");
   inactiveRequest.IsActive=false;inactiveRequest.IsHeadOffice=false;await Reject(()=>repo.CreateStoreAsync(inactiveRequest),"Same code with different shop type is rejected");
   await repo.UpdateStoreAsync(inactiveId,new(){StoreCode=inactiveDetail.Store.StoreCode,StoreName="Renamed inactive head office",IsActive=false,ExpectedVersion=inactiveDetail.Store.Version});
   Check((await repo.StoreDetailsAsync(inactiveId)).Store.IsHeadOffice,"Older edit clients preserve the existing shop type when omitted");
   await using(var runtime=factory.CreateConnection()){Check(!await runtime.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM stores WHERE store_id=@inactiveId AND is_active)",new{inactiveId}),"Inactive new shop is excluded from active staff-assignment choices");}
   var counts=await repo.DirectoryAsync(new());Check(counts.ActiveStoreCount+counts.InactiveStoreCount==counts.StoreCount&&counts.InactiveStoreCount>=1,"Shop totals include active and inactive counts across the directory");
   var bad=New();bad.StoreName="Different name";await Reject(()=>repo.CreateStoreAsync(bad),"Duplicate code with different details rejected");bad=New();bad.StoreCode="BAD-GST";bad.GstNumber="27AAPFU0939F1ZV";await Reject(()=>repo.CreateStoreAsync(bad),"New free-text store GSTIN rejected");
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM stores WHERE store_code='BAD-GST'")==0,"Rejected store request leaves no orphan store");
   await setup.ExecuteAsync("CREATE FUNCTION public.test_reject_store_location() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF EXISTS(SELECT 1 FROM public.stores WHERE store_id=NEW.store_id AND store_code='ROLLBACK-STORE') THEN RAISE EXCEPTION 'Synthetic location failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER test_reject_store_location BEFORE INSERT ON public.store_locations FOR EACH ROW EXECUTE FUNCTION public.test_reject_store_location()");
   await Reject(()=>repo.CreateStoreAsync(new(){StoreCode="ROLLBACK-STORE",StoreName="Must roll back"}),"Main-stock failure rolls back store creation");
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM stores WHERE store_code='ROLLBACK-STORE'")==0,"Failed main-stock insert leaves no store row");
   await setup.ExecuteAsync("DROP TRIGGER test_reject_store_location ON public.store_locations; DROP FUNCTION public.test_reject_store_location()");
   var detail=await repo.StoreDetailsAsync(id);Check(detail.Store.AddressLine1=="12 Explicit Street"&&detail.Store.PinCode=="400001"&&detail.Store.GstStatus=="Not assigned","Directory reload returns saved metadata and actual unmapped GST status");
   var update=new StoreUpdateRequest{StoreCode=detail.Store.StoreCode,StoreName="Updated operational name",Address=detail.Store.Address,AddressLine1=detail.Store.AddressLine1,City=detail.Store.City,State=detail.Store.State,PinCode=detail.Store.PinCode,ExpectedVersion=detail.Store.Version};
   await repo.UpdateStoreAsync(id,update);await Reject(()=>repo.UpdateStoreAsync(id,update),"Stale store edit rejected");
   detail=await repo.StoreDetailsAsync(id);Check(detail.Store.Version==update.ExpectedVersion+1&&detail.Store.StoreName==update.StoreName,"Successful edit increments version once");
   var legacy=await repo.CreateStoreAsync(new(){StoreCode="LEGACY-ADDRESS",StoreName="Legacy address",Address="Original unsplit\naddress",City="Saved town"});
   detail=await repo.StoreDetailsAsync(legacy);await repo.UpdateStoreAsync(legacy,new(){StoreCode=detail.Store.StoreCode,StoreName="Renamed legacy",Address=detail.Store.Address,City=detail.Store.City,ExpectedVersion=detail.Store.Version});
   Check((await repo.StoreDetailsAsync(legacy)).Store.Address=="Original unsplit\naddress","Unrelated edit preserves the exact legacy address");
   var warehouse=new LocationRequest{LocationName="Synthetic central warehouse",LocationType="WAREHOUSE"};var wh=await repo.CreateLocationAsync(warehouse);Check(wh==await repo.CreateLocationAsync(warehouse),"Central warehouse creation is retry safe");
   await Reject(()=>repo.CreateLocationAsync(new(){LocationName="Invalid central floor",LocationType="SALES_FLOOR"}),"Central location cannot impersonate a retail sales floor");
   await Reject(()=>repo.CreateLocationAsync(new(){StoreId=long.MaxValue,LocationName="Forged store",LocationType="STOCKROOM"}),"Forged store ID cannot receive a location");
   detail=await repo.StoreDetailsAsync(legacy);await repo.UpdateStoreAsync(legacy,new(){StoreCode=detail.Store.StoreCode,StoreName=detail.Store.StoreName,Address=detail.Store.Address,City=detail.Store.City,ExpectedVersion=detail.Store.Version,IsActive=false});
   Check(!(await repo.StoreDetailsAsync(legacy)).Store.IsActive,"Unused store can be deactivated without deleting locations");
   await Reject(()=>repo.CreateLocationAsync(new(){StoreId=legacy,LocationName="Inactive parent",LocationType="STOCKROOM"}),"Inactive store cannot receive new stock locations");
   detail=await repo.StoreDetailsAsync(1);await Reject(()=>repo.UpdateStoreAsync(1,new(){StoreCode=detail.Store.StoreCode,StoreName=detail.Store.StoreName,Address=detail.Store.Address,City=detail.Store.City,State=detail.Store.State,MobileNumber=detail.Store.MobileNumber,ExpectedVersion=detail.Store.Version,IsActive=false}),"Store with history or configuration cannot be deactivated");
   await using(var runtime=factory.CreateConnection()){await Reject(()=>runtime.ExecuteAsync("UPDATE stores SET store_code='FORGED-CODE' WHERE store_id=@id",new{id}),"Database trigger protects permanent store codes");}
   for(var n=0;n<12;n++)await repo.CreateStoreAsync(new(){StoreCode="PAGE-"+n,StoreName="Paging store "+n});
   foreach(var size in new[]{10,25,50,100}){var page=await repo.DirectoryAsync(new(){PageSize=size,Search="Paging store"});Check(page.Total==12&&page.Rows.Count()==Math.Min(size,12),"Directory uses real filtered paging size "+size);}
   Check((await repo.DirectoryAsync(new(){Page=999,PageSize=10,Search="Paging store"})).Page==2,"Out of range directory page is clamped");await Reject(()=>repo.DirectoryAsync(new(){PageSize=11}),"Invalid page size rejected");
   Check((await repo.DirectoryAsync(new(){Status="INACTIVE"})).Rows.Any(r=>r.StoreId==legacy),"Inactive status filter returns saved lifecycle state");
   var hiddenSetupStore=await repo.CreateStoreAsync(new(){StoreCode="OTHER-PROFILE-SETUP",StoreName="Other profile setup"});
   await setup.ExecuteAsync("INSERT INTO expense_categories(tenant_id,store_id,business_profile_code,category_id,name) VALUES(1,@id,'BIKE_ACCESSORIES',@category,'Synthetic other-profile setup')",new{id=hiddenSetupStore,category=Guid.NewGuid()});
   detail=await repo.StoreDetailsAsync(hiddenSetupStore);await Reject(()=>repo.UpdateStoreAsync(hiddenSetupStore,new(){StoreCode=detail.Store.StoreCode,StoreName=detail.Store.StoreName,ExpectedVersion=detail.Store.Version,IsActive=false}),"Other-profile setup hidden from operational RLS still prevents deactivation");
   var gst=new GstRepository(factory);var today=await setup.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");var business=await gst.SaveBusinessAsync(new(){LegalName="Synthetic authoritative identity",Address="Registered address",StateCode="27",PinCode="400001",Pan="AAPFU0939F"});
   var registration=await gst.SaveRegistrationAsync(new(){BusinessId=business,Gstin="27AAPFU0939F1ZV",StateCode="27",Address="Registered premises",EffectiveFrom=today});
   detail=await repo.StoreDetailsAsync(id);await repo.UpdateStoreAsync(id,new(){StoreCode=detail.Store.StoreCode,StoreName=detail.Store.StoreName,AddressLine1=detail.Store.AddressLine1,City=detail.Store.City,State=detail.Store.State,PinCode=detail.Store.PinCode,GstBusinessId=business,ExpectedVersion=detail.Store.Version});
   StoreRequest Mapped(string code)=>new(){StoreCode=code,StoreName="Synthetic mapped shop",AddressLine1="12 Shop Road",City="Mumbai",State="Maharashtra",PinCode="400001",GstBusinessId=business,GstRegistrationId=registration,GstSetupMode="EXISTING"};
   var mappedRequest=Mapped("MAP-AT-CREATE");var mappedId=await repo.CreateStoreAsync(mappedRequest);
   Check(await repo.CreateStoreAsync(mappedRequest)==mappedId,"Atomic creation and identical retry reuse the existing shop and GST mapping");
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM gst_shop_mappings WHERE store_id=@mappedId",new{mappedId})==1,"Shop creation creates one effective audited mapping without a duplicate GST registration");
   Check((await repo.StoreDetailsAsync(mappedId)).Store.SetupReadiness=="Shop setup complete","Readiness derives from address, legal business, sales location and effective mapping");
   await setup.ExecuteAsync("CREATE FUNCTION public.test_reject_shop_mapping() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF EXISTS(SELECT 1 FROM public.stores WHERE store_id=NEW.store_id AND store_code='ROLLBACK-MAPPING') THEN RAISE EXCEPTION 'Synthetic mapping failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER zzz_test_reject_shop_mapping BEFORE INSERT ON public.gst_shop_mappings FOR EACH ROW EXECUTE FUNCTION public.test_reject_shop_mapping()");
   await Reject(()=>repo.CreateStoreAsync(Mapped("ROLLBACK-MAPPING")),"Mapping failure rolls back the entire new-shop transaction");
   Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM stores WHERE store_code='ROLLBACK-MAPPING'")==0,"Failed registration assignment leaves no shop or dependent sales location");
   await setup.ExecuteAsync("DROP TRIGGER zzz_test_reject_shop_mapping ON public.gst_shop_mappings; DROP FUNCTION public.test_reject_shop_mapping()");
   var badState=Mapped("BAD-STATE");badState.State="Karnataka";await Reject(()=>repo.CreateStoreAsync(badState),"Wrong-state registration rejected before shop creation");
   var badEntity=Mapped("BAD-ENTITY");badEntity.GstBusinessId=long.MaxValue;await Reject(()=>repo.CreateStoreAsync(badEntity),"Unavailable legal entity rejected before shop creation");
   var badMode=Mapped("BAD-NONGST");badMode.GstSetupMode="UNREGISTERED";await Reject(()=>repo.CreateStoreAsync(badMode),"Regular GST registration cannot impersonate an unregistered setup");
   var noAddress=Mapped("BAD-ADDRESS");noAddress.PinCode=null;await Reject(()=>repo.CreateStoreAsync(noAddress),"Mapping at creation requires a complete structured shop address");
   var pendingId=await repo.CreateStoreAsync(new(){StoreCode="PENDING-SETUP",StoreName="Pending shop",GstSetupMode="LATER"});
   Check((await repo.StoreDetailsAsync(pendingId)).Store.SetupReadiness=="Address incomplete","Incomplete draft setup is saved without claiming billing readiness");
   var partialId=await repo.CreateStoreAsync(new(){StoreCode="PARTIAL-ADDRESS",StoreName="Partial address draft",GstSetupMode="LATER",PinCode="560001"});
   Check((await repo.StoreDetailsAsync(partialId)).Store.PinCode=="560001"&&(await repo.StoreDetailsAsync(partialId)).Store.SetupReadiness=="Address incomplete","Configure Later accepts a partial structured address without enabling billing");
   await Reject(()=>repo.CreateStoreAsync(new(){StoreCode="BAD-DRAFT-PIN",StoreName="Invalid PIN draft",GstSetupMode="LATER",PinCode="123"}),"Draft setup still validates entered PIN codes");
   await Reject(()=>repo.CreateStoreAsync(new(){StoreCode="BAD-INDIAN-STATE",StoreName="Invalid state draft",GstSetupMode="LATER",AddressLine1="Shop road",State="Unknown State"}),"Structured shop addresses reject unsupported Indian states");
   await Reject(()=>repo.CreateStoreAsync(new(){StoreCode="DISTRICT-WITHOUT-STATE",StoreName="District draft",GstSetupMode="LATER",District="Chennai"}),"A District cannot be saved without selecting its State");
   await Reject(()=>gst.SaveMappingAsync(new(){RegistrationId=registration,StoreId=pendingId,BusinessProfileCode="ELECTRICAL",EffectiveFrom=today}),"Existing GST mapping API also rejects missing shop legal entity/state");
   await using(var runtime=factory.CreateConnection())await runtime.ExecuteAsync("UPDATE stores SET management_details=management_details-'ShopSetupRequired' WHERE store_id=@pendingId",new{pendingId});
   Check(await setup.ExecuteScalarAsync<bool>("SELECT (management_details->>'ShopSetupRequired')::boolean FROM stores WHERE store_id=@pendingId",new{pendingId}),"Older clients cannot remove new-shop setup enforcement");
   var nonGstRegistration=await gst.SaveRegistrationAsync(new(){BusinessId=business,RegistrationType="UNREGISTERED",StateCode="27",Address="Unregistered premises",EffectiveFrom=today});
   var nonGstRequest=Mapped("NONGST-AT-CREATE");nonGstRequest.GstSetupMode="UNREGISTERED";nonGstRequest.GstRegistrationId=nonGstRegistration;
   var nonGstId=await repo.CreateStoreAsync(nonGstRequest);Check((await repo.StoreDetailsAsync(nonGstId)).Store.SetupReadiness=="Non-GST shop setup complete","Explicit unregistered setup is supported without inventing a GSTIN");
   foreach(var shop in new[]{pendingId,nonGstId}){
    scope.StoreId=shop;scope.LocationId=await setup.ExecuteScalarAsync<long>("SELECT location_id FROM store_locations WHERE store_id=@shop AND location_type='SALES_FLOOR'",new{shop});
    await using var runtime=factory.CreateConnection();await runtime.OpenAsync();await using var tx=await runtime.BeginTransactionAsync();
    try{
     await runtime.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,store_id,location_id,business_profile_code,status) VALUES(@number,1,@shop,@location,'ELECTRICAL','COMPLETED')",new{number="SETUP-"+Guid.NewGuid().ToString("N")[..10],shop,location=scope.LocationId},tx);
     Check(shop==nonGstId,"Explicit non-GST setup passes the shop finalization guard");
    }catch(PostgresException e)when(shop==pendingId&&e.SqlState=="23514"&&e.MessageText.Contains("Complete shop setup")){Check(true,"Pending shop cannot finalize a sale through direct runtime SQL");}
    await tx.RollbackAsync();
   }
   scope.StoreId=1;scope.LocationId=1;
   detail=await repo.StoreDetailsAsync(mappedId);await Reject(()=>repo.UpdateStoreAsync(mappedId,new(){StoreCode=detail.Store.StoreCode,StoreName=detail.Store.StoreName,State="Karnataka",ExpectedVersion=detail.Store.Version}),"Changing a mapped shop state cannot invalidate its active mapping");
   await gst.SaveMappingAsync(new(){RegistrationId=registration,StoreId=id,BusinessProfileCode="ELECTRICAL",EffectiveFrom=today});await gst.SaveMappingAsync(new(){RegistrationId=registration,StoreId=1,BusinessProfileCode="ELECTRICAL",EffectiveFrom=today});
   Check((await repo.StoreDetailsAsync(id)).Store.GstStatus=="Mapped"&&(await repo.StoreDetailsAsync(1)).Store.GstStatus=="Mapped","Two stores can use the same authoritative registration without duplicate GSTIN entry");
   var futureStore=await repo.CreateStoreAsync(new(){StoreCode="FUTURE-GST",StoreName="Future GST mapping",State="Maharashtra",GstBusinessId=business});
   var unregistered=await gst.SaveRegistrationAsync(new(){BusinessId=business,RegistrationType="UNREGISTERED",StateCode="27",Address="Independent unregistered premises",EffectiveFrom=today.AddDays(1)});
   await gst.SaveMappingAsync(new(){RegistrationId=unregistered,StoreId=futureStore,BusinessProfileCode="ELECTRICAL",EffectiveFrom=today.AddDays(1)});
   Check((await repo.StoreDetailsAsync(futureStore)).Store.GstStatus=="Review dates / status","A future registration and mapping never imply current readiness");
   detail=await repo.StoreDetailsAsync(id);await Reject(()=>repo.UpdateStoreAsync(id,new(){StoreCode=detail.Store.StoreCode,StoreName=detail.Store.StoreName,Address=detail.Store.Address,ExpectedVersion=detail.Store.Version,IsActive=false}),"Configured GST mapping prevents unused-store deactivation");
   await repo.SetAccessAsync(2,new(){StoreIds=[id],DefaultStoreId=id,WarehouseLocationIds=[wh]});
   var assignment=await repo.AccessAsync(2);Check(assignment.DefaultStoreId==id&&assignment.StoreIds.SequenceEqual([id])&&assignment.WarehouseLocationIds.SequenceEqual([wh]),"Existing staff-access transaction saves exact store/default and central warehouse assignments");
   await Reject(()=>repo.SetAccessAsync(2,new(){StoreIds=[long.MaxValue],DefaultStoreId=long.MaxValue}),"Forged staff store assignment is rejected before deleting saved access");
   Check((await repo.AccessAsync(2)).StoreIds.SequenceEqual([id]),"Failed staff assignment preserves previous access");
   var staffScope=new BusinessScope{TenantId=1,UserId=2,StoreId=1,LocationId=1};var staff=new BranchRepository(new DbConnectionFactory(config,staffScope),staffScope);await Reject(()=>staff.CreateStoreAsync(New()),"Non-owner cannot create a store");
   staffScope.IsOwner=true;await Reject(()=>staff.CreateStoreAsync(New()),"Forged in-memory owner flag cannot override actual database membership");
   await setup.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=false WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='STORES' AND a.action_code='EDIT')");
   await using(var runtime=factory.CreateConnection()){await Reject(()=>runtime.ExecuteAsync("UPDATE stores SET store_name='Unauthorized edit' WHERE store_id=@id",new{id}),"Direct runtime write still requires the actual EDIT action");}
   await setup.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=true WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='STORES' AND a.action_code='EDIT')");
   scope.TenantId=999;await Reject(()=>repo.StoreDetailsAsync(id),"Cross-tenant store detail is invisible");scope.TenantId=1;
   Check(await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM inventory_transactions)::text||':'||(SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM purchases)::text||':'||(SELECT count(*) FROM gst_documents)::text")==history,"Store metadata/location changes do not post stock, invoices, purchases or GST documents");
   using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();s.RemoveAll<DbConnectionFactory>();s.AddScoped(sp=>new DbConnectionFactory(config,sp.GetRequiredService<BusinessScope>()));s.RemoveAll<IBranchRepository>();s.AddScoped<IBranchRepository,BranchRepository>();s.RemoveAll<ISecurityRepository>();s.AddScoped<ISecurityRepository,SecurityRepository>();s.RemoveAll<IBusinessProfileRepository>();s.AddScoped<IBusinessProfileRepository,BusinessProfileRepository>();}));
   await RoleManagementChecks.Run(setup,factory);
   await RoleTemplateChecks.Run(setup,factory);
   await StaffRegistrationChecks.Run(setup,factory);
   await LiveRoleAccessChecks.Run(setup,factory);
   using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");client.DefaultRequestHeaders.Add("Test-Version",(await setup.ExecuteScalarAsync<long>("SELECT security_version FROM users WHERE user_id=1")).ToString());var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
   var response=await client.GetAsync("/api/branches/directory");Check(response.IsSuccessStatusCode,"Real HTTP directory uses database permissions and RLS");
   var html=await client.GetStringAsync("/Admin/Stores");Directory.CreateDirectory("tests/bin/store-management-fixtures");await File.WriteAllTextAsync("tests/bin/store-management-fixtures/Stores.html",html);Check(html.Contains("store-dialog")&&!html.Contains("name=\"gstNumber\""),"Real Razor directory has focused forms and no duplicate GST input");
   var createHtml=await client.GetStringAsync("/Admin/Stores/Create");await File.WriteAllTextAsync("tests/bin/store-management-fixtures/Create.html",createHtml);
   Check(createHtml.Contains("data-create-page=\"true\"")&&createHtml.Contains("stores-page-editor"),"Add Shop has an authorized dedicated Razor page using the shared form");
   Directory.CreateDirectory("tests/bin/role-management-fixtures");
   await File.WriteAllTextAsync("tests/bin/role-management-fixtures/Modules.json",System.Text.Json.JsonSerializer.Serialize(await new SecurityRepository(factory).GetModulesAsync(),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
   foreach(var (path,name) in new[]{("/Admin/Roles","List"),("/Admin/Roles/Create","Create"),("/Admin/Roles/Edit/2","Edit"),("/Admin/Roles/View/2","View"),("/Admin/Users","Users")})await File.WriteAllTextAsync("tests/bin/role-management-fixtures/"+name+".html",await client.GetStringAsync(path));
   Check((await client.GetAsync("/api/admin/roles/directory")).IsSuccessStatusCode,"Role directory HTTP uses real database authorization");
   var httpEdit=await client.PutAsJsonAsync("/api/branches/stores/"+id,new StoreUpdateRequest{StoreCode="NEW-RETRY",StoreName="Stale HTTP",ExpectedVersion=0});Check((int)httpEdit.StatusCode==409,"Stale HTTP edit returns actionable conflict");
   Check((int)(await client.GetAsync("/api/branches/stores/"+long.MaxValue)).StatusCode==404,"Forged HTTP detail ID does not reveal another store");
   client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((int)(await client.PostAsJsonAsync("/api/branches/stores",New())).StatusCode==400,"Store mutation requires CSRF token");
   Console.WriteLine("Store management checks passed in disposable database; fixture is synthetic.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH(FORCE)");}
 }
}
