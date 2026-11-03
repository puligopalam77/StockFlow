using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
namespace StockFlow.MasterDataChecks;
internal static class DraftNumberingChecks
{
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";var database="draft_numbers_"+Guid.NewGuid().ToString("N");
  await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
  try{
   await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();
   foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3]) is >=70 and <78).Order())await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.location_id','1',false),set_config('app.store_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false)");
   var store2=await c.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'DRAFTTEST','Numbering shop two') RETURNING store_id");
   await c.ExecuteAsync("UPDATE store_locations SET store_id=@store2 WHERE tenant_id=1 AND location_id=2",new{store2});
   await c.ExecuteAsync("ALTER TABLE billing_drafts ADD COLUMN created_at timestamptz");
   Guid older=Guid.Parse("ffffffff-0000-4000-8000-000000000001"),newer=Guid.Parse("00000000-0000-4000-8000-000000000002"),unknown1=Guid.Parse("00000000-0000-4000-8000-000000000003"),unknown2=Guid.Parse("ffffffff-0000-4000-8000-000000000004"),secondShop=Guid.NewGuid();
   var payload=JsonSerializer.Serialize(new{cart=new[]{new{productId=0,productName="Legacy manual",quantity=2,sellingPrice=25,discountAmount=1,discountPercent=2,manualItem=new{name="Legacy manual",mrp=40,taxRuleId=0},sku=(string?)null}},customerId="10",customerLabel="Old customer / 9876543210",notes="Untouched",discount="5",mode="UPI",reference="UNPAID-DRAFT",savedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
   foreach(var (id,created,updated,loc) in new[]{(older,(DateTime?)new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),DateTime.UtcNow,1L),(newer,(DateTime?)new DateTime(2026,2,1,0,0,0,DateTimeKind.Utc),DateTime.UtcNow.AddDays(-2),1L),(unknown1,(DateTime?)null,DateTime.UtcNow,1L),(unknown2,(DateTime?)null,DateTime.UtcNow.AddDays(-1),1L),(secondShop,(DateTime?)null,DateTime.UtcNow,2L)})
    await c.ExecuteAsync("INSERT INTO billing_drafts(tenant_id,user_id,location_id,draft_id,label,payload,created_at,updated_at,business_profile_code) VALUES(1,1,@loc,@id,'Legacy',jsonb_set(@payload::jsonb,'{draftId}',to_jsonb(@id::text)),@created,@updated,'ELECTRICAL')",new{id,loc,payload,created,updated});
   var before=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY draft_id)::text FROM billing_drafts d");
   var finances=await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM payments)||':'||(SELECT count(*) FROM inventory_transactions)||':'||(SELECT count(*) FROM gst_documents)");
   var migration=await File.ReadAllTextAsync("database/migrations/078_sequential_draft_numbers.sql");await c.ExecuteAsync(migration);
   var after=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d)-ARRAY['store_id','draft_sequence','creation_time_source'] ORDER BY draft_id)::text FROM billing_drafts d");
   Check(before==after,"Migration preserves UUIDs, existing timestamps, full cart/payment JSON and ownership exactly");
   async Task<string?> Number(Guid id)=>await c.ExecuteScalarAsync<string>("SELECT sf_draft_number(draft_sequence) FROM billing_drafts WHERE draft_id=@id",new{id});
   Check(await Number(older)=="DRF-0001"&&await Number(newer)=="DRF-0002","Recovered creation order overrides reverse update order and UUID order");
   Check(await Number(unknown1)=="DRF-0003"&&await Number(unknown2)=="DRF-0005","Unknown creation dates use explicit stable UUID fallback after dated drafts (including the pre-existing fixture draft)");
   Check(await Number(secondShop)=="DRF-0001","Two shops backfill independently");
   Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_drafts WHERE created_at IS NULL AND creation_time_source='UNKNOWN'")==4,"Unrecoverable creation dates remain NULL and flagged UNKNOWN");
   var assigned=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY draft_id)::text FROM billing_drafts d");await c.ExecuteAsync(migration);
   Check(assigned==await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY draft_id)::text FROM billing_drafts d"),"Rerunning migration preserves every number and metadata value");
   foreach(var row in await c.QueryAsync("SELECT store_id,count(*) AS drafts,count(*) FILTER(WHERE creation_time_source='UNKNOWN') AS unknown FROM billing_drafts GROUP BY store_id ORDER BY store_id"))Console.WriteLine($"TEST MIGRATION: shop {row.store_id}: {row.drafts} drafts, {row.unknown} unknown creation times");
   var cashier=await c.ExecuteScalarAsync<int>("SELECT user_id FROM users WHERE tenant_id=1 AND user_id<>1 LIMIT 1");
   await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,@cashier,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='SALES' AND a.action_code='CREATE' ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true; INSERT INTO user_store_access(tenant_id,user_id,store_id,is_default,is_active) VALUES(1,@cashier,1,false,true) ON CONFLICT(user_id,store_id) DO UPDATE SET is_active=true; INSERT INTO user_location_access(tenant_id,user_id,location_id) VALUES(1,@cashier,1) ON CONFLICT DO NOTHING",new{cashier});
   async Task<string?> Save(Guid id,int actor=1,long shop=1,long location=1){var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();var db=new DbConnectionFactory(cfg,new BusinessScope{TenantId=1,UserId=actor,StoreId=shop,LocationId=location});await using var cn=db.CreateConnection();return await cn.ExecuteScalarAsync<string>("INSERT INTO billing_drafts(draft_id,label,payload) VALUES(@id,'Concurrent',jsonb_build_object('draftId',@id::text,'cart','[]'::jsonb,'savedAt',0)) ON CONFLICT(tenant_id,user_id,location_id,draft_id) DO UPDATE SET label=excluded.label RETURNING sf_draft_number(draft_sequence)",new{id});}
   var concurrent=await Task.WhenAll(Enumerable.Range(0,30).Select(i=>Save(Guid.NewGuid(),i%2==0?1:cashier)));
   Check(concurrent.Distinct().Count()==30,"Thirty concurrent creates by two cashiers receive unique shop numbers");
   var retryId=Guid.NewGuid();var retried=await Task.WhenAll(Enumerable.Range(0,10).Select(_=>Save(retryId)));Check(retried.Distinct().Count()==1,"Concurrent retries of the same UUID reuse one allocation");
   var oldNumber=retried[0];await c.ExecuteAsync("DELETE FROM billing_drafts WHERE draft_id=@retryId",new{retryId});Check(await Save(retryId)==oldNumber,"Retained ledger preserves allocation on same-ID recovery");
   await c.ExecuteAsync("DELETE FROM billing_drafts WHERE draft_id=@retryId",new{retryId});Check(await Save(Guid.NewGuid())!=oldNumber,"Discarded references are never reassigned to another draft");
   await c.ExecuteAsync("UPDATE billing_draft_counters SET last_number=9999 WHERE tenant_id=1 AND store_id=@store2",new{store2});
   Check(await Save(Guid.NewGuid(),1,store2,2)=="DRF-10000","Formatting and allocation continue beyond four digits");
   try{await Save(Guid.NewGuid(),1,store2,1);throw new Exception("Spoofed shop allocation was accepted");}catch(PostgresException e) when(e.SqlState=="42501"){Console.WriteLine("PASS: Location/shop mismatch cannot allocate another shop's number");}
   try{await c.ExecuteAsync("UPDATE billing_drafts SET draft_sequence=999999 WHERE draft_id=@older",new{older});throw new Exception("Assigned number was mutable");}catch(PostgresException e) when(e.SqlState=="23514"){Console.WriteLine("PASS: Assigned draft numbers cannot be edited");}
   await c.ExecuteAsync(await File.ReadAllTextAsync("database/migrations/079_bill_draft_lifecycle.sql"));
   using var factory=new Factory();using var app=factory.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database},{"ConnectionStrings:NotificationWorker",host+database},{"Notifications:WorkerEnabled","false"},{"Notifications:WhatsApp:Enabled","false"}})));
   using var client=app.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
   var response=await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts/"+older);Check(response!["draftId"]!.GetValue<string>()==older.ToString()&&response["draftNumber"]!.GetValue<string>()=="DRF-0001","Legacy UUID GET returns authoritative display number without changing its lookup key");
   var search=await client.GetFromJsonAsync<JsonObject>("/api/sales/billing/drafts?paged=true&recovery=true&search=drf-0001");Check(search!["items"]!.AsArray().Any(x=>x!["draftId"]!.GetValue<string>()==older.ToString()),"Search matches new display numbers case-insensitively");
   var editorId=Guid.NewGuid();var recovered=await client.PostAsJsonAsync($"/api/sales/billing/drafts/{older}/resume",new{editorId,revision=response["revision"]!.GetValue<long>(),operationId=Guid.NewGuid(),recover=true});recovered.EnsureSuccessStatusCode();var recoveredState=await recovered.Content.ReadFromJsonAsync<JsonObject>();
   response["draftNumber"]="DRF-999999";var body=new{label="Edited legacy",payload=response,editorId,revision=recoveredState!["revision"]!.GetValue<long>(),operationId=Guid.NewGuid()};var saved=await client.PutAsJsonAsync("/api/sales/billing/drafts/"+older+"?reference=true",body);saved.EnsureSuccessStatusCode();var result=await saved.Content.ReadFromJsonAsync<JsonObject>();Check(result!["draftNumber"]!.GetValue<string>()=="DRF-0001","Hold/edit response retains the existing number and ignores submitted display references");
   Check((int)(await client.GetAsync("/api/sales/billing/drafts/"+secondShop)).StatusCode==404,"Other-shop numbered drafts cannot be retrieved");
   client.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:CREATE");Check((int)(await client.GetAsync("/api/sales/billing/drafts?paged=true")).StatusCode==403,"Numbered draft API keeps existing billing permissions");client.DefaultRequestHeaders.Remove("Test-Deny-Action");
   Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM (SELECT tenant_id,store_id,draft_sequence FROM billing_draft_numbers GROUP BY 1,2,3 HAVING count(*)>1) d")==0,"Duplicate-number validation finds no duplicates");
   Check(finances==await c.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM payments)||':'||(SELECT count(*) FROM inventory_transactions)||':'||(SELECT count(*) FROM gst_documents)"),"Number migration and draft operations leave invoices, payments, stock and GST unchanged");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH(FORCE)");}
 }
 static void Check(bool passed,string message){if(!passed)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
