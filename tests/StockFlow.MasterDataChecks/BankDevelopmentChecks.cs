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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Security;
namespace StockFlow.MasterDataChecks;

internal static class BankDevelopmentChecks
{
    static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL bank: "+label);Console.WriteLine("PASS bank: "+label);}
    static async Task Reject(Func<Task> operation,string label){try{await operation();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or PostgresException){Check(true,label);return;}throw new Exception("Accepted invalid bank operation: "+label);}
    public static async Task Run()
    {
        // Never reads appsettings, connection-string environment variables or live data.
        var target=JsonNode.Parse(await File.ReadAllTextAsync(".artifacts/bank-development-target.json"))!;
        var database=target["Database"]!.GetValue<string>();
        if(!System.Text.RegularExpressions.Regex.IsMatch(database,"^sf_bank_dev_[a-f0-9]{32}$"))throw new Exception("Unrecognized local test database");
        var cs=$"Host=127.0.0.1;Port=55447;Username=postgres;Database={database}";
        await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
        Check(await c.ExecuteScalarAsync<bool>("SELECT inet_server_addr()='127.0.0.1'::inet AND inet_server_port()=55447 AND current_setting('cluster_name')=@marker",new{marker=target["Marker"]!.GetValue<string>()}),"explicit local database/cluster identity verified");
        var keyFile=".artifacts/bank-development-identity.key";
        if(!File.Exists(keyFile))await File.WriteAllTextAsync(keyFile,Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var settings=new BankAccountsOptions{Enabled=true,PostingEnabled=true,DevelopmentConnection=cs,IdentityKey=(await File.ReadAllTextAsync(keyFile)).Trim()};
        var options=Options.Create(settings);var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,CanReviewStores=true};
        var repo=new BankAccountsRepository(options,scope);var protection=new BankAccountProtection(new EphemeralDataProtectionProvider(),options);
        var acct=Guid.NewGuid();var other=Guid.NewGuid();var date=new DateTime(2026,11,10);
        JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
        JsonElement Config(Guid id,string name){var body=JsonSerializer.SerializeToNode(new{bankName="Synthetic Bank",displayName=name,holderName="Synthetic Business",accountNumber="91"+RandomNumberGenerator.GetInt32(10000000,99999999),accountType="CURRENT",ifsc="TEST0000001",stores=new[]{1,2}})!.AsObject();protection.Protect(body,1,id);Check(!body.ContainsKey("accountNumber")&&!body.ContainsKey("upiId"),"account plaintext removed before repository boundary");return JsonSerializer.SerializeToElement(body);}
        var config=Config(acct,"Synthetic Bank "+acct.ToString()[..8]);var createId=Guid.NewGuid();
        await repo.WriteAsync("create",acct,createId,config);await repo.WriteAsync("create",acct,createId,config);
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM bank_accounts WHERE account_id=@acct",new{acct})==1,"create retry creates one shared account and audit");
        await Reject(()=>repo.WriteAsync("create",other,Guid.NewGuid(),config),"duplicate protected account fingerprint rejected atomically");
        var opening=Json(new{amount=100000,trackingStartedAt="2026-11-10T00:00:00+05:30",evidence="Synthetic verified opening statement",reference="SYNTHETIC-OPEN-"+acct});
        var openId=Guid.NewGuid();await repo.WriteAsync("open",acct,openId,opening);await repo.WriteAsync("open",acct,openId,opening);
        await Reject(()=>repo.WriteAsync("open",acct,Guid.NewGuid(),opening),"duplicate opening initialization rejected");
        Check((await repo.ReadAsync(acct,date,1,null)).GetProperty("recordedBalance").GetDecimal()==100000,"opening included once and carries as a recorded balance");
        // Create fresh synthetic source rows so test reruns never reattribute old rows.
        var receipt=await c.ExecuteScalarAsync<long>("INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date,reference_number,created_by) SELECT 1,1,coalesce(max(payment_id),0)+1,1,'UPI',5000,'2026-11-10'::timestamp,'SYNTHETIC-NEW',1 FROM payments RETURNING payment_id");
        var supplier=await c.ExecuteScalarAsync<long>("INSERT INTO supplier_payments(tenant_id,location_id,supplier_payment_id,purchase_id,payment_mode,amount,payment_date,notes) SELECT 1,1,coalesce(max(supplier_payment_id),0)+1,1,'BANK',20000,'2026-11-10'::timestamptz,'Synthetic new supplier' FROM supplier_payments RETURNING supplier_payment_id");
        var expense=Guid.NewGuid();await c.ExecuteAsync("INSERT INTO expense_payments(tenant_id,location_id,expense_id,movement_id,amount,payment_mode,paid_date,reference,payment_id) SELECT 1,1,md5('bank-expense')::uuid,coalesce(max(movement_id),0)+1,3000,'BANK_TRANSFER','2026-11-10','SYNTHETIC-NEW-EXP',@expense FROM expense_payments",new{expense});
        var before=await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(p) ORDER BY payment_id)::text FROM payments p");
        var links=new List<Guid>();
        foreach(var source in new[]{("SALE_PAYMENT",receipt.ToString()),("SUPPLIER_PAYMENT",supplier.ToString()),("EXPENSE_PAYMENT",expense.ToString())}){
            var linked=await repo.WriteAsync("link",acct,Guid.NewGuid(),Json(new{kind=source.Item1,key=source.Item2}));links.Add(linked.GetProperty("linkId").GetGuid());
        }
        Check((await repo.ReadAsync(acct,date,1,null)).GetProperty("recordedBalance").GetDecimal()==100000,"linking UPI/supplier/expense records leaves settlement pending without bank movement");
        await Reject(()=>repo.WriteAsync("link",acct,Guid.NewGuid(),Json(new{kind="SALE_PAYMENT",key="999999999"})),"nonexistent source rejected");
        object Verify(Guid link,decimal amount,string direction,string reference)=>new{amount,direction,reference,effectiveDate="2026-11-10",evidence="Synthetic actual bank statement evidence",afterOpening=true,allocations=new[]{new{linkId=link,amount}}};
        var verifyId=Guid.NewGuid();var verifyBody=Json(Verify(links[0],5000,"IN","SYNTHETIC-IN-"+acct));
        var posted=await repo.WriteAsync("verify",acct,verifyId,verifyBody);await repo.WriteAsync("verify",acct,verifyId,verifyBody);
        await Reject(()=>repo.WriteAsync("verify",acct,Guid.NewGuid(),verifyBody),"already allocated bank receipt cannot post twice");
        await Reject(()=>repo.WriteAsync("verify",acct,Guid.NewGuid(),Json(Verify(links[1],20001,"OUT","SYNTHETIC-OVER-"+acct))),"source over-allocation rejected");
        await repo.WriteAsync("verify",acct,Guid.NewGuid(),Json(Verify(links[1],20000,"OUT","SYNTHETIC-SUP-"+acct)));
        var expenseResult=await repo.WriteAsync("verify",acct,Guid.NewGuid(),Json(Verify(links[2],3000,"OUT","SYNTHETIC-EXP-"+acct)));
        var report=await repo.ReadAsync(acct,date,1,null);
        Check(report.GetProperty("recordedBalance").GetDecimal()==82000&&report.GetProperty("moneyIn").GetDecimal()==5000&&report.GetProperty("moneyOut").GetDecimal()==23000,"source-backed 100000 +5000 -20000 -3000 =82000; daily totals exclude opening");
        Check((await repo.ReadAsync(acct,date.AddDays(1),1,null)).GetProperty("recordedBalance").GetDecimal()==82000,"next-day empty history carries account balance forward");
        var adminScope=new BusinessScope{TenantId=1,UserId=2,StoreId=1,LocationId=1,CanReviewStores=true};var admin=new BankAccountsRepository(options,adminScope);
        var adminJson=(await admin.ReadAsync(acct,date,1,null)).GetRawText();
        Check(!adminJson.Contains("recordedBalance")&&!adminJson.Contains("runningBalance")&&!adminJson.Contains("trackingStartedAt")&&!adminJson.Contains("\"stores\""),"admin API shape omits account-wide balance, running balances, opening and other-shop assignments");
        await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=false WHERE tenant_id=1 AND user_id=2 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='BANK_ACCOUNTS' AND a.action_code='VERIFY')");
        await Reject(()=>admin.WriteAsync("verify",acct,Guid.NewGuid(),verifyBody),"admin role alone cannot verify without explicit VERIFY grant");
        await c.ExecuteAsync("UPDATE user_action_permissions SET is_allowed=true WHERE tenant_id=1 AND user_id=2 AND module_action_id IN(SELECT a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='BANK_ACCOUNTS' AND a.action_code='VERIFY')");
        var reversal=Json(new{movementId=expenseResult.GetProperty("movementId").GetGuid(),effectiveDate="2026-11-11",evidence="Synthetic erroneous expense verification correction"});
        await repo.WriteAsync("reverse",acct,Guid.NewGuid(),reversal);
        Check((await repo.ReadAsync(acct,date.AddDays(1),1,null)).GetProperty("recordedBalance").GetDecimal()==85000,"server-derived full opposite reversal preserves original and restores allocations");
        await Reject(()=>repo.WriteAsync("reverse",acct,Guid.NewGuid(),reversal),"second reversal rejected");
        Check(before==await c.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(p) ORDER BY payment_id)::text FROM payments p"),"bank attribution/verification/reversal never rewrites original sales payments");
        await Reject(()=>repo.WriteAsync("verify",acct,verifyId,Json(Verify(links[0],4999,"IN","SYNTHETIC-CONFLICT"))),"request UUID cannot be reused with changed financial payload");
        await Reject(()=>repo.WriteAsync("verify",acct,Guid.NewGuid(),Json(Verify(links[1],0.001m,"OUT","SYNTHETIC-PRECISION"))),"sub-paise verification rejected before numeric rounding");
        var boundary=JsonSerializer.SerializeToNode(Verify(links[1],1,"OUT","SYNTHETIC-BOUNDARY"))!;boundary["effectiveDate"]="2026-11-09";
        await Reject(()=>repo.WriteAsync("verify",acct,Guid.NewGuid(),JsonSerializer.SerializeToElement(boundary)),"pre-opening bank-effective movement rejected");
        boundary["effectiveDate"]="2026-11-10";boundary["afterOpening"]=false;
        await Reject(()=>repo.WriteAsync("verify",acct,Guid.NewGuid(),JsonSerializer.SerializeToElement(boundary)),"same-day evidence requires explicit after-opening declaration");
        var shared=Guid.NewGuid();await repo.WriteAsync("create",shared,Guid.NewGuid(),Config(shared,"Synthetic shared privacy"));
        await repo.WriteAsync("open",shared,Guid.NewGuid(),opening);
        var foreignSale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(tenant_id,store_id,location_id,sale_id,invoice_number,status,created_by) SELECT 1,2,2,coalesce(max(sale_id),0)+1,'SYNTHETIC-PRIVATE-SHOP','COMPLETED',1 FROM sales RETURNING sale_id");
        async Task<Guid> FreshLink(int location,long sale,int amount){var payment=await c.ExecuteScalarAsync<long>("INSERT INTO payments(tenant_id,location_id,payment_id,sale_id,payment_mode,amount,payment_date,created_by) SELECT 1,@location,coalesce(max(payment_id),0)+1,@sale,'UPI',@amount,'2026-11-10'::timestamp,1 FROM payments RETURNING payment_id",new{location,sale,amount});return (await repo.WriteAsync("link",shared,Guid.NewGuid(),Json(new{kind="SALE_PAYMENT",key=payment.ToString()}))).GetProperty("linkId").GetGuid();}
        var localLink=await FreshLink(1,1,1000);var foreignLink=await FreshLink(2,foreignSale,2000);
        var batch=Json(new{amount=3000,direction="IN",reference="SYNTHETIC-SHARED-"+shared,effectiveDate="2026-11-10",evidence="Synthetic batch evidence across two shops",afterOpening=true,allocations=new[]{new{linkId=localLink,amount=1000},new{linkId=foreignLink,amount=2000}}});
        var batchPosted=await repo.WriteAsync("verify",shared,Guid.NewGuid(),batch);
        var privateView=await admin.ReadAsync(shared,date,1,null);
        Check(privateView.GetProperty("moneyIn").GetDecimal()==1000&&privateView.GetProperty("transactions").GetArrayLength()==1&&privateView.GetProperty("transactions")[0].GetProperty("amount").GetDecimal()==1000,"shared 3000 bank event exposes only authorized shop allocation of 1000");
        await Reject(()=>admin.WriteAsync("reverse",shared,Guid.NewGuid(),Json(new{movementId=batchPosted.GetProperty("movementId").GetGuid(),effectiveDate="2026-11-11",evidence="Synthetic unauthorized shared reversal"})),"shop admin cannot reverse whole-account shared batch");
        var ownLink=await FreshLink(1,1,1000);var ownBody=Json(Verify(ownLink,1000,"IN","SYNTHETIC-ADMIN-"+shared));
        await admin.WriteAsync("verify",shared,Guid.NewGuid(),ownBody);
        Check((await admin.ReadAsync(shared,date,1,null)).GetProperty("moneyIn").GetDecimal()==2000,"explicitly permitted shop admin can verify own source without receiving account balance");
        var raceLink=await FreshLink(1,1,1000);var raceBody=Json(Verify(raceLink,1000,"IN","SYNTHETIC-RACE-"+shared));
        async Task<bool> Compete(){try{await repo.WriteAsync("verify",shared,Guid.NewGuid(),raceBody);return true;}catch(ValidationException){return false;}}
        var races=await Task.WhenAll(Compete(),Compete());Check(races.Count(x=>x)==1,"concurrent verifications serialize and post exactly one source allocation");
        var tenant=new BankAccountsRepository(options,new BusinessScope{TenantId=2,UserId=4,StoreId=3,LocationId=3,IsOwner=true,CanReviewStores=true});
        await Reject(()=>tenant.ReadAsync(shared,date,1,null),"other tenant owner cannot access bank account without authorized tenant grants");
        Check(await c.ExecuteScalarAsync<bool>("SELECT NOT EXISTS(SELECT 1 FROM bank_account_events WHERE redacted_changes::text LIKE '%protectedNumber%' OR redacted_changes::text LIKE '%protectedUpi%')"),"configuration audit contains no ciphertext or full account number fields");
        foreach(var table in new[]{"bank_accounts","bank_account_secrets","bank_requests","bank_release_control","bank_movements"}){
            await using var denied=await c.BeginTransactionAsync();
            await c.ExecuteAsync("SET LOCAL ROLE stockflow_runtime",transaction:denied);
            try{await c.ExecuteScalarAsync<int>("SELECT count(*) FROM "+table,transaction:denied);throw new Exception("Runtime directly read "+table);}catch(PostgresException e)when(e.SqlState=="42501"){Check(true,"runtime direct access denied: "+table);}finally{await denied.RollbackAsync();}
        }
        var detail=await repo.ReadAsync(acct,date,1,null);var v=detail.GetProperty("account").GetProperty("version").GetInt64();
        await repo.WriteAsync("default",acct,Guid.NewGuid(),Json(new{storeId=1,expectedVersion=v}));
        detail=await repo.ReadAsync(acct,date,1,null);v=detail.GetProperty("account").GetProperty("version").GetInt64();
        await repo.WriteAsync("status",acct,Guid.NewGuid(),Json(new{active=false,expectedVersion=v,reason="Synthetic closure"}));
        Check(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM bank_account_shops WHERE tenant_id=1 AND account_id=@acct AND is_default_receiving)",new{acct}),"deactivation atomically clears defaults");
        await Reject(()=>repo.WriteAsync("link",acct,Guid.NewGuid(),Json(new{kind="SALE_PAYMENT",key=receipt.ToString()})),"inactive account rejects new attribution");
        var disabled=new BankAccountsRepository(Options.Create(new BankAccountsOptions()),scope);
        await Reject(()=>disabled.ReadAsync(acct,date,1,null),"feature off refuses access before any database connection");
        await Reject(()=>new BankAccountsRepository(Options.Create(new BankAccountsOptions{Enabled=true,DevelopmentConnection="Host=example.invalid;Database=StockFlow_DB"}),scope).ReadAsync(null,date,1,null),"non-local target rejected before connecting");
        await HttpAndFixtures(settings,acct,repo,date);
    }
    static async Task HttpAndFixtures(BankAccountsOptions settings,Guid acct,IBankAccountsRepository repo,DateTime date)
    {
        using var factory=new Factory();using var web=factory.WithWebHostBuilder(builder=>{
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"BankAccounts:Enabled","true"},{"BankAccounts:PostingEnabled","true"},{"BankAccounts:DevelopmentConnection",settings.DevelopmentConnection},{"BankAccounts:IdentityKey",settings.IdentityKey}}));
            builder.ConfigureServices(services=>{services.RemoveAll<IBankAccountsRepository>();services.AddScoped<IBankAccountsRepository,BankAccountsRepository>();});
        });
        using var client=web.CreateClient(new(){AllowAutoRedirect=false});client.DefaultRequestHeaders.Add("Test-Role","Admin");
        var html=await client.GetStringAsync("/Settings/BankAccounts/"+acct);var output="tests/bin/bank-accounts-fixtures";Directory.CreateDirectory(output);await File.WriteAllTextAsync(output+"/Index.html",html);
        Check(html.Contains("bank-workspace")&&html.Contains("Business Setup"),"actual Razor bank page and navigation render");
        var response=await client.GetAsync("/api/bankaccounts/"+acct+"?date=2026-11-10");Check(response.StatusCode==HttpStatusCode.OK,"authenticated HTTP read uses isolated repository");
        var noToken=await client.PostAsJsonAsync("/api/bankaccounts/"+acct+"/verify",new BankOperation{Body=JsonSerializer.SerializeToElement(new{})});
        Check(noToken.StatusCode==HttpStatusCode.BadRequest,"bank write requires anti-forgery token: "+noToken.StatusCode);
        var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var current=await repo.ReadAsync(acct,date,1,null);
        var activated=await client.PostAsJsonAsync("/api/bankaccounts/"+acct+"/status",new BankOperation{RequestId=Guid.NewGuid(),Body=JsonSerializer.SerializeToElement(new{active=true,expectedVersion=current.GetProperty("account").GetProperty("version").GetInt64()})});
        Check(activated.StatusCode==HttpStatusCode.OK,"authenticated anti-forgery protected HTTP command reaches bank writer: "+await activated.Content.ReadAsStringAsync());
        await File.WriteAllTextAsync(output+"/detail.json",(await repo.ReadAsync(acct,date,1,null)).GetRawText());
        await File.WriteAllTextAsync(output+"/list.json",(await repo.ReadAsync(null,date,1,null)).GetRawText());
        client.DefaultRequestHeaders.Add("Test-Deny","BANK_ACCOUNTS");Check((await client.GetAsync("/api/bankaccounts")).StatusCode==HttpStatusCode.Forbidden,"HTTP permission middleware denies unauthorized bank access");
        using var off=new Factory();using var offClient=off.CreateClient();offClient.DefaultRequestHeaders.Add("Test-Role","Admin");var offHtml=await offClient.GetStringAsync("/Inventory");Check(!offHtml.Contains("href=\"/Settings/BankAccounts\""),"default-off application does not add bank navigation");
    }
}
