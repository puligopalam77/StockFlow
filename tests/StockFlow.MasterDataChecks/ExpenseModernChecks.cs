using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class ExpenseModernChecks
{
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
 static async Task Reject(Func<Task> f,string name){try{await f();}catch(Exception e)when(e is PostgresException or ValidationException or UnauthorizedAccessException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
 public static async Task Run()
 {
  const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";var db="expense_modern_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();await admin.ExecuteAsync("CREATE DATABASE "+db+" TEMPLATE product_edit_069_test");
  try{await using var c=new NpgsqlConnection(host+db);await c.OpenAsync();var preMobilePayee=Guid.NewGuid();var preDefaultCategory=Guid.NewGuid();var legacyExpense=Guid.NewGuid();string legacyExpenseSnapshot="";long emptyCategoryStore=0;foreach(var f in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order()){
    if(Path.GetFileName(f).StartsWith("093"))await c.ExecuteAsync("INSERT INTO expense_payees(tenant_id,store_id,business_profile_code,payee_id,name) VALUES(1,1,'ELECTRICAL',@preMobilePayee,'Before mobile support')",new{preMobilePayee});
    if(Path.GetFileName(f).StartsWith("094")){
     await c.ExecuteAsync("INSERT INTO expense_categories(tenant_id,store_id,business_profile_code,category_id,name) VALUES(1,1,'ELECTRICAL',@preDefaultCategory,'  SHOP RENT  ')",new{preDefaultCategory});
     emptyCategoryStore=await c.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'BEFORE-CATEGORY-DEFAULTS','Empty store before defaults') RETURNING store_id");
    }
    if(Path.GetFileName(f).StartsWith("095")){
     await c.ExecuteAsync("INSERT INTO expenses VALUES(1,1,1,'ELECTRICAL',@legacyExpense,@preDefaultCategory,@preMobilePayee,CURRENT_DATE,1180,'Historical expense before GST references','POSTED',1,1,now(),now())",new{legacyExpense,preDefaultCategory,preMobilePayee});
     legacyExpenseSnapshot=await c.ExecuteScalarAsync<string>("SELECT to_jsonb(e)::text FROM expenses e WHERE expense_id=@legacyExpense",new{legacyExpense})??throw new Exception("Legacy expense fixture missing");
    }
    await c.ExecuteAsync(await File.ReadAllTextAsync(f));
   }
   Check(await c.ExecuteScalarAsync<bool>("SELECT name='Before mobile support' AND mobile_number IS NULL FROM expense_payees WHERE payee_id=@preMobilePayee",new{preMobilePayee}),"Mobile migration preserves pre-existing payee identity and name");
   await c.ExecuteAsync("UPDATE users SET must_change_password=false WHERE user_id=1; UPDATE user_action_permissions SET is_allowed=true WHERE user_id=1; SET app.tenant_id='1'; SET app.user_id='1'; SET app.store_id='1'; SET app.location_id='1'; SET app.business_profile_code='ELECTRICAL'");
   var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+db}}).Build();var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};var factory=new DbConnectionFactory(config,scope);var repo=new ExpenseRepository(factory);var cat=await repo.MasterAsync("CATEGORY",Guid.NewGuid(),"Modern Rent");var payee=await repo.MasterAsync("PAYEE",Guid.NewGuid(),"Modern Landlord");
   await ExpenseCategoryChecks.Run(c,config,repo,preDefaultCategory,emptyCategoryStore);
   Check(cat==await repo.MasterAsync("CATEGORY",Guid.NewGuid(),"  modern rent  ")&&payee==await repo.MasterAsync("PAYEE",Guid.NewGuid(),"MODERN LANDLORD"),"Scoped master creation reuses case-insensitive existing names without merging stores");
   Check(await c.ExecuteScalarAsync<bool>("SELECT mobile_number IS NULL FROM expense_payees WHERE payee_id=@payee",new{payee}),"Name-only requests store NULL mobile numbers");
   var mobilePayee=await repo.MasterAsync("PAYEE",Guid.NewGuid(),"  Mobile Landlord  "," 9876543210 ");
   Check(await c.ExecuteScalarAsync<bool>("SELECT name='Mobile Landlord' AND mobile_number='9876543210' FROM expense_payees WHERE payee_id=@mobilePayee",new{mobilePayee}),"Payee name and optional mobile are trimmed and persisted");
   Check(await repo.MasterAsync("PAYEE",Guid.NewGuid()," mobile landlord ","9123456789")==mobilePayee&&await c.ExecuteScalarAsync<string>("SELECT mobile_number FROM expense_payees WHERE payee_id=@mobilePayee",new{mobilePayee})=="9876543210","Case-insensitive duplicates reuse identity without overwriting existing mobile");
   var parallelPayees=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>repo.MasterAsync("PAYEE",Guid.NewGuid(),i%2==0?"Concurrent payee":" CONCURRENT PAYEE ","9876543210")));
   Check(parallelPayees.Distinct().Count()==1&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expense_payees WHERE lower(name)='concurrent payee'")==1,"Concurrent duplicate payee submissions create one record");
   foreach(var mobile in new[]{"1234567890","98765","98765432101","98765abcde","+919876543210"})await Reject(()=>repo.MasterAsync("PAYEE",Guid.NewGuid(),"Invalid phone payee",mobile),"Reject invalid mobile "+mobile);
   await Reject(()=>repo.MasterAsync("PAYEE",Guid.NewGuid(),"   "),"Reject blank payee name");
   await Reject(()=>repo.MasterAsync("CATEGORY",Guid.NewGuid(),"Category with phone","9876543210"),"Mobile metadata is accepted only for payees");
   var blankMobile=await repo.MasterAsync("PAYEE",Guid.NewGuid(),"Blank mobile","  ");
   Check(await c.ExecuteScalarAsync<bool>("SELECT mobile_number IS NULL FROM expense_payees WHERE payee_id=@blankMobile",new{blankMobile}),"Optional blank mobile is stored as NULL");
   var legacyPayee=await c.ExecuteScalarAsync<Guid>("SELECT sf_expense_master('PAYEE',@id,'Legacy signature')",new{id=Guid.NewGuid()});
   Check(await c.ExecuteScalarAsync<bool>("SELECT mobile_number IS NULL FROM expense_payees WHERE payee_id=@legacyPayee",new{legacyPayee}),"Original SQL master signature remains compatible");
   await Reject(()=>c.ExecuteScalarAsync<Guid>("SELECT sf_expense_master('PAYEE',@id,'Invalid SQL mobile','1234567890')",new{id=Guid.NewGuid()}),"Database function rejects invalid mobile independently of the API");
   await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=false WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='EXPENSES' AND a.action_code='EDIT')");
   await Reject(()=>repo.MasterAsync("PAYEE",Guid.NewGuid(),"Denied payee","9876543210"),"Payee repository retains EDIT permission enforcement");
   await Reject(()=>c.ExecuteScalarAsync<Guid>("SELECT sf_expense_master('PAYEE',@id,'Denied SQL payee','9876543210')",new{id=Guid.NewGuid()}),"Mobile-aware database function retains EDIT permission enforcement");
   await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=true WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='EXPENSES' AND a.action_code='EDIT')");
   var today=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE");string Day(DateTime d)=>d.ToString("yyyy-MM-dd");
   var stock=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions");var gst=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents");
   var cash=new CashClosingRepository(factory,scope,Microsoft.Extensions.Options.Options.Create(new ShopTimeOptions()),TimeProvider.System);var cashBefore=(await cash.GetAsync(today)).Totals.ExpenseCash;
   object Details()=>new{categoryId=cat,payeeId=payee,incurredDate=Day(today.AddDays(-10)),amount=100,description="Modern atomic expense"};
   ExpenseOperation Record(decimal? paid,string method="CASH",Guid? operation=null)=>new(operation??Guid.NewGuid(),0,JsonSerializer.SerializeToElement(new{expense=Details(),payment=paid is {} amount?(object)new{paidDate=Day(today),amount,paymentMode=method,reference="Synthetic reference",confirmed=true}:null}));
   var unpaid=Guid.NewGuid();var unpaidOp=Record(null);await repo.RecordAsync(unpaid,unpaidOp);Check((await repo.GetAsync(unpaid)).GetProperty("status").GetString()=="POSTED"&&(await repo.GetAsync(unpaid)).GetProperty("paid").GetDecimal()==0,"Record unpaid expense creates no payment");
   await Reject(()=>repo.RecordAsync(unpaid,Record(10,operation:unpaidOp.OperationId)),"Changed retry cannot add an initial payment to a previously unpaid record");
   var partial=Guid.NewGuid();var op=Record(25);var versions=await Task.WhenAll(repo.RecordAsync(partial,op),repo.RecordAsync(partial,op));var row=await repo.GetAsync(partial);Check(versions.Distinct().Count()==1&&row.GetProperty("paid").GetDecimal()==25&&row.GetProperty("payments").GetArrayLength()==1,"Concurrent record/payment retries create one expense and one partial settlement");
   foreach(var method in new[]{"CASH","UPI","DEBIT_CARD","CREDIT_CARD","BANK_TRANSFER"}){var id=Guid.NewGuid();await repo.RecordAsync(id,Record(100,method));Check((await repo.GetAsync(id)).GetProperty("paid").GetDecimal()==100,"Fully paid recording uses actual expense method "+method);}
   Check((await cash.GetAsync(today)).Totals.ExpenseCash-cashBefore==125,"Atomic recording reconciles 25 partial plus 100 full cash to Cash Closing; digital methods excluded");
   var invalid=Guid.NewGuid();await Reject(()=>repo.RecordAsync(invalid,Record(101)),"Initial overpayment rejected");Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expenses WHERE expense_id=@id",new{id=invalid})==0&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expense_events WHERE expense_id=@id",new{id=invalid})==0,"Failed initial payment rolls back draft, posting and audit events atomically");
   var unconfirmed=new ExpenseOperation(Guid.NewGuid(),0,JsonSerializer.SerializeToElement(new{expense=Details(),payment=new{paidDate=Day(today),amount=1,paymentMode="UPI",confirmed=false}}));await Reject(()=>repo.RecordAsync(Guid.NewGuid(),unconfirmed),"Selecting a digital method alone cannot confirm an initial payment");
   var report=await repo.ReportAsync(today.AddDays(-10),today.AddDays(-2));var reported=report.GetProperty("expenses").EnumerateArray().Single(x=>x.GetProperty("expense_id").GetGuid()==partial);Check(reported.GetProperty("paid").GetDecimal()==0,"Report expense rows use payments through cutoff, not future all-time payments");
   foreach(var size in new[]{10,25,50,100}){var list=await repo.ListAsync(null,null,null,1,size,cat,payee,"POSTED","PARTIAL");Check(list.GetProperty("total").GetInt32()==1&&list.GetProperty("rows")[0].GetProperty("expense_id").GetGuid()==partial,"Authoritative category/payee/lifecycle/payment filter and page size "+size);}
   var draft=Guid.NewGuid();await ExpenseRecordingChecks.SeedLegacyAsync(c,draft,new(Guid.NewGuid(),0,JsonSerializer.SerializeToElement(Details())));await repo.RecordAsync(draft,new(Guid.NewGuid(),1,Record(null).Body));Check((await repo.GetAsync(draft)).GetProperty("status").GetString()=="POSTED","Legacy saved expense can be reviewed and recorded by the atomic action");
   await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=false WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='EXPENSES' AND a.action_code='PAY')");var denied=Guid.NewGuid();await Reject(()=>repo.RecordAsync(denied,Record(10)),"Initial payment checks its actual PAY permission");Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM expenses WHERE expense_id=@denied",new{denied})==0,"Payment permission failure rolls back expense recording");await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=true WHERE user_id=1 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='EXPENSES' AND a.action_code='PAY')");
   using var original=new Factory();using var web=original.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();s.RemoveAll<DbConnectionFactory>();s.AddScoped(sp=>new DbConnectionFactory(config,sp.GetRequiredService<BusinessScope>()));s.RemoveAll<IBranchRepository>();s.AddScoped<IBranchRepository,BranchRepository>();s.RemoveAll<ISecurityRepository>();s.AddScoped<ISecurityRepository,SecurityRepository>();s.RemoveAll<IBusinessProfileRepository>();s.AddScoped<IBusinessProfileRepository,BusinessProfileRepository>();}));
   using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");client.DefaultRequestHeaders.Add("Test-Version",(await c.ExecuteScalarAsync<long>("SELECT security_version FROM users WHERE user_id=1")).ToString());var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
   var httpId=Guid.NewGuid();var httpOp=Record(50);var responses=await Task.WhenAll(client.PostAsJsonAsync($"/api/expenses/{httpId}/record",httpOp),client.PostAsJsonAsync($"/api/expenses/{httpId}/record",httpOp));Check(responses.All(r=>r.IsSuccessStatusCode)&&(await repo.GetAsync(httpId)).GetProperty("paid").GetDecimal()==50,"Actual atomic record HTTP route is permission protected and retry safe");
   await ExpenseSettlementChecks.Run(c,repo,client,cat,payee,today);
   await ExpenseGstChecks.Run(c,factory,repo,client,payee,today,legacyExpense,legacyExpenseSnapshot);
   await ExpenseReportingChecks.Run(c,config,repo,client,payee,today);
   var httpPayeeId=Guid.NewGuid();var httpPayee=await client.PostAsJsonAsync("/api/expenses/masters/PAYEE",new{id=httpPayeeId,name="  HTTP payee  ",mobileNumber="9876543210"});
   Check(httpPayee.IsSuccessStatusCode&&(await httpPayee.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>()==httpPayeeId&&await c.ExecuteScalarAsync<string>("SELECT mobile_number FROM expense_payees WHERE payee_id=@httpPayeeId",new{httpPayeeId})=="9876543210","Existing HTTP master API persists optional payee mobile");
   var nameOnlyId=Guid.NewGuid();var nameOnlyResponse=await client.PostAsJsonAsync("/api/expenses/masters/PAYEE",new{id=nameOnlyId,name="Name-only HTTP payee"});
   Check(nameOnlyResponse.IsSuccessStatusCode&&await c.ExecuteScalarAsync<bool>("SELECT mobile_number IS NULL FROM expense_payees WHERE payee_id=@nameOnlyId",new{nameOnlyId}),"Existing name-only HTTP request bodies remain compatible");
   var duplicate=await client.PostAsJsonAsync("/api/expenses/masters/PAYEE",new{id=Guid.NewGuid(),name="http PAYEE",mobileNumber="9123456789"});var duplicateBody=await duplicate.Content.ReadFromJsonAsync<JsonObject>();
   Check(duplicate.IsSuccessStatusCode&&duplicateBody!["existing"]!.GetValue<bool>()&&duplicateBody["id"]!.GetValue<Guid>()==httpPayeeId,"API identifies an existing payee for a simple duplicate message");
   var httpCategoryId=Guid.NewGuid();var httpCategory=await client.PostAsJsonAsync("/api/expenses/masters/CATEGORY",new{id=httpCategoryId,name="  Council Fees  "});
   Check(httpCategory.IsSuccessStatusCode&&await c.ExecuteScalarAsync<bool>("SELECT name='Council Fees' AND NOT is_system_default FROM expense_categories WHERE category_id=@httpCategoryId",new{httpCategoryId}),"Existing category API trims custom names and retains user-created provenance");
   var categoryDuplicate=await client.PostAsJsonAsync("/api/expenses/masters/CATEGORY",new{id=Guid.NewGuid(),name=" council fees "});var categoryDuplicateBody=await categoryDuplicate.Content.ReadFromJsonAsync<JsonObject>();
   Check(categoryDuplicate.IsSuccessStatusCode&&categoryDuplicateBody!["existing"]!.GetValue<bool>()&&categoryDuplicateBody["id"]!.GetValue<Guid>()==httpCategoryId,"Category HTTP duplicates reuse the existing ID ignoring case and surrounding spaces");
   Check((int)(await client.PostAsJsonAsync("/api/expenses/masters/CATEGORY",new{id=Guid.NewGuid(),name="   "})).StatusCode==400,"Category HTTP API rejects an empty name");
   Check((int)(await client.PostAsJsonAsync("/api/expenses/masters/PAYEE",new{id=Guid.NewGuid(),name="Bad HTTP mobile",mobileNumber="1234567890"})).StatusCode==400,"API rejects invalid Indian mobile numbers");
   Directory.CreateDirectory("tests/bin/expense-modern-fixtures");foreach(var (name,url)in new[]{("Create","/Expenses/Create"),("Details","/Expenses/Details?id="+httpId),("Index","/Expenses"),("Report","/Expenses/Report")})await File.WriteAllTextAsync("tests/bin/expense-modern-fixtures/"+name+".html",await client.GetStringAsync(url));
   await File.WriteAllTextAsync("tests/bin/expense-modern-fixtures/expense.json",(await repo.GetAsync(httpId)).GetRawText());await File.WriteAllTextAsync("tests/bin/expense-modern-fixtures/options.json",(await repo.OptionsAsync()).GetRawText());
   client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Check((int)(await client.PostAsJsonAsync("/api/expenses/masters/PAYEE",new{id=Guid.NewGuid(),name="No CSRF"})).StatusCode==400,"Payee creation requires antiforgery");Check((int)(await client.PostAsJsonAsync($"/api/expenses/{Guid.NewGuid()}/record",Record(1))).StatusCode==400,"Record Expense requires antiforgery");Check(await c.ExecuteScalarAsync<long>("SELECT count(*) FROM inventory_transactions")==stock&&await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents")==gst,"Atomic expense recording creates no inventory or GST documents");Console.WriteLine("Expense modernization database/HTTP checks passed in disposable clone.");
  }finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH(FORCE)");}
 }
}
