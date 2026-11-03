using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Notifications;

namespace StockFlow.MasterDataChecks;

internal static class WhatsAppChecks
{
    public static async Task Run()
    {
        // Disposable local cluster only. Never read the configured shop database connection.
        const string host="Host=127.0.0.1;Port=55443;Username=postgres;Database=";
        var database="whatsapp_checks_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE product_edit_069_test");
        try
        {
            await using var c=new NpgsqlConnection(host+database);await c.OpenAsync();
            foreach(var file in Directory.GetFiles("database/migrations","*.sql").Where(f=>int.Parse(Path.GetFileName(f)[..3])>=70).Order()) await c.ExecuteAsync(await File.ReadAllTextAsync(file));
            await c.ExecuteAsync("SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.store_id','1',false),set_config('app.location_id','1',false),set_config('app.business_profile_code','ELECTRICAL',false)");
            var settings=new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database},{"ConnectionStrings:NotificationWorker",host+database},{"Notifications:WorkerEnabled","false"},{"Notifications:WhatsApp:Enabled","false"}};
            var config=new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,IsOwner=true,ProfileDataVisibilityAvailable=true};
            var factory=new DbConnectionFactory(config,scope);await using var owner=factory.CreateConnection();await owner.OpenAsync();
            var protector=new WhatsAppTokenProtector(new EphemeralDataProtectionProvider());var options=Options.Create(new WhatsAppOptions{Enabled=true,AppId="123456789",AppSecret=new string('s',32),SignupConfigurationId="987654321",GraphVersion="v25.0",WebhookVerifyToken=new string('v',40)});
            var handler=new MetaHandler();using var client=new HttpClient(handler);var meta=new MetaWhatsAppClient(client,options);
            var service=new WhatsAppService(factory,scope,meta,protector,options);
            Check(await service.Ready(),"manual migration 076 creates the expected schema");
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_whatsapp_deliveries")==0,"migration creates no historical messages");
            var first=await service.Begin();var second=await service.Begin();
            await Reject(()=>service.Complete(new(){Ticket=first,Code="synthetic-code",WabaId="1234567890",PhoneId="9876543210"},default),"starting a new signup invalidates older tickets");
            await service.Complete(new(){Ticket=second,Code="synthetic-code",WabaId="1234567890",PhoneId="9876543210"},default);
            handler.WrongApp=true;await Reject(()=>meta.Connect("synthetic-code","1234567890","9876543210",null,false,default),"authorization for another Meta app is rejected");handler.WrongApp=false;
            await Reject(()=>meta.Connect("synthetic-code","1234567890","1111111111",null,false,default),"browser cannot substitute a number outside the authorized WABA");
            handler.CloudRegistered=false;
            await Reject(()=>meta.Connect("synthetic-code","1234567890","9876543210",null,false,default),"new Cloud API registration requires the owner's PIN");
            await meta.Connect("synthetic-code","1234567890","9876543210","123456",false,default);Check(handler.Registrations==1,"new Cloud API number is registered through the verified account");
            await meta.Connect("synthetic-code","1234567890","9876543210",null,true,default);Check(handler.Registrations==1,"Business App onboarding does not register or migrate the mobile number");handler.CloudRegistered=true;
            await Reject(()=>service.Complete(new(){Ticket=second,Code="synthetic-code",WabaId="1234567890",PhoneId="9876543210"},default),"signup tickets are consumed once");
            var connection=(await service.Connection())!;Check(!connection.Enabled&&!connection.BillingConfirmed,"connecting never auto-enables sending or billing");
            var cipher=await c.ExecuteScalarAsync<string>("SELECT protected_token FROM notification_whatsapp_connections");
            Check(cipher is not null&&!cipher.Contains("synthetic-access-token")&&protector.Unprotect(1,connection.PhoneId,cipher)=="synthetic-access-token","stored authorization is encrypted and bound to tenant and sender");
            try{protector.Unprotect(2,connection.PhoneId,cipher!);throw new Exception("Tenant token scope bypass");}catch(CryptographicException){Check(true,"another tenant cannot use the saved authorization");}
            try{await owner.ExecuteScalarAsync<string>("SELECT protected_token FROM notification_whatsapp_connections");throw new Exception("Runtime ciphertext exposure");}catch(PostgresException e) when(e.SqlState=="42501"){Check(true,"runtime cannot read ciphertext directly");}
            await Reject(()=>service.Save(new(){Enabled=true,InvoiceEnabled=true,BillingConfirmed=false}),"billing confirmation required before enabling");
            await Reject(()=>service.Save(new(){Enabled=true,InvoiceEnabled=true,BillingConfirmed=true}),"unapproved templates cannot be enabled");
            await service.Sync();Check((await service.Templates()).Count(t=>t.Status=="APPROVED")==2,"only matching approved utility templates are accepted");
            handler.WrongBody=true;await service.Sync();Check((await service.Templates()).All(t=>t.Status=="UNAVAILABLE"),"a template with changed content is rejected");handler.WrongBody=false;await service.Sync();
            await service.Save(new(){Enabled=true,BillingConfirmed=true,InvoiceEnabled=true,PaymentEnabled=true,DailyLimit=100});
            await c.ExecuteAsync("UPDATE customers SET mobile_number='9876543210',is_active=true WHERE tenant_id=1 AND customer_id=1");
            var notifications=new NotificationRepository(factory);
            await notifications.SaveConsentAsync(new(){CustomerId=1,Channel="whatsapp",Allowed=true,Evidence="Synthetic explicit customer agreement"});
            async Task<long> Sale(bool payment=false)
            {
                await using var tx=await c.BeginTransactionAsync();
                var sale=await c.ExecuteScalarAsync<long>("INSERT INTO sales(invoice_number,created_by,status,customer_id,subtotal,total_amount) VALUES(@Reference,1,'COMPLETED',1,100,100) RETURNING sale_id",new{Reference="WA-"+Guid.NewGuid().ToString("N")[..12]},tx);
                if(payment)await c.ExecuteAsync("INSERT INTO payments(sale_id,payment_mode,amount,created_by) VALUES(@Sale,'CASH',100,1)",new{Sale=sale},tx);
                await tx.CommitAsync();return sale;
            }
            await using(var tx=await c.BeginTransactionAsync())
            {await c.ExecuteAsync("INSERT INTO sales(invoice_number,created_by,status,customer_id) VALUES('WA-ROLLBACK',1,'COMPLETED',1)",transaction:tx);await tx.RollbackAsync();}
            Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_whatsapp_deliveries")==0,"rolled-back invoices produce no sends");
            var sale=await Sale(true);Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_whatsapp_deliveries WHERE sale_id=@sale",new{sale})==2,"committed invoices and payments each queue exactly once");
            await c.ExecuteAsync("UPDATE sales SET status=status WHERE sale_id=@sale",new{sale});Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_whatsapp_deliveries WHERE sale_id=@sale",new{sale})==2,"unrelated sale updates do not duplicate messages");
            var workerCs=new NpgsqlConnectionStringBuilder(host+database){Options="-c role=stockflow_notification_worker"}.ConnectionString;
            async Task<WhatsAppJob?> Claim(){await using var w=new NpgsqlConnection(workerCs);return await w.QuerySingleOrDefaultAsync<WhatsAppJob>("SELECT id,tenant_id AS TenantId,connection_id AS ConnectionId,token,request_key AS RequestKey,phone_id AS PhoneId,protected_token AS ProtectedToken,recipient,template_name AS TemplateName,language,parameters FROM sf_wa_claim()");}
            await using var worker=new NpgsqlConnection(workerCs);await worker.OpenAsync();
            var claims=(await Task.WhenAll(Claim(),Claim(),Claim())).Where(j=>j is not null).Select(j=>j!).ToList();
            // SKIP LOCKED may briefly skip the other candidate while a cursor owns its lock.
            if(claims.Count<2 && await Claim() is {} additional) claims.Add(additional);
            Check(claims.Count==2&&claims.Select(j=>j.Id).Distinct().Count()==2,"concurrent workers claim each message once");
            var job=claims.First(j=>j is not null)!;
            await worker.ExecuteAsync("SELECT sf_wa_status(@PhoneId,'wamid.synthetic','READ',@RequestKey)",job);
            await worker.ExecuteAsync("SELECT sf_wa_finish(@Id,@Token,'ACCEPTED','wamid.synthetic',NULL,300)",job);
            Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE id=@Id",job)=="READ","a webhook arriving before HTTP acknowledgement remains Read");
            await worker.ExecuteAsync("SELECT sf_wa_status(@PhoneId,'wamid.synthetic','DELIVERED',@RequestKey);SELECT sf_wa_status(@PhoneId,'wamid.synthetic','FAILED',@RequestKey)",job);
            Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE id=@Id",job)=="READ","late and duplicate callbacks cannot downgrade Read");
            var pending=claims.First(j=>j is not null&&j.Id!=job.Id)!;
            await worker.ExecuteAsync("SELECT sf_wa_finish(@Id,@Token,'ACCEPTED','stale',NULL,300)",new{pending.Id,Token=Guid.NewGuid()});
            Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE id=@Id",pending)=="LEASED","stale worker acknowledgements cannot complete a lease");
            await c.ExecuteAsync("UPDATE notification_whatsapp_deliveries SET lease_until=now()-interval '1 second' WHERE id=@Id",pending);await Claim();
            Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE id=@Id",pending)=="UNKNOWN","expired leases are Unknown and never automatically replayed");
            await Sale();await notifications.SaveConsentAsync(new(){CustomerId=1,Channel="whatsapp",Allowed=false,Evidence="Synthetic consent withdrawal"});Check(await Claim() is null,"consent is rechecked before queued sends");
            await notifications.SaveConsentAsync(new(){CustomerId=1,Channel="whatsapp",Allowed=true,Evidence="Synthetic fresh explicit consent"});await Sale();
            await worker.ExecuteAsync("SELECT sf_wa_optout(@PhoneId,'919876543210','stop-message')",connection);Check(await Claim() is null,"STOP blocks queued messages independently of staff consent editing");
            await Task.Delay(20);await notifications.SaveConsentAsync(new(){CustomerId=1,Channel="whatsapp",Allowed=true,Evidence="Customer explicitly agreed again"});
            await worker.ExecuteAsync("SELECT sf_wa_optout(@PhoneId,'919876543210','stop-message')",connection);await Sale();var renewed=await Claim();Check(renewed is not null,"duplicate STOP webhook does not invalidate later explicit consent");
            handler.SendStatus=HttpStatusCode.OK;var sent=await meta.Send(renewed!,"synthetic-access-token",default);Check(sent.Status=="ACCEPTED"&&handler.LastSend?.Contains("biz_opaque_callback_data")==true,"provider uses the authorized sender and correlation metadata");
            handler.SendStatus=HttpStatusCode.TooManyRequests;Check((await meta.Send(renewed!,"synthetic-access-token",default)).Status=="RETRY","explicit rate rejection is retried");
            handler.SendStatus=HttpStatusCode.InternalServerError;Check((await meta.Send(renewed!,"synthetic-access-token",default)).Status=="UNKNOWN","ambiguous provider failures are not blindly retried");
            handler.SendStatus=HttpStatusCode.Unauthorized;Check((await meta.Send(renewed!,"synthetic-access-token",default)).Status=="FAILED","revoked credentials produce a terminal failure");
            await worker.ExecuteAsync("SELECT sf_wa_finish(@Id,@Token,'ACCEPTED','wamid.renewed',NULL,300)",renewed);
            await service.Save(new(){Enabled=true,BillingConfirmed=true,InvoiceEnabled=true,DailyLimit=1});await c.ExecuteAsync("DELETE FROM notification_whatsapp_quota");
            await Sale();await Sale();var limited=await Claim();Check(limited is not null&&await Claim() is null,"daily quota atomically defers additional sends");
            await worker.ExecuteAsync("SELECT sf_wa_finish(@Id,@Token,'RETRY',NULL,'Rate limited',60)",limited);
            await c.ExecuteAsync("UPDATE notification_whatsapp_deliveries SET next_attempt_at=now() WHERE id=@Id;UPDATE notification_whatsapp_quota SET reserved=0",limited);
            for(var retry=0;retry<4;retry++)
            {
                var retryJob=await Claim();Check(retryJob?.Id==limited!.Id,"rate-limited retry retains its original delivery identity");
                await worker.ExecuteAsync("SELECT sf_wa_finish(@Id,@Token,'RETRY',NULL,'Rate limited',60)",retryJob);
                await c.ExecuteAsync("UPDATE notification_whatsapp_deliveries SET next_attempt_at=now() WHERE id=@Id;UPDATE notification_whatsapp_quota SET reserved=0",limited);
            }
            Check(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE id=@Id",limited)=="FAILED","retries stop after five send attempts");
            await c.ExecuteAsync("UPDATE notification_whatsapp_deliveries SET status='SKIPPED' WHERE status IN('PENDING','RETRY');UPDATE notification_whatsapp_quota SET reserved=0");
            await service.Save(new(){Enabled=true,BillingConfirmed=true,InvoiceEnabled=true,DailyLimit=100});
            await c.ExecuteAsync("UPDATE notification_whatsapp_templates SET synced_at=now()-interval '2 days'");
            var hostedSale=await Sale();Check(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM notification_whatsapp_deliveries WHERE sale_id=@Sale",new{Sale=hostedSale})==1&&await Claim() is null,"stale approvals retain events in the queue while pausing sending");
            handler.SendStatus=HttpStatusCode.OK;
            var hostedConfig=new ConfigurationBuilder().AddInMemoryCollection(settings).AddInMemoryCollection(new Dictionary<string,string?>{{"Notifications:WorkerEnabled","true"},{"Notifications:PollSeconds","5"}}).Build();
            using(var hosted=new WhatsAppWorker(new WhatsAppWorkerStore(hostedConfig),meta,protector,options,hostedConfig,Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppWorker>.Instance))
            {
                await hosted.StartAsync(default);using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
                while(!await c.ExecuteScalarAsync<bool>("SELECT bool_and(synced_at>now()-interval '1 minute') FROM notification_whatsapp_templates"))await Task.Delay(50,deadline.Token);
                Check(true,"background worker refreshes stale template approvals automatically");
                while(await c.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE sale_id=@Sale",new{Sale=hostedSale})!="ACCEPTED")await Task.Delay(50,deadline.Token);
                await hosted.StopAsync(default);Check(true,"hosted worker sends and acknowledges committed invoices using a fake Meta provider");
            }
            scope.UserId=2;scope.IsOwner=false;Check(await service.Connection() is null,"staff cannot inspect owner connection settings");await Reject(()=>service.Begin(),"staff cannot initiate Meta onboarding");scope.UserId=1;scope.IsOwner=true;
            scope.TenantId=2;Check(await service.Connection() is null,"connections are invisible across tenants");scope.TenantId=1;
            Check(await c.ExecuteScalarAsync<bool>("SELECT NOT EXISTS(SELECT 1 FROM notification_whatsapp_audit WHERE before_value ? 'protected_token' OR after_value ? 'protected_token')"),"connection audit history never includes credentials");
            await Http(config,options.Value,host+database);
            await notifications.SaveConsentAsync(new(){CustomerId=1,Channel="whatsapp",Allowed=true,Evidence="Synthetic agreement after HTTP opt-out test"});
            await Sale();await service.Disconnect();Check(await Claim() is null,"disconnect prevents pending messages from using a replacement sender");
            Check((await service.Connection())!.DisconnectedAt is not null,"disconnect removes local credentials and pauses sending");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static async Task Http(IConfiguration config,WhatsAppOptions options,string cs)
    {
        using var factory=new Factory();using var app=factory.WithWebHostBuilder(b=>
        {
            b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",cs},{"ConnectionStrings:NotificationWorker",cs},{"Notifications:WorkerEnabled","false"},{"Notifications:WhatsApp:Enabled","false"}}));
            b.ConfigureServices(s=>s.Configure<WhatsAppOptions>(o=>{o.AppId=options.AppId;o.AppSecret=options.AppSecret;o.SignupConfigurationId=options.SignupConfigurationId;o.GraphVersion=options.GraphVersion;o.WebhookVerifyToken=options.WebhookVerifyToken;o.Enabled=false;}));
        });
        using var anonymous=app.CreateClient(new(){AllowAutoRedirect=false});
        Check((int)(await anonymous.PostAsJsonAsync("/api/configuration/whatsapp/signup",new{})).StatusCode==401,"anonymous onboarding is denied");
        using var owner=app.CreateClient(new(){AllowAutoRedirect=false});owner.DefaultRequestHeaders.Add("Test-Role","Admin");
        Check((int)(await owner.PostAsJsonAsync("/api/configuration/whatsapp/signup",new{})).StatusCode==400,"onboarding requires CSRF protection");
        var token=await owner.GetFromJsonAsync<JsonObject>("/api/antiforgery");owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
        var page=await owner.GetStringAsync("/Configuration/Notifications/WhatsApp");Check(page.Contains("WhatsApp Business")&&page.Contains("__RequestVerificationToken")&&!page.Contains("synthetic-access-token"),"owner setup page renders safely without exposing credentials");
        var path=Path.Combine(AppContext.BaseDirectory,"whatsapp-fixtures");Directory.CreateDirectory(path);await File.WriteAllTextAsync(Path.Combine(path,"settings.html"),page);
        Check((int)(await owner.PostAsJsonAsync("/api/configuration/whatsapp/signup",new{})).StatusCode==200,"authorized signup returns a scoped ticket");
        var body=Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\",\"entry\":[]}");
        using var bad=new HttpRequestMessage(HttpMethod.Post,"/api/whatsapp/webhook"){Content=new ByteArrayContent(body)};Check((int)(await anonymous.SendAsync(bad)).StatusCode==401,"unsigned webhooks are denied");
        var signature="sha256="+Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.AppSecret),body));
        Check(WhatsAppWebhookSecurity.Verify(body,signature,options.AppSecret)&&!WhatsAppWebhookSecurity.Verify(Encoding.UTF8.GetBytes("tampered"),signature,options.AppSecret),"webhook signatures cover exact raw request bytes");
        using var good=new HttpRequestMessage(HttpMethod.Post,"/api/whatsapp/webhook"){Content=new ByteArrayContent(body)};good.Headers.Add("X-Hub-Signature-256",signature);Check((int)(await anonymous.SendAsync(good)).StatusCode==200,"signed Meta callbacks work without a staff session or CSRF token");
        await using var database=new NpgsqlConnection(cs);await database.OpenAsync();
        var requestKey=await database.ExecuteScalarAsync<Guid>("SELECT request_key FROM notification_whatsapp_deliveries WHERE status='ACCEPTED' ORDER BY id DESC LIMIT 1");
        var callback=JsonSerializer.SerializeToUtf8Bytes(new{ @object="whatsapp_business_account",entry=new[]{new{id="1234567890",changes=new[]{new{field="messages",value=new{metadata=new{phone_number_id="9876543210"},statuses=new[]{new{id="wamid.http",status="read",biz_opaque_callback_data=requestKey.ToString()}}}}}}}});
        using var statusCallback=new HttpRequestMessage(HttpMethod.Post,"/api/whatsapp/webhook"){Content=new ByteArrayContent(callback)};statusCallback.Headers.Add("X-Hub-Signature-256","sha256="+Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.AppSecret),callback)));
        Check((int)(await anonymous.SendAsync(statusCallback)).StatusCode==200&&await database.ExecuteScalarAsync<string>("SELECT status FROM notification_whatsapp_deliveries WHERE request_key=@Key",new{Key=requestKey})=="READ","HTTP callback parsing routes status to the authorized sender's delivery");
        var optout=JsonSerializer.SerializeToUtf8Bytes(new{@object="whatsapp_business_account",entry=new[]{new{id="1234567890",changes=new[]{new{field="messages",value=new{metadata=new{phone_number_id="9876543210"},messages=new[]{new{id="http-stop",from="919876543210",type="text",text=new{body="STOP"}}}}}}}}});
        using var stopCallback=new HttpRequestMessage(HttpMethod.Post,"/api/whatsapp/webhook"){Content=new ByteArrayContent(optout)};stopCallback.Headers.Add("X-Hub-Signature-256","sha256="+Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.AppSecret),optout)));
        Check((int)(await anonymous.SendAsync(stopCallback)).StatusCode==200&&await database.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM notification_whatsapp_suppressions WHERE message_id='http-stop')"),"incoming signed STOP messages create durable suppression");
        Check(await anonymous.GetStringAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token="+options.WebhookVerifyToken+"&hub.challenge=test-challenge")=="test-challenge","Meta webhook verification challenge is supported");
    }
    private static async Task Reject(Func<Task> action,string name)
    {try{await action();}catch(ValidationException){Check(true,name);return;}catch(PostgresException e)when(e.SqlState is "23514" or "42501"){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    private sealed class MetaHandler:HttpMessageHandler
    {
        public bool WrongBody;public bool WrongApp;public bool CloudRegistered=true;public int Registrations;public HttpStatusCode SendStatus=HttpStatusCode.OK;public string? LastSend;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            object value;
            if(path.EndsWith("/oauth/access_token"))value=new{access_token="synthetic-access-token"};
            else if(path.EndsWith("/debug_token"))value=new{data=new{is_valid=true,app_id=WrongApp?"000000000":"123456789",scopes=new[]{"whatsapp_business_management","whatsapp_business_messaging"}}};
            else if(path.EndsWith("/phone_numbers"))value=new{data=new[]{new{id="9876543210",display_phone_number="+91 8000000000",verified_name="Synthetic Shop",code_verification_status="VERIFIED",platform_type=CloudRegistered?"CLOUD_API":"NOT_REGISTERED"}}};
            else if(path.EndsWith("/register")){Registrations++;value=new{success=true};}
            else if(path.EndsWith("/subscribed_apps"))value=new{success=true};
            else if(path.EndsWith("/message_templates"))value=new{data=new[]{"invoice.completed","payment.recorded"}.Select((kind,i)=>new{id=(12345000+i).ToString(),name=MetaWhatsAppClient.TemplateName(kind),language="en",status="APPROVED",category="UTILITY",components=new[]{new{type="BODY",text=WrongBody?"Changed content":MetaWhatsAppClient.TemplateBody(kind)}}}).ToArray()};
            else if(path.EndsWith("/messages")){LastSend=await request.Content!.ReadAsStringAsync(ct);using var payload=JsonDocument.Parse(LastSend);return new(SendStatus){Content=JsonContent.Create(new{messages=new[]{new{id="wamid."+payload.RootElement.GetProperty("biz_opaque_callback_data").GetString()}}})};}
            else throw new Exception("Unexpected Meta path");
            return new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
        }
    }
}
