using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Notifications;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

static class NotificationDatabaseChecks
{
    public static async Task Run()
    {
        // ONLY the isolated test server; never the configured shop database.
        var db="notifications_test_"+Guid.NewGuid().ToString("N");
        var cs=$"Host=127.0.0.1;Port=55441;Database={db};Username=postgres";
        await using var admin=new NpgsqlConnection(cs.Replace($"Database={db}","Database=postgres"));
        await admin.OpenAsync(); await admin.ExecuteAsync("CREATE DATABASE "+db);
        try
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
            await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
            foreach(var f in Directory.GetFiles(Path.Combine(root!.FullName,"database/scripts"),"*.sql").Where(f=>!Path.GetFileName(f).StartsWith("001")&&!Path.GetFileName(f).StartsWith("006")).Order()) await c.ExecuteAsync(await File.ReadAllTextAsync(f));
            foreach(var f in Directory.GetFiles(Path.Combine(root.FullName,"database/migrations"),"*.sql").Order())
            {
                if(Path.GetFileName(f).StartsWith("020"))await c.ExecuteAsync("INSERT INTO roles(role_name) VALUES('Admin'); INSERT INTO users(role_id,full_name,username,password_hash) VALUES(1,'Owner','owner','unused'); INSERT INTO categories(category_name) VALUES('General'); INSERT INTO products(category_id,product_name,sku,selling_price) VALUES(1,'Shirt','SHIRT',100);");
                if(Path.GetFileName(f).StartsWith("045"))
                    await c.ExecuteAsync("UPDATE users SET require_login_email_otp=true; INSERT INTO login_email_challenges(id,tenant_id,user_id,security_version,code_hash) SELECT gen_random_uuid(),tenant_id,user_id,security_version,repeat('a',64) FROM users;");
                await c.ExecuteAsync(await File.ReadAllTextAsync(f));
                if(Path.GetFileName(f).StartsWith("045"))
                {
                    if(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE require_login_email_otp) OR EXISTS(SELECT 1 FROM login_email_challenges WHERE consumed_at IS NULL) OR sf_login_email_user_required(1,1) OR has_function_privilege('stockflow_notification_worker','sf_login_email_begin(bigint,integer,bigint,uuid,text)','EXECUTE')"))
                        throw new Exception("Retired email OTP must be disabled and pending challenges consumed.");
                    Console.WriteLine("PASS: migration 045 disables email OTP, consumes pending codes and revokes worker sign-in access.");
                }
            }
            await c.ExecuteAsync("""
                SELECT set_config('app.tenant_id','1',false);SELECT set_config('app.user_id','1',false);SELECT set_config('app.location_id','1',false);
                INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,1,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE a.is_active AND m.is_active ON CONFLICT DO NOTHING;
                INSERT INTO users(tenant_id,role_id,full_name,username,password_hash) VALUES(1,1,'No permissions','staff','unused');
                INSERT INTO customers(customer_name,mobile_number) VALUES('Test customer','9876543210');
                """);
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs}}).Build();
            NotificationRepository Repo(long tenant=1,int actor=1)=>new(new DbConnectionFactory(config,new BusinessScope{TenantId=tenant,UserId=actor,StoreId=1,LocationId=1}));
            var repo=Repo(); Check(await repo.ReadyAsync(),"schema ready");
            Check(!(await repo.SettingsAsync()).Enabled,"disabled initially");
            var settings=new NotificationSettings{Enabled=true,RecipientIds=[1,2]};await repo.SaveSettingsAsync(settings);
            Check((await repo.SettingsAsync()).RecipientIds.Length==2,"settings round trip");
            await Reject(()=>repo.SaveSettingsAsync(new(){Enabled=true,RecipientIds=[999]}),"invalid recipient rejected");
            await Reject(()=>Repo(1,2).SaveSettingsAsync(settings),"settings require edit permission");
            await repo.SaveConsentAsync(new(){CustomerId=1,Channel="email",Allowed=true,Evidence="Written agreement, test"});
            await repo.SaveConsentAsync(new(){CustomerId=1,Channel="email",Allowed=false,Evidence="Customer withdrew, test"});
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_consent_history")==2,"consent changes audited");
            Check(!(await repo.CustomersAsync("Test")).Single(x=>x.Channel=="email").Allowed,"withdrawal retained");
            var sale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-TEST',1,'COMPLETED') RETURNING sale_id");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox")==1,"completed invoice captured");
            await c.ExecuteAsync("UPDATE sales SET notes='changed' WHERE sale_id=@sale",new{sale});
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox")==1,"ordinary edits do not duplicate event");
            await using(var tx=await c.BeginTransactionAsync())
            {
                await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-ROLLBACK',1,'COMPLETED')",transaction:tx);
                await tx.RollbackAsync();
            }
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox")==1,"business rollback removes event");
            var workerCs=new NpgsqlConnectionStringBuilder(cs){Options="-c role=stockflow_notification_worker"}.ConnectionString;
            async Task Process() { await using var w=new NpgsqlConnection(workerCs);await w.ExecuteScalarAsync<int>("SELECT sf_notification_process(25)"); }
            await Task.WhenAll(Process(),Process());await Process();
            var feed=await repo.FeedAsync(1);Check(feed.Total==1&&feed.Unread==1,"concurrent workers deliver once");
            Check(feed.Items[0].Url=="/Sales/Details/"+sale,"invoice link matches existing route");
            Check((await Repo(1,2).FeedAsync(1)).Total==0,"recipient module permission enforced");
            Check((await Repo(2,1).FeedAsync(1)).Total==0,"cross-shop feed hidden");
            await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='SALES' AND a.action_code='VIEW'");
            await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-LOCATION',1,'COMPLETED')");await Process();
            Check((await Repo(1,2).FeedAsync(1)).Total==0,"module access alone does not grant store alerts");
            await Repo(1,2).MarkReadAsync(feed.Items[0].Id);Check((await repo.FeedAsync(1)).Unread==2,"another user cannot mark read");
            await repo.MarkReadAsync(null);Check((await repo.FeedAsync(1)).Unread==0,"mark read persists");
            await using(var runtime=new DbConnectionFactory(config,new BusinessScope{TenantId=1,UserId=1,LocationId=1}).CreateConnection())
                await Reject(()=>runtime.ExecuteAsync("SELECT sf_notification_process(25)"),"runtime cannot invoke global worker");
            await c.ExecuteAsync("DELETE FROM user_action_permissions p USING module_actions a,modules m WHERE p.module_action_id=a.module_action_id AND a.module_id=m.module_id AND p.user_id=1 AND m.module_code='SALES'");
            Check((await repo.FeedAsync(1)).Total==0,"permission revocation hides existing alert");
            await c.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,1,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='SALES' AND a.is_active AND m.is_active ON CONFLICT DO NOTHING");
            // Force an isolated delivery failure to exercise transactional retry/backoff, not an external provider.
            await c.ExecuteAsync("""
                CREATE FUNCTION test_delivery_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test'; END $$;
                CREATE TRIGGER test_delivery_failure BEFORE INSERT ON notifications FOR EACH ROW EXECUTE FUNCTION test_delivery_failure();
                INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-RETRY',1,'COMPLETED');
                """);
            for(var i=1;i<=5;i++)
            {
                await Process();
                var job=(await repo.JobsAsync()).First();Check(job.Attempts==i&&job.Status==(i==5?"FAILED":"RETRY"),"bounded retry "+i);
                await Process();Check((await repo.JobsAsync()).First().Attempts==i,"backoff prevents immediate retry "+i);
                await c.ExecuteAsync("UPDATE notification_outbox SET next_attempt_at=now() WHERE status='RETRY'");
            }
            Check((await repo.FeedAsync(1)).Total==2,"failed partial deliveries rolled back");
            await c.ExecuteAsync("DROP TRIGGER test_delivery_failure ON notifications; DROP FUNCTION test_delivery_failure();");
            await c.ExecuteAsync("INSERT INTO service_requests(service_number,customer_id,product_id,complaint,created_by) VALUES('SRV-TEST',1,1,'Test repair',1); UPDATE service_requests SET status='READY',version=version+1 WHERE service_number='SRV-TEST';");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox WHERE event LIKE 'repair.%'")==2,"repair received and ready captured");
            await c.ExecuteAsync("""
                INSERT INTO suppliers(supplier_name,country_code) VALUES('Test supplier','IN');
                INSERT INTO purchases(supplier_id,purchase_number,purchase_date,status,created_by) VALUES(1,'PUR-TEST',CURRENT_DATE,'DRAFT',1);
                INSERT INTO purchase_receipts(purchase_id,received_date,created_by) VALUES(1,CURRENT_DATE,1);
                INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,line_total) VALUES(1,1,1,1,10,10);
                INSERT INTO warranties(sale_item_id,warranty_start_date,warranty_end_date) VALUES(1,CURRENT_DATE,CURRENT_DATE+30);
                INSERT INTO warranty_claims(warranty_id,claim_number,complaint,created_by) VALUES(1,'CLM-TEST','Test claim',1);
                UPDATE warranty_claims SET claim_status='UNDER_REVIEW',updated_at=now() WHERE claim_number='CLM-TEST';
                """);
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox WHERE event='receipt.completed'")==1,"receipt captures event");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox WHERE event='warranty.updated' AND reference='CLM-TEST'")==1,"warranty status captures human-readable reference");
            Check(await c.ExecuteScalarAsync<string>("SELECT url FROM notification_outbox WHERE event='receipt.completed'")=="/StockInflow/Details/PURCHASE/1","receipt link matches existing source route");
            // Queue while worker is offline, then restart processing.
            await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-OFFLINE',1,'COMPLETED')");await Process();
            Check((await repo.JobsAsync()).Any(x=>x.Reference=="INV-OFFLINE"&&x.Status=="DELIVERED"),"committed queue recovers after interruption");
            settings.Enabled=false;await repo.SaveSettingsAsync(settings);
            await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-DISABLED',1,'COMPLETED')");
            Check(!(await repo.JobsAsync()).Any(x=>x.Reference=="INV-DISABLED"),"disabled alerts leave billing functional");
            settings.Enabled=true;await repo.SaveSettingsAsync(settings);
            await NotificationDeliveryChecks.Run(c,repo,workerCs);
            await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status) VALUES('INV-WORKER',1,'COMPLETED')");
            var workerConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs},{"Notifications:WorkerEnabled","true"}}).Build();
            using var worker=new StockFlow.Web.Notifications.NotificationWorker(workerConfig,Microsoft.Extensions.Logging.Abstractions.NullLogger<StockFlow.Web.Notifications.NotificationWorker>.Instance);
            await worker.StartAsync(CancellationToken.None);
            using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                while(!(await repo.JobsAsync()).Any(x=>x.Reference=="INV-WORKER"&&x.Status=="DELIVERED")) await Task.Delay(50,deadline.Token);
                await worker.StopAsync(deadline.Token);
            }
            Check(worker.ExecuteTask?.IsCompletedSuccessfully==true,"hosted worker delivers and shuts down normally");
            await DashboardFollowUpDatabaseChecks.Run(c,config);
        }
        finally { NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+db+" WITH (FORCE)"); }
    }
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message); }
    static async Task Reject(Func<Task> action,string message) { try { await action(); } catch(Exception e) when(e is PostgresException or System.ComponentModel.DataAnnotations.ValidationException) {Console.WriteLine("PASS: "+message);return;}throw new Exception(message); }
}
