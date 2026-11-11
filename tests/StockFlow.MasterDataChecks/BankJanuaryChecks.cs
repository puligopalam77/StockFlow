using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;
internal static class BankJanuaryChecks
{
    static int passed;
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL January bank: "+label);passed++;Console.WriteLine("PASS January bank: "+label);}
    static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or PostgresException){Check(true,label);return;}throw new Exception("Accepted invalid operation: "+label);}
    static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value);
    public static async Task Run()
    {
        var target=JsonNode.Parse(await File.ReadAllTextAsync(".artifacts/bank-january-target.json"))!;
        var database=target["Database"]!.GetValue<string>();
        if(!System.Text.RegularExpressions.Regex.IsMatch(database,"^sf_bank_dev_[a-f0-9]{32}$"))throw new Exception("Invalid local target");
        var cs=$"Host=127.0.0.1;Port=55447;Username=postgres;Database={database}";
        await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
        Check(await c.ExecuteScalarAsync<bool>("SELECT inet_server_addr()='127.0.0.1'::inet AND inet_server_port()=55447 AND current_database()=@database AND current_setting('cluster_name')=@marker",new{database,marker=target["Marker"]!.GetValue<string>()}),"local synthetic target identity verified");
        var settings=new BankAccountsOptions{Enabled=true,PostingEnabled=true,DevelopmentConnection=cs,IdentityKey=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))};
        var options=Options.Create(settings);var protector=new BankAccountProtection(new EphemeralDataProtectionProvider(),options);
        async Task<(long Tenant,int Owner,int Admin)> Business(){
            var t=await c.ExecuteScalarAsync<long>("SELECT max(tenant_id)+1 FROM tenants");var u=await c.ExecuteScalarAsync<int>("SELECT max(user_id)+1 FROM users");
            await c.ExecuteAsync("INSERT INTO tenants VALUES(@t,@code,'Synthetic January business',true); INSERT INTO roles VALUES(@t,1,'Admin',true); INSERT INTO users(tenant_id,user_id,role_id,is_active,username,full_name,password_hash,is_tenant_owner) VALUES(@t,@u,1,true,@code,'Synthetic owner','NOT-A-LOGIN-HASH',true),(@t,@admin,1,true,@adminName,'Synthetic shop admin','NOT-A-LOGIN-HASH',false)",new{t,u,admin=u+1,code="SYNTHETIC-"+Guid.NewGuid(),adminName="SYNTHETIC-ADMIN-"+Guid.NewGuid()});
            for(var shop=1;shop<=4;shop++)await c.ExecuteAsync("INSERT INTO stores(tenant_id,store_id,store_code,store_name) VALUES(@t,@shop,@name,@name);INSERT INTO store_locations(tenant_id,store_id,location_id,location_name) VALUES(@t,@shop,@shop,@name)",new{t,shop,name="Synthetic shop "+shop});
            await c.ExecuteAsync("INSERT INTO user_store_access VALUES(@t,@admin,1,true); INSERT INTO user_action_permissions SELECT @t,u.user_id,a.module_action_id,true FROM users u CROSS JOIN module_actions a JOIN modules m USING(module_id) WHERE u.tenant_id=@t AND m.module_code IN('BANK_ACCOUNTS','SALES','PURCHASES','EXPENSES','SERVICE')",new{t,admin=u+1});
            return(t,u,u+1);
        }
        BankAccountsRepository Repo(long tenant,int user,bool owner=true)=>new(options,new BusinessScope{TenantId=tenant,UserId=user,StoreId=1,LocationId=1,IsOwner=owner,CanReviewStores=true});
        JsonElement Config(long tenant,Guid id,int[]? shops=null){var b=JsonSerializer.SerializeToNode(new{bankName="Synthetic Bank",displayName="January shared account",holderName="Synthetic owner",ifsc="TEST0000001",accountType="CURRENT",accountNumber="90"+RandomNumberGenerator.GetInt32(10000000,99999999),stores=shops??new[]{1,2,3}})!.AsObject();protector.Protect(b,tenant,id);return JsonSerializer.SerializeToElement(b);}
        async Task Grant(long tenant,int user,string action,bool allow)=>await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=@allow WHERE tenant_id=@tenant AND user_id=@user AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='BANK_ACCOUNTS' AND a.action_code=@action)",new{tenant,user,action,allow});
        var b=await Business();var owner=Repo(b.Tenant,b.Owner);var admin=Repo(b.Tenant,b.Admin,false);var account=Guid.NewGuid();var date=new DateTime(2026,11,10);
        await c.ExecuteAsync("UPDATE bank_release_control SET max_active_accounts=1,max_assigned_shops=3");
        var initial=Config(b.Tenant,account);var create=Guid.NewGuid();await owner.WriteAsync("create",account,create,initial);await owner.WriteAsync("create",account,create,initial);
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM bank_accounts WHERE tenant_id=@t",new{t=b.Tenant})==1,"exact create replay preserves one shared account");
        await Reject(()=>owner.WriteAsync("create",Guid.NewGuid(),Guid.NewGuid(),Config(b.Tenant,Guid.NewGuid())),"second active operating account rejected");
        async Task<long> Version(Guid id)=>(await owner.ReadAsync(id,date,1,null)).GetProperty("account").GetProperty("version").GetInt64();
        await Reject(async()=>await owner.WriteAsync("assign",account,Guid.NewGuid(),J(new{stores=new[]{1,2,3,4},expectedVersion=await Version(account)})),"fourth shop assignment rejected by authoritative writer");
        await Reject(async()=>await owner.WriteAsync("assign",account,Guid.NewGuid(),J(new{stores=new[]{1,1,2},expectedVersion=await Version(account)})),"duplicate shop IDs rejected");
        var opening=J(new{amount=100000,trackingStartedAt="2026-11-10T00:00:00+05:30",reference="SYNTHETIC-OPEN",evidence="Synthetic statement balance at tracking start"});var open=Guid.NewGuid();
        await owner.WriteAsync("open",account,open,opening);await owner.WriteAsync("open",account,open,opening);
        await Reject(()=>owner.WriteAsync("open",account,Guid.NewGuid(),opening),"opening initialized once despite request retries");
        async Task<long> Sale(int shop,decimal amount){var sale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(tenant_id,store_id,location_id,sale_id,invoice_number,status,created_by) SELECT @t,@shop,@shop,coalesce(max(sale_id),0)+1,'SYNTHETIC-JANUARY','COMPLETED',@user FROM sales RETURNING sale_id",new{t=b.Tenant,shop,user=b.Owner});return await c.ExecuteScalarAsync<long>("INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date,created_by) SELECT @t,@shop,coalesce(max(payment_id),0)+1,@sale,'UPI',@amount,'2026-11-10'::timestamp,@user FROM payments RETURNING payment_id",new{t=b.Tenant,shop,sale,amount,user=b.Owner});}
        async Task<Guid> Link(string kind,string key)=>(await owner.WriteAsync("link",account,Guid.NewGuid(),J(new{kind,key}))).GetProperty("linkId").GetGuid();
        var p1=await Sale(1,600);var p2=await Sale(1,400);var p3=await Sale(2,2000);var p4=await Sale(1,100);var unlinked=await Sale(1,50);
        await Grant(b.Tenant,b.Owner,"EDIT",false);
        await Reject(()=>owner.WriteAsync("link",account,Guid.NewGuid(),J(new{kind="SALE_PAYMENT",key=p1.ToString()})),"Owner VIEW without EDIT cannot link a payment");
        Check(!(await owner.ReadAsync(account,date,1,null)).GetProperty("canLink").GetBoolean(),"UI capability hides linking when Owner lacks EDIT");
        await Grant(b.Tenant,b.Owner,"EDIT",true);
        await Reject(()=>admin.WriteAsync("link",account,Guid.NewGuid(),J(new{kind="SALE_PAYMENT",key=p1.ToString()})),"Shop Admin with EDIT still cannot perform Owner-only linking");
        var l1=await Link("SALE_PAYMENT",p1.ToString());var l2=await Link("SALE_PAYMENT",p2.ToString());var l3=await Link("SALE_PAYMENT",p3.ToString());var l4=await Link("SALE_PAYMENT",p4.ToString());
        await Reject(()=>owner.WriteAsync("link",account,Guid.NewGuid(),J(new{kind="SALE_PAYMENT",key=p1.ToString()})),"source cannot be linked twice");
        await Reject(()=>owner.WriteAsync("link",account,Guid.NewGuid(),J(new{kind="SALE_PAYMENT",key="999999999"})),"nonexistent source rejected");
        Check((await owner.ReadAsync(account,date,1,null)).GetProperty("recordedBalance").GetDecimal()==100000,"pending attribution contributes zero to recorded balance");
        object Verify(decimal amount,string direction,string reference,object[] allocations)=>new{amount,direction,reference,effectiveDate="2026-11-10",evidence="Synthetic manually checked bank statement",afterOpening=true,allocations};
        var batch=J(Verify(3000,"IN","SYNTHETIC-BATCH-"+account,new object[]{new{linkId=l1,amount=600},new{linkId=l2,amount=400},new{linkId=l3,amount=2000}}));var request=Guid.NewGuid();var posted=await owner.WriteAsync("verify",account,request,batch);await owner.WriteAsync("verify",account,request,batch);
        var all=await owner.ReadAsync(account,date,1,null);var shop1=await owner.ReadAsync(account,date,1,null,1);var shop2=await owner.ReadAsync(account,date,1,null,2);
        Check(all.GetProperty("recordedBalance").GetDecimal()==103000&&all.GetProperty("moneyIn").GetDecimal()==3000&&all.GetProperty("total").GetInt64()==1,"shared event counted once in consolidated balance and totals");
        Check(shop1.GetProperty("moneyIn").GetDecimal()==1000&&shop1.GetProperty("total").GetInt64()==1&&shop2.GetProperty("moneyIn").GetDecimal()==2000,"shop filters aggregate multiple source allocations per movement correctly");
        Check(shop1.GetProperty("recordedBalance").GetDecimal()==103000&&!shop1.GetProperty("showRunningBalance").GetBoolean()&&!shop1.GetProperty("transactions")[0].TryGetProperty("runningBalance",out _),"Owner filtered totals preserve consolidated balance and omit invented shop running balance");
        Check(!shop1.GetProperty("transactions")[0].GetProperty("reversible").GetBoolean(),"filtered allocation cannot misleadingly reverse a full shared settlement");
        var privateView=await admin.ReadAsync(account,date,1,null);
        Check(privateView.GetProperty("moneyIn").GetDecimal()==1000&&privateView.GetProperty("total").GetInt64()==1&&!privateView.TryGetProperty("recordedBalance",out _)&&!privateView.TryGetProperty("opening",out _)&&!privateView.TryGetProperty("filterShops",out _)&&!privateView.GetProperty("canLink").GetBoolean(),"Shop Admin sees own aggregate only; no opening, balance, other-shop filter or linking");
        Check(privateView.GetProperty("transactions")[0].GetProperty("shops").EnumerateArray().All(s=>s.GetProperty("id").GetInt64()==1),"shared batch details contain no other shop identifiers or allocated amounts");
        await Reject(()=>admin.ReadAsync(account,date,1,null,2),"forged admin shop filter rejected");
        await Reject(()=>owner.ReadAsync(account,date,1,null,99),"foreign or never-assigned shop filter rejected");
        await Reject(()=>admin.WriteAsync("reverse",account,Guid.NewGuid(),J(new{movementId=posted.GetProperty("movementId").GetGuid(),effectiveDate="2026-11-11",evidence="Synthetic forbidden shared reversal"})),"admin cannot reverse a shared settlement");
        await Grant(b.Tenant,b.Admin,"VERIFY",false);
        var one=J(Verify(100,"IN","SYNTHETIC-RACE-"+account,new object[]{new{linkId=l4,amount=100}}));
        await Reject(()=>admin.WriteAsync("verify",account,Guid.NewGuid(),one),"explicit VERIFY required independently of Admin role");await Grant(b.Tenant,b.Admin,"VERIFY",true);
        async Task<bool> Try(Func<Task> a){try{await a();return true;}catch(ValidationException){return false;}}
        var races=await Task.WhenAll(Try(()=>admin.WriteAsync("verify",account,Guid.NewGuid(),one)),Try(()=>owner.WriteAsync("verify",account,Guid.NewGuid(),one)));
        Check(races.Count(x=>x)==1,"concurrent verification posts one source allocation exactly once");
        await Reject(()=>owner.WriteAsync("verify",account,Guid.NewGuid(),one),"duplicate posting rejected");
        var bad=JsonSerializer.SerializeToNode(Verify(0.001m,"IN","SYNTHETIC-BAD",new object[]{new{linkId=l4,amount=0.001m}}))!;
        await Reject(()=>owner.WriteAsync("verify",account,Guid.NewGuid(),JsonSerializer.SerializeToElement(bad)),"sub-paise bank amount rejected");bad["amount"]=1;bad["effectiveDate"]="2026-11-09";
        await Reject(()=>owner.WriteAsync("verify",account,Guid.NewGuid(),JsonSerializer.SerializeToElement(bad)),"pre-opening evidence rejected");
        var supplier=await c.ExecuteScalarAsync<long>("INSERT INTO purchases VALUES(@t,1,1,'ELECTRICAL','COMPLETED'); INSERT INTO supplier_payments(tenant_id,location_id,supplier_payment_id,purchase_id,amount,payment_mode,payment_date) SELECT @t,1,coalesce(max(supplier_payment_id),0)+1,1,20000,'BANK','2026-11-10'::timestamptz FROM supplier_payments RETURNING supplier_payment_id",new{t=b.Tenant});
        var expense=Guid.NewGuid();await c.ExecuteAsync("INSERT INTO expenses(tenant_id,expense_id,description,store_id,status) VALUES(@t,@expense,'Synthetic January expense',1,'POSTED'); INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,payment_id) SELECT @t,1,@expense,coalesce(max(movement_id),0)+1,3000,'BANK_TRANSFER','2026-11-10',@expense FROM expense_payments",new{t=b.Tenant,expense});
        var supplierLink=await Link("SUPPLIER_PAYMENT",supplier.ToString());var expenseLink=await Link("EXPENSE_PAYMENT",expense.ToString());
        var original=await c.ExecuteScalarAsync<string>("SELECT jsonb_build_array((SELECT jsonb_agg(to_jsonb(p)) FROM payments p WHERE tenant_id=@t),(SELECT jsonb_agg(to_jsonb(p)) FROM supplier_payments p WHERE tenant_id=@t),(SELECT jsonb_agg(to_jsonb(p)) FROM expense_payments p WHERE tenant_id=@t))::text",new{t=b.Tenant});
        await owner.WriteAsync("verify",account,Guid.NewGuid(),J(Verify(20000,"OUT","SYNTHETIC-SUP-"+account,new object[]{new{linkId=supplierLink,amount=20000}})));
        var expensePosted=await owner.WriteAsync("verify",account,Guid.NewGuid(),J(Verify(3000,"OUT","SYNTHETIC-EXP-"+account,new object[]{new{linkId=expenseLink,amount=3000}})));
        Check((await owner.ReadAsync(account,date,1,null)).GetProperty("recordedBalance").GetDecimal()==80100,"100000 opening +3100 received -23000 paid equals 80100");
        var reversal=J(new{movementId=expensePosted.GetProperty("movementId").GetGuid(),effectiveDate="2026-11-11",evidence="Synthetic erroneous verification correction"});
        await owner.WriteAsync("reverse",account,Guid.NewGuid(),reversal);await Reject(()=>owner.WriteAsync("reverse",account,Guid.NewGuid(),reversal),"duplicate reversal rejected");
        Check((await owner.ReadAsync(account,date.AddDays(1),1,null)).GetProperty("recordedBalance").GetDecimal()==83100,"opposite reversal preserves original and carries 83100 forward");
        Check(original==await c.ExecuteScalarAsync<string>("SELECT jsonb_build_array((SELECT jsonb_agg(to_jsonb(p)) FROM payments p WHERE tenant_id=@t),(SELECT jsonb_agg(to_jsonb(p)) FROM supplier_payments p WHERE tenant_id=@t),(SELECT jsonb_agg(to_jsonb(p)) FROM expense_payments p WHERE tenant_id=@t))::text",new{t=b.Tenant}),"bank verification and reversal preserve Sales, supplier and Expense source rows");
        var other=await Business();var otherRepo=Repo(other.Tenant,other.Owner);var otherAccount=Guid.NewGuid();await otherRepo.WriteAsync("create",otherAccount,Guid.NewGuid(),Config(other.Tenant,otherAccount));
        await Reject(()=>otherRepo.ReadAsync(account,date,1,null),"another tenant Owner cannot access the shared account");
        var clash=await Business();var competing=Repo(clash.Tenant,clash.Owner);var ids=new[]{Guid.NewGuid(),Guid.NewGuid()};
        var creates=await Task.WhenAll(ids.Select(id=>Try(()=>competing.WriteAsync("create",id,Guid.NewGuid(),Config(clash.Tenant,id)))));
        Check(creates.Count(x=>x)==1,"concurrent account creation enforces one active account per business");
        var version=await Version(account);var assigns=await Task.WhenAll(Try(()=>owner.WriteAsync("assign",account,Guid.NewGuid(),J(new{stores=new[]{1,2,3},expectedVersion=version}))),Try(()=>owner.WriteAsync("assign",account,Guid.NewGuid(),J(new{stores=new[]{1,2,4},expectedVersion=version}))));
        Check(assigns.Count(x=>x)==1&&await c.ExecuteScalarAsync<int>("SELECT count(*) FROM bank_account_shops WHERE tenant_id=@t AND account_id=@account AND is_active",new{t=b.Tenant,account})==3,"concurrent assignments preserve three-shop cap and reject stale versions");
        await owner.WriteAsync("assign",account,Guid.NewGuid(),J(new{stores=new[]{1,2,3},expectedVersion=await Version(account)}));
        await c.ExecuteAsync("UPDATE bank_release_control SET max_active_accounts=2,max_assigned_shops=4");
        var backup=Guid.NewGuid();await owner.WriteAsync("create",backup,Guid.NewGuid(),Config(b.Tenant,backup,new[]{1,2,3,4}));
        Check((await owner.ReadAsync(backup,date,1,null)).GetProperty("account").GetProperty("stores").GetArrayLength()==4,"configuration expands to two accounts and four shops without relationship restructuring");
        var linkedBefore=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(l) ORDER BY link_id)::text FROM bank_payment_links l WHERE tenant_id=@t",new{t=b.Tenant});
        await owner.WriteAsync("default",account,Guid.NewGuid(),J(new{storeId=1,expectedVersion=await Version(account)}));await owner.WriteAsync("default",backup,Guid.NewGuid(),J(new{storeId=1,expectedVersion=await Version(backup)}));
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM bank_account_shops WHERE tenant_id=@t AND store_id=1 AND is_default_receiving",new{t=b.Tenant})==1&&linkedBefore==await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(l) ORDER BY link_id)::text FROM bank_payment_links l WHERE tenant_id=@t",new{t=b.Tenant}),"default changes preserve historical destinations and keep one receiving default per shop");
        await owner.WriteAsync("status",backup,Guid.NewGuid(),J(new{active=false,expectedVersion=await Version(backup),reason="Synthetic retired account"}));
        await c.ExecuteAsync("UPDATE bank_release_control SET max_active_accounts=1,max_assigned_shops=3");
        await Reject(async()=>await owner.WriteAsync("status",backup,Guid.NewGuid(),J(new{active=true,expectedVersion=await Version(backup)})),"reactivation cannot bypass active-account limit");
        Check((await owner.ReadAsync(null,date,1,null)).GetProperty("accounts").GetArrayLength()==2,"inactive account and all prior history remain available to Owner");
        await owner.WriteAsync("status",account,Guid.NewGuid(),J(new{active=false,expectedVersion=await Version(account),reason="Synthetic reactivation race"}));
        await Reject(async()=>await owner.WriteAsync("status",backup,Guid.NewGuid(),J(new{active=true,expectedVersion=await Version(backup)})),"reactivation cannot bypass lowered shop limit");
        await owner.WriteAsync("assign",backup,Guid.NewGuid(),J(new{stores=new[]{1,2,3},expectedVersion=await Version(backup)}));
        var va=await Version(account);var vb=await Version(backup);var reactivate=await Task.WhenAll(Try(()=>owner.WriteAsync("status",account,Guid.NewGuid(),J(new{active=true,expectedVersion=va}))),Try(()=>owner.WriteAsync("status",backup,Guid.NewGuid(),J(new{active=true,expectedVersion=vb}))));
        Check(reactivate.Count(x=>x)==1,"concurrent reactivations cannot exceed active-account policy");
        // Restore the account with financial history for API/browser fixtures, without deleting anything.
        var main=await owner.ReadAsync(account,date,1,null);if(!main.GetProperty("account").GetProperty("active").GetBoolean()){
            await owner.WriteAsync("status",backup,Guid.NewGuid(),J(new{active=false,expectedVersion=await Version(backup),reason="Synthetic final fixture"}));await owner.WriteAsync("status",account,Guid.NewGuid(),J(new{active=true,expectedVersion=await Version(account)}));}
        Check((await owner.ReadAsync(account,date.AddDays(1),1,null)).GetProperty("recordedBalance").GetDecimal()==83100,"retirement and reactivation never rewrite financial history");
        Check(!await c.ExecuteScalarAsync<bool>("SELECT has_table_privilege('stockflow_runtime','bank_release_control','UPDATE')"),"runtime cannot change trusted policy limits");
        await Http(settings,b.Tenant,b.Owner,account,owner,date,unlinked);
        Console.WriteLine("January bank checks passed: "+passed+"; synthetic local database only.");
    }
    static async Task Http(BankAccountsOptions settings,long tenant,int user,Guid account,BankAccountsRepository owner,DateTime date,long source)
    {
        using var factory=new Factory();using var web=factory.WithWebHostBuilder(builder=>builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"BankAccounts:Enabled","true"},{"BankAccounts:PostingEnabled","true"},{"BankAccounts:DevelopmentConnection",settings.DevelopmentConnection},{"BankAccounts:IdentityKey",settings.IdentityKey}})));
        using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");client.DefaultRequestHeaders.Add("Test-Tenant",tenant.ToString());client.DefaultRequestHeaders.Add("Test-UserId",user.ToString());
        var html=await client.GetStringAsync("/Settings/BankAccounts/"+account);Check(html.Contains("bank-shop"),"actual Razor page includes Owner shop filter");
        var response=await client.GetFromJsonAsync<JsonElement>("/api/bankaccounts/"+account+"?date=2026-11-10&shopId=2");Check(response.GetProperty("moneyIn").GetDecimal()==2000,"HTTP shop filter reaches scoped SQL read");
        var body=new BankOperation{RequestId=Guid.NewGuid(),Body=J(new{kind="SALE_PAYMENT",key=source.ToString()})};
        Check((await client.PostAsJsonAsync("/api/bankaccounts/"+account+"/link",body)).StatusCode==HttpStatusCode.BadRequest,"link POST requires anti-forgery token");
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());client.DefaultRequestHeaders.Add("Test-Bank-ViewOnly","true");
        Check((await client.PostAsJsonAsync("/api/bankaccounts/"+account+"/link",body)).StatusCode==HttpStatusCode.Forbidden,"HTTP VIEW-only principal cannot link payments");client.DefaultRequestHeaders.Remove("Test-Bank-ViewOnly");
        Check((await client.PostAsJsonAsync("/api/bankaccounts/"+account+"/link",body)).StatusCode==HttpStatusCode.OK,"HTTP Owner with EDIT can link without creating a bank movement");
        var output="tests/bin/bank-accounts-fixtures";Directory.CreateDirectory(output);await File.WriteAllTextAsync(output+"/Index.html",html);await File.WriteAllTextAsync(output+"/detail.json",(await owner.ReadAsync(account,date,1,null)).GetRawText());await File.WriteAllTextAsync(output+"/list.json",(await owner.ReadAsync(null,date,1,null)).GetRawText());
    }
}
