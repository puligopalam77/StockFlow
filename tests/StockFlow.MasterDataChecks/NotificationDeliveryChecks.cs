using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Notifications;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Notifications;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace StockFlow.MasterDataChecks;
static class NotificationDeliveryChecks
{
    public static async Task Run(NpgsqlConnection c,NotificationRepository repo,string workerCs)
    {
        Check(await repo.DeliveryReadyAsync(),"advanced schema ready");
        var settings=new NotificationDeliverySettings{DigestEnabled=true,DigestHour=0,EmailEnabled=true,SenderEmail="shop@example.test",AdministratorEmails="admin@example.test",InvoiceEmails=true,RepairEmails=true,WarrantyEmails=true,FailureAlerts=true,DailyLimit=2};
        await repo.SaveDeliverySettingsAsync(settings);
        Check(!(await repo.DeliverySettingsAsync()).SenderVerified,"posted settings cannot self-verify sender");
        await repo.SetSenderVerifiedAsync(settings.SenderEmail);
        Check((await repo.DeliverySettingsAsync()).SenderVerified,"saved sender verification persists");
        await repo.SaveDeliverySettingsAsync(settings);
        Check((await repo.DeliverySettingsAsync()).SenderVerified,"unchanged sender retains verification");
        await c.ExecuteAsync("UPDATE customers SET email='customer@example.test' WHERE customer_id=1; UPDATE product_variants SET minimum_stock_level=100 WHERE variant_id=1;");
        await repo.SaveConsentAsync(new(){CustomerId=1,Channel="email",Allowed=true,Evidence="Explicit test agreement"});
        async Task Digest() { await using var w=new NpgsqlConnection(workerCs);await w.ExecuteAsync("SELECT sf_notification_digest()"); }
        await Task.WhenAll(Digest(),Digest());await Digest();
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox WHERE event='stock.digest'")==1,"concurrent stock digests run once per local day");
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_email_deliveries WHERE customer_id IS NULL")==1,"digest queues administrator email");
        await using(var tx=await c.BeginTransactionAsync())
        {
            var sale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,created_by,status,customer_id,subtotal,total_amount) VALUES('INV-EMAIL',1,'COMPLETED',1,20,20) RETURNING sale_id",transaction:tx);
            await c.ExecuteAsync("INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,line_total,product_name_snapshot) VALUES(@sale,1,1,2,10,20,'Invoice line snapshot')",new{sale},tx);
            await tx.CommitAsync();
        }
        Check(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM notification_email_deliveries WHERE customer_id=1 AND body LIKE '%Invoice line snapshot x 2%')"),"deferred email captures completed invoice lines");
        await using var worker=new NpgsqlConnection(workerCs);await worker.OpenAsync();
        async Task<EmailJob?> Claim()=>await worker.QuerySingleOrDefaultAsync<EmailJob>("SELECT id,tenant_id AS TenantId,token,request_key AS RequestKey,recipient,title,body,sender_email AS SenderEmail,sender_name AS SenderName FROM sf_notification_email_claim()");
        var digest=await Claim();Check(digest is not null,"worker claims durable email job");
        await worker.ExecuteAsync("SELECT sf_notification_email_finish(@Id,@Token,'ACCEPTED','provider-test',NULL,60)",digest);
        Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_email_deliveries WHERE id=@Id",digest)=="ACCEPTED","provider acceptance recorded separately");
        var invoice=await Claim();Check(invoice?.Recipient=="customer@example.test","consented customer email claimed");
        await worker.ExecuteAsync("SELECT sf_notification_email_finish(@Id,@Token,'ACCEPTED','wrong-token',NULL,60)",new{invoice!.Id,Token=Guid.NewGuid()});
        Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_email_deliveries WHERE id=@Id",invoice)=="LEASED","lease fencing rejects stale acknowledgement");
        await c.ExecuteAsync("UPDATE notification_email_deliveries SET lease_until=now()-interval '1 second' WHERE id=@Id",invoice);
        await Claim();
        Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_email_deliveries WHERE id=@Id",invoice)=="UNKNOWN","interrupted delivery never blindly resends");
        await c.ExecuteAsync("UPDATE service_requests SET status='REPAIRING',version=version+1 WHERE service_number='SRV-TEST'; UPDATE service_requests SET status='READY',version=version+1 WHERE service_number='SRV-TEST'");
        Check(await Claim() is null,"daily quota defers further attempts");
        await c.ExecuteAsync("UPDATE notification_email_quota SET reserved=0; UPDATE notification_email_deliveries SET next_attempt_at=now() WHERE status='PENDING'");
        await repo.SaveConsentAsync(new(){CustomerId=1,Channel="email",Allowed=false,Evidence="Customer withdrew test"});
        await Claim();await Claim();await Claim();Check(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM notification_email_deliveries WHERE customer_id=1 AND status='SKIPPED')"),"consent withdrawal stops queued messages");
        var run=Guid.NewGuid();await worker.ExecuteAsync("SELECT sf_notification_failure(1,@run,'backup'); SELECT sf_notification_failure(1,@run,'backup')",new{run});
        Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_outbox WHERE event='operations.failed' AND reference='backup'")==1,"actual backup failure reports deduplicate by run ID");
        await worker.ExecuteAsync("SELECT sf_notification_process(100)");
        Check((await repo.JobsAsync()).Any(x=>x.Event=="stock.digest"&&x.Status=="DELIVERED"),"stock digest delivered to eligible in-app recipient");
        Check((await repo.EmailHistoryAsync()).Count>0,"email history respects repository scope");
        await ProviderChecks();
        settings.DailyLimit=300;await repo.SaveDeliverySettingsAsync(settings);
        await repo.SaveConsentAsync(new(){CustomerId=1,Channel="email",Allowed=true,Evidence="Renewed explicit test consent"});
        await c.ExecuteAsync("UPDATE notification_email_deliveries SET status='SKIPPED' WHERE status IN('PENDING','RETRY','LEASED'); UPDATE notification_email_quota SET reserved=0; INSERT INTO sales(invoice_number,created_by,status,customer_id) VALUES('INV-EMAIL-WORKER',1,'COMPLETED',1)");
        var workerConfig=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",workerCs},{"Notifications:WorkerEnabled","true"},{"Notifications:Email:Enabled","true"}}).Build();
        var fake=new FakeProvider();
        using(var hosted=new NotificationWorker(workerConfig,Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationWorker>.Instance,fake))
        {
            await hosted.StartAsync(CancellationToken.None);using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM notification_email_deliveries e JOIN notification_outbox o ON o.id=e.outbox_id WHERE o.reference='INV-EMAIL-WORKER' AND e.status='ACCEPTED')")) await Task.Delay(50,deadline.Token);
            await hosted.StopAsync(deadline.Token);
        }
        Check(fake.Sent==1,"hosted worker sends and acknowledges once through fake provider");
        await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status,customer_id) VALUES('INV-EMAIL-RETRY',1,'COMPLETED',1)");
        for(var attempt=1;attempt<=5;attempt++)
        {
            var retry=await Claim();Check(retry is not null,"email retry claim "+attempt);
            await worker.ExecuteAsync("SELECT sf_notification_email_finish(@Id,@Token,'RETRY',NULL,'Rate limit',60)",retry);
            Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_email_deliveries WHERE id=@Id",retry)==(attempt==5?"FAILED":"RETRY"),"bounded email retry "+attempt);
            await c.ExecuteAsync("UPDATE notification_email_deliveries SET next_attempt_at=now() WHERE id=@Id",retry);
        }
        await c.ExecuteAsync("UPDATE notification_email_deliveries SET status='SKIPPED' WHERE status IN('PENDING','RETRY','LEASED'); INSERT INTO sales(invoice_number,created_by,status,customer_id) VALUES('INV-EMAIL-CONCURRENT',1,'COMPLETED',1)");
        async Task<EmailJob?> ParallelClaim(){await using var w=new NpgsqlConnection(workerCs);return await w.QuerySingleOrDefaultAsync<EmailJob>("SELECT id,tenant_id AS TenantId,token,request_key AS RequestKey,recipient,title,body,sender_email AS SenderEmail,sender_name AS SenderName FROM sf_notification_email_claim()");}
        var concurrent=await Task.WhenAll(ParallelClaim(),ParallelClaim());Check(concurrent.Count(x=>x is not null)==1,"concurrent email workers claim one row once");
        settings.DigestEnabled=false;settings.EmailEnabled=false;settings.FailureAlerts=false;await repo.SaveDeliverySettingsAsync(settings);
    }
    static async Task ProviderChecks()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Notifications:Email:Tenants:1:ApiKey","test-only"}}).Build();
        var job=new EmailJob(1,1,Guid.NewGuid(),Guid.NewGuid(),"customer@example.test","Test","Text","shop@example.test","Shop");
        foreach(var pair in new[]{(HttpStatusCode.Created,"ACCEPTED"),(HttpStatusCode.TooManyRequests,"RETRY"),(HttpStatusCode.ServiceUnavailable,"UNKNOWN"),(HttpStatusCode.Unauthorized,"FAILED")})
        {
            using var http=new HttpClient(new Handler(pair.Item1));var provider=new BrevoNotificationEmailProvider(http,config);
            Check((await provider.SendAsync(job,CancellationToken.None)).Status==pair.Item2,"provider response "+pair.Item1);
        }
    }
    sealed class Handler(HttpStatusCode status):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Check(request.RequestUri!.Host=="api.brevo.com"&&request.Headers.Contains("api-key"),"provider destination and authentication fixed");
            var body=await request.Content!.ReadAsStringAsync(token);Check(body.Contains("Idempotency-Key")&&body.Contains("textContent"),"provider stable request key and plain text");
            return new(status){Content=new StringContent("{\"messageId\":\"test-message\"}")};
        }
    }
    sealed class FakeProvider:INotificationEmailProvider
    {
        public int Sent;
        public Task<bool> VerifySenderAsync(long tenant,string sender,CancellationToken token)=>Task.FromResult(true);
        public Task<EmailResult> SendAsync(EmailJob job,CancellationToken token){Interlocked.Increment(ref Sent);return Task.FromResult(new EmailResult("ACCEPTED","fake-hosted"));}
    }
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
}
