using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class DraftLifecycleChecks
{
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";var database="draft_lifecycle_"+Guid.NewGuid().ToString("N");
  await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
  try{
   await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();
   foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3]) is >=70 and <=78).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false)");
   var legacy=Guid.NewGuid();await c.ExecuteAsync("INSERT INTO billing_drafts(draft_id,label,payload) VALUES(@legacy,'Recoverable legacy',jsonb_build_object('draftId',@legacy::text,'cart','[]'::jsonb,'savedAt',extract(epoch FROM clock_timestamp())*1000))",new{legacy});
   var before=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY draft_id)::text FROM billing_drafts d");var migration=await File.ReadAllTextAsync("database/migrations/079_bill_draft_lifecycle.sql");await c.ExecuteAsync(migration);
   const string fields="ARRAY['draft_status','revision','editor_id','lease_until','held_at','completed_at','discarded_at','completed_sale_id','last_operation_id','last_operation_action']";
   Check(before==await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d)-"+fields+" ORDER BY draft_id)::text FROM billing_drafts d"),"079 preserves legacy UUIDs, DRF numbers, payload JSON, timestamps and ownership");
   Check(await c.ExecuteScalarAsync<bool>("SELECT bool_and(draft_status='LEGACY') FROM billing_drafts"),"Ambiguous historical drafts remain explicitly LEGACY and recoverable");
   var metadata=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY draft_id)::text FROM billing_drafts d");await c.ExecuteAsync(migration);Check(metadata==await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY draft_id)::text FROM billing_drafts d"),"Lifecycle migration reruns without changing existing records");
   // Preserve 079 upgrade assertions, then exercise recovery against the current schema.
   foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=80).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false)");
   var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();var db=new DbConnectionFactory(cfg,new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,ProfileDataVisibilityAvailable=true});
   using var factory=new Factory();using var app=factory.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database},{"ConnectionStrings:NotificationWorker",host+database},{"Notifications:WorkerEnabled","false"},{"Notifications:WhatsApp:Enabled","false"}})));
   using var client=app.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");var csrf=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
   async Task<JsonObject> Success(HttpResponseMessage r){var text=await r.Content.ReadAsStringAsync();if(!r.IsSuccessStatusCode)throw new Exception(r.StatusCode+": "+text);return JsonNode.Parse(text)!.AsObject();}
   long Rev(JsonObject d)=>d["revision"]!.GetValue<long>();
   object Command(Guid editor,long revision,Guid? operation=null)=>new{editorId=editor,revision,operationId=operation??Guid.NewGuid()};
   async Task<JsonObject> Act(Guid id,string action,Guid editor,long revision)=>await Success(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/{action}",Command(editor,revision)));
   async Task<JsonObject> Save(Guid id,Guid editor,long revision,JsonObject payload)=>await Success(await client.PutAsJsonAsync($"/api/sales/billing/drafts/{id}",new{label="Ranga",payload,editorId=editor,revision,operationId=Guid.NewGuid()}));
   var financeBefore=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM payments)||':'||(SELECT count(*) FROM inventory_transactions)||':'||(SELECT count(*) FROM gst_documents)");
   var id=Guid.NewGuid();var editorA=Guid.NewGuid();var editorB=Guid.NewGuid();
   var payload=JsonNode.Parse("{\"customerId\":\"10\",\"customerLabel\":\"Ranga / 9876543210\",\"notes\":\"Keep this note\",\"estimatedTotal\":50,\"cart\":[{\"productId\":0,\"productName\":\"Manual cord\",\"quantity\":2,\"sellingPrice\":25,\"discountAmount\":0,\"manualItem\":{\"name\":\"Manual cord\",\"mrp\":30,\"taxRuleId\":0}}]}")!.AsObject();payload["draftId"]=id.ToString();payload["savedAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
   var active=await Save(id,editorA,0,payload);var number=active["draftNumber"]!.GetValue<string>();Check(active["draftStatus"]!.GetValue<string>()=="ACTIVE","First persistence creates Active with exclusive editor and DRF reference");
   var firstOperation=await c.ExecuteScalarAsync<Guid>("SELECT last_operation_id FROM billing_drafts WHERE draft_id=@id",new{id});
   var retrySave=await Success(await client.PutAsJsonAsync($"/api/sales/billing/drafts/{id}",new{label="Ranga",payload,editorId=editorA,revision=0,operationId=firstOperation}));
   Check(Rev(retrySave)==Rev(active),"Retry after an uncertain save response reuses the same operation without another revision or number");
   var conflictingPayload=payload.DeepClone().AsObject();conflictingPayload["notes"]="Different request";
   Check((int)(await client.PutAsJsonAsync($"/api/sales/billing/drafts/{id}",new{label="Ranga",payload=conflictingPayload,editorId=editorA,revision=0,operationId=firstOperation})).StatusCode==409,"Reusing a save operation ID for different contents is rejected");
   var heldList=await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts?paged=true");Check(heldList!["total"]!.GetValue<int>()==0,"Held list excludes both Active and ambiguous legacy drafts");
   var recovery=await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts?paged=true&recovery=true");Check(recovery!["total"]!.GetValue<int>()>=1,"Separate recovery view exposes legacy bills without inventing Held state");
   var held=await Act(id,"hold",editorA,Rev(active));Check(held["draftStatus"]!.GetValue<string>()=="HELD"&&held["draftNumber"]!.GetValue<string>()==number,"Hold transitions the same numbered draft to Held");
   Check((await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts?paged=true"))!["total"]!.GetValue<int>()==1,"Held draft appears exactly once");
   Check((int)(await client.PutAsJsonAsync($"/api/sales/billing/drafts/{id}",new{label="Stale",payload,editorId=editorA,revision=Rev(active),operationId=Guid.NewGuid()})).StatusCode==409,"Delayed autosave after Hold is rejected");
   var races=await Task.WhenAll(new[]{editorA,editorB}.Select(e=>client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",new{editorId=e,revision=Rev(held),operationId=Guid.NewGuid()})));
   Check(races.Count(r=>r.IsSuccessStatusCode)==1&&races.Count(r=>(int)r.StatusCode==409)==1,"Two sessions racing Resume yield one exclusive editor and one conflict");
   var winner=races[0].IsSuccessStatusCode?editorA:editorB;var loser=winner==editorA?editorB:editorA;active=await Success(races.First(r=>r.IsSuccessStatusCode));
   Check((await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts?paged=true"))!["total"]!.GetValue<int>()==0,"Resumed draft disappears from the backend Held list");
   var heartbeat=await Act(id,"heartbeat",winner,Rev(active));Check(Rev(heartbeat)==Rev(active),"Heartbeat extends lease without changing content revision");
   Check((int)(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/heartbeat",Command(loser,Rev(active)))).StatusCode==409,"Other session cannot renew or steal a live lease");
   var full=payload["cart"]!.DeepClone();payload["cart"]=new JsonArray();payload["estimatedTotal"]=0;active=await Save(id,winner,Rev(active),payload);
   var empty=await client.GetFromJsonAsync<JsonObject>($"/api/sales/billing/drafts/{id}");Check(empty!["cart"]!.AsArray().Count==0&&empty["customerLabel"]!.GetValue<string>().StartsWith("Ranga")&&empty["notes"]!.GetValue<string>()=="Keep this note"&&empty["draftNumber"]!.GetValue<string>()==number,"Removing final item preserves customer, notes, identity, number and Active state");
   Check((int)(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/hold",Command(winner,Rev(active)))).StatusCode==409,"Existing empty-hold policy rejects Hold without discarding the Active bill");
   Check((await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts?paged=true"))!["total"]!.GetValue<int>()==0,"Empty Active draft remains excluded from Held");
   await c.ExecuteAsync("UPDATE billing_drafts SET lease_until=clock_timestamp()-interval '1 second' WHERE draft_id=@id",new{id});
   active=await Success(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",new{editorId=loser,revision=Rev(active),operationId=Guid.NewGuid(),recover=true}));winner=loser;
   Check(active["draftStatus"]!.GetValue<string>()=="ACTIVE","Expired editor lease can be recovered explicitly without permanent locks");
   await Act(id,"release",winner,Rev(active));active=await Success(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",new{editorId=editorA,revision=Rev(active),operationId=Guid.NewGuid(),recover=true}));winner=editorA;
   Check(active["draftNumber"]!.GetValue<string>()==number,"Browser-close release permits immediate recovery with the original DRF number");
   payload["cart"]=full;active=await Save(id,winner,Rev(active),payload);held=await Act(id,"hold",winner,Rev(active));
   var other=Guid.NewGuid();var otherPayload=payload.DeepClone().AsObject();otherPayload["draftId"]=other.ToString();var current=await Save(other,winner,0,otherPayload);
   var badSwitch=await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",new{editorId=winner,revision=Rev(held)-1,operationId=Guid.NewGuid(),currentDraftId=other,currentRevision=Rev(current),currentAction="HOLD"});
   Check((int)badSwitch.StatusCode==409&&await c.ExecuteScalarAsync<string>("SELECT draft_status FROM billing_drafts WHERE draft_id=@other",new{other})=="ACTIVE","Failed target Resume rolls back protection changes to the current bill atomically");
   var switchId=Guid.NewGuid();var switchBody=new{editorId=winner,revision=Rev(held),operationId=switchId,currentDraftId=other,currentRevision=Rev(current),currentAction="HOLD"};
   active=await Success(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",switchBody));var repeat=await Success(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",switchBody));
   Check(Rev(active)==Rev(repeat)&&await c.ExecuteScalarAsync<string>("SELECT draft_status FROM billing_drafts WHERE draft_id=@other",new{other})=="HELD","Hold Current & Resume is atomic and idempotent after an uncertain response");
   current=(await client.GetFromJsonAsync<JsonObject>($"/api/sales/billing/drafts/{other}"))!;
   await Success(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,$"/api/sales/billing/drafts/{other}"){Content=JsonContent.Create(Command(winner,Rev(current)))}));
   Check(await c.ExecuteScalarAsync<string>("SELECT draft_status FROM billing_drafts WHERE draft_id=@other",new{other})=="DISCARDED"&&(int)(await client.GetAsync($"/api/sales/billing/drafts/{other}")).StatusCode==404,"Discard retains terminal audit record and makes the selected bill non-resumable");
   Check(financeBefore==await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM payments)||':'||(SELECT count(*) FROM inventory_transactions)||':'||(SELECT count(*) FROM gst_documents)"),"Hold, Resume, empty edits, recovery and Discard post no invoice/payment/stock/GST records");
   await using(var runtime=db.CreateConnection()){try{await runtime.ExecuteAsync("UPDATE billing_drafts SET draft_status='HELD' WHERE draft_id=@id",new{id});throw new Exception("Raw lifecycle write accepted");}catch(PostgresException e)when(e.SqlState=="42501"){Console.WriteLine("PASS: Direct runtime writes cannot bypass lifecycle functions");}}
   client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:CREATE");Check((int)(await client.GetAsync("/api/sales/billing/drafts?paged=true")).StatusCode==403,"Lifecycle endpoints retain billing authorization");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
   var otherStore=await c.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'LIFECYCLE2','Lifecycle second shop') RETURNING store_id");
   await c.ExecuteAsync("UPDATE store_locations SET store_id=@otherStore WHERE tenant_id=1 AND location_id=2",new{otherStore});
   var otherUser=await c.ExecuteScalarAsync<int>("SELECT user_id FROM users WHERE tenant_id=1 AND user_id<>1 LIMIT 1");
   foreach(var scope in new[]{new BusinessScope{TenantId=1,UserId=1,StoreId=otherStore,LocationId=2},new BusinessScope{TenantId=1,UserId=otherUser,StoreId=1,LocationId=1}})
   {
    await using var isolated=new DbConnectionFactory(cfg,scope).CreateConnection();
    Check(await isolated.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_drafts WHERE draft_id=@id",new{id})==0,"Another shop/cashier cannot read the active draft");
    try{await isolated.ExecuteAsync("SELECT sf_draft_action('RESUME',@id,@editor,@rev,@operation,NULL,NULL)",new{id,editor=Guid.NewGuid(),rev=Rev(active),operation=Guid.NewGuid()});throw new Exception("Foreign draft transition accepted");}catch(PostgresException e)when(e.SqlState is "P0001" or "42501"){Console.WriteLine("PASS: Another shop/cashier cannot acquire draft editing ownership");}
   }
   var customer=await new CustomerRepository(db).CreateAsync(new(){CustomerName="Ranga",MobileNumber="9861200044"});var sales=new SalesRepository(db);var company=await new CompanySettingsRepository(db).GetAsync();
   var request=new CreateSaleRequest{RequestId=id,LocationId=1,CustomerId=customer,PricesIncludeTax=company.DefaultTaxMode=="INCLUSIVE",DraftEditorId=winner,DraftRevision=Rev(active),Items=[new(){Quantity=2,SellingPrice=25,ManualItem=new(){Name="Manual cord",Mrp=30,TaxRuleId=0}}]};
   var quote=await sales.QuoteAsync(request);request.Payments=[new(){PaymentMode="CASH",Amount=quote.TotalAmount}];
   held=await Act(id,"hold",winner,Rev(active));try{await sales.CreateSaleAsync(request,1);throw new Exception("Held bill checkout accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: Held bills cannot be finalized without Resume");}
   active=await Success(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",new{editorId=winner,revision=Rev(held),operationId=Guid.NewGuid()}));request.DraftRevision=Rev(active);
   request.Items[0].ManualItem!.Mrp=1;try{await sales.CreateSaleAsync(request,1);throw new Exception("Invalid MRP accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS: Failed invoice validation leaves draft Active and recoverable");}request.Items[0].ManualItem!.Mrp=30;
   var sale=await sales.CreateSaleAsync(request,1);Check(await sales.CreateSaleAsync(request,1)==sale,"Successful checkout retry returns one invoice without duplicate posting");
   Check(await c.ExecuteScalarAsync<bool>("SELECT draft_status='COMPLETED' AND completed_sale_id=@sale FROM billing_drafts WHERE draft_id=@id",new{sale,id}),"Draft becomes Completed with invoice reference in the successful checkout transaction");
   Check((int)(await client.PostAsJsonAsync($"/api/sales/billing/drafts/{id}/resume",new{editorId=winner,revision=Rev(active),operationId=Guid.NewGuid(),recover=true})).StatusCode==409,"Completed draft cannot Resume or Recover");
   Console.WriteLine("Lifecycle HTTP/database suite passed against disposable database only.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH(FORCE)");}
 }
 static void Check(bool passed,string message){if(!passed)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
