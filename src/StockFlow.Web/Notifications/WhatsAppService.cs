using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Web.Notifications;

public sealed class WhatsAppService(DbConnectionFactory factory,BusinessScope scope,MetaWhatsAppClient meta,WhatsAppTokenProtector protector,IOptions<WhatsAppOptions> options)
{
    public async Task<bool> Ready()
    {await using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_wa_signup_begin()') IS NOT NULL");}
    public async Task<WhatsAppConnection?> Connection()
    {
        await using var c=factory.CreateConnection();return await c.QuerySingleOrDefaultAsync<WhatsAppConnection>("""
            SELECT connection_id AS ConnectionId,waba_id AS WabaId,phone_id AS PhoneId,display_number AS DisplayNumber,business_name AS BusinessName,
            enabled,billing_confirmed AS BillingConfirmed,invoice_enabled AS InvoiceEnabled,payment_enabled AS PaymentEnabled,daily_limit AS DailyLimit,disconnected_at AS DisconnectedAt
            FROM public.notification_whatsapp_connections
            """);
    }
    public async Task<IReadOnlyList<WhatsAppTemplate>> Templates()
    {
        await using var c=factory.CreateConnection();return (await c.QueryAsync<WhatsAppTemplate>("SELECT event,template_name AS TemplateName,language,body,status,synced_at AS SyncedAt FROM public.notification_whatsapp_templates ORDER BY event")).AsList();
    }
    public async Task<IReadOnlyList<WhatsAppDelivery>> Deliveries()
    {
        await using var c=factory.CreateConnection();return (await c.QueryAsync<WhatsAppDelivery>("SELECT id,event,reference,'…'||right(recipient,4) AS Recipient,status,attempts,diagnostic,created_at AS CreatedAt FROM public.notification_whatsapp_deliveries ORDER BY id DESC LIMIT 50")).AsList();
    }
    public async Task<Guid> Begin()
    {
        if(!options.Value.Configured) throw new ValidationException("The administrator must complete Meta server configuration first.");
        await using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<Guid>("SELECT public.sf_wa_signup_begin()");
    }
    public async Task Complete(WhatsAppSignupCompletion input,CancellationToken ct)
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT public.sf_wa_signup_consume(@Ticket)",input,cancellationToken:ct))) throw new ValidationException("Signup expired, was already used, or the active shop changed. Start again.");
        if(input.BusinessApp && !options.Value.CoexistenceEnabled) throw new ValidationException("Business App onboarding is not enabled for this integration.");
        var account=await meta.Connect(input.Code,input.WabaId,input.PhoneId,input.RegistrationPin,input.BusinessApp,ct);
        var cipher=protector.Protect(scope.TenantId,input.PhoneId,account.Token);
        await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_wa_connect(@Id,@WabaId,@PhoneId,@DisplayNumber,@BusinessName,@Cipher)",new{Id=Guid.NewGuid(),input.WabaId,input.PhoneId,account.DisplayNumber,account.BusinessName,Cipher=cipher},cancellationToken:ct));
    }
    private async Task<(WhatsAppSecret Account,string Token)> Authorization()
    {
        await using var c=factory.CreateConnection();var account=await c.QuerySingleOrDefaultAsync<WhatsAppSecret>("SELECT connection_id AS ConnectionId,waba_id AS WabaId,phone_id AS PhoneId,protected_token AS ProtectedToken FROM public.sf_wa_secret()");
        if(account is null) throw new ValidationException("Connect this shop's WhatsApp account first.");
        try{return (account,protector.Unprotect(scope.TenantId,account.PhoneId,account.ProtectedToken));}
        catch(CryptographicException){throw new ValidationException("The saved authorization cannot be read. Restore the server's protection keys or reconnect this account.");}
    }
    public async Task SubmitTemplate(string kind,CancellationToken ct)
    {
        if(kind is not ("invoice.completed" or "payment.recorded")) throw new ValidationException("Choose an invoice or payment template.");
        var auth=await Authorization();await meta.CreateTemplate(auth.Account.WabaId,auth.Token,kind,ct);
    }
    public async Task Sync(CancellationToken ct=default)
    {
        var auth=await Authorization();var templates=await meta.Templates(auth.Account.WabaId,auth.Token,ct);
        await using var c=factory.CreateConnection();await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        // Serialize with reconnect: never save results obtained for an older authorization.
        // Token comparison is performed in SQL, without returning or logging the credential.
        var same=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM public.sf_wa_secret() s WHERE s.connection_id=@ConnectionId AND s.protected_token=@ProtectedToken)",auth.Account,tx,cancellationToken:ct));
        if(!same) throw new ValidationException("The WhatsApp connection changed. Sync again.");
        foreach(var kind in new[]{"invoice.completed","payment.recorded"})
        {
            var template=templates.FirstOrDefault(t=>t.Name==MetaWhatsAppClient.TemplateName(kind) && t.Language=="en");
            await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_wa_template(@Kind,@Id,@Name,@Language,@Body,@Approved)",new{Kind=kind,Id=template?.Id??"",Name=MetaWhatsAppClient.TemplateName(kind),Language="en",Body=template?.Body??MetaWhatsAppClient.TemplateBody(kind),Approved=template?.Status=="APPROVED"},tx,cancellationToken:ct));
        }
        await tx.CommitAsync(ct);
    }
    public async Task Save(WhatsAppPreferences input)
    {
        Validator.ValidateObject(input,new ValidationContext(input),true);
        if(input.Enabled && !options.Value.Enabled) throw new ValidationException("The administrator must enable WhatsApp delivery on the server first.");
        await using var c=factory.CreateConnection();await c.ExecuteAsync("SELECT public.sf_wa_settings(@Enabled,@BillingConfirmed,@InvoiceEnabled,@PaymentEnabled,@DailyLimit)",input);
    }
    public async Task Disconnect()
    {await using var c=factory.CreateConnection();await c.ExecuteAsync("SELECT public.sf_wa_disconnect()");}
}

// The worker/webhook role has function execution only; it has no business table grants.
public sealed class WhatsAppWorkerStore(IConfiguration configuration)
{
    public NpgsqlConnection OpenConnection()=>new(new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("NotificationWorker")??configuration.GetConnectionString("DefaultConnection"))
        {Options="-c role=stockflow_notification_worker -c statement_timeout=10000",MaxPoolSize=2}.ConnectionString);
}
public sealed class WhatsAppWorker(WhatsAppWorkerStore store,MetaWhatsAppClient meta,WhatsAppTokenProtector protector,IOptions<WhatsAppOptions> options,IConfiguration config,ILogger<WhatsAppWorker> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Value.Enabled || !options.Value.Configured || !config.GetValue<bool>("Notifications:WorkerEnabled")) return;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Notifications:PollSeconds",10),5,300)));var failures=0;
        try
        {
            do
            {
                try
                {
                    await using var c=store.OpenConnection();await c.OpenAsync(stoppingToken);
                    if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT to_regprocedure('public.sf_wa_claim()') IS NOT NULL",cancellationToken:stoppingToken))) continue;
                    var refresh=await c.QuerySingleOrDefaultAsync<WhatsAppRefresh>(new CommandDefinition("SELECT connection_id AS ConnectionId,tenant_id AS TenantId,generation,waba_id AS WabaId,phone_id AS PhoneId,protected_token AS ProtectedToken FROM public.sf_wa_refresh_claim()",cancellationToken:stoppingToken));
                    if(refresh is not null)
                    {
                        try
                        {
                            var templates=await meta.Templates(refresh.WabaId,protector.Unprotect(refresh.TenantId,refresh.PhoneId,refresh.ProtectedToken),stoppingToken);
                            await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_wa_refresh_finish(@ConnectionId,@Generation,CAST(@Templates AS jsonb))",new{refresh.ConnectionId,refresh.Generation,Templates=System.Text.Json.JsonSerializer.Serialize(templates)},cancellationToken:stoppingToken));
                        }
                        catch(Exception error) when(error is HttpRequestException or ValidationException or CryptographicException or System.Text.Json.JsonException)
                        {log.LogWarning("WhatsApp template refresh unavailable. Approval refresh will retry; stale approvals pause sending.");}
                    }
                    for(var i=0;i<5 && !stoppingToken.IsCancellationRequested;i++)
                    {
                        var job=await c.QuerySingleOrDefaultAsync<WhatsAppJob>(new CommandDefinition("SELECT id,tenant_id AS TenantId,connection_id AS ConnectionId,token,request_key AS RequestKey,phone_id AS PhoneId,protected_token AS ProtectedToken,recipient,template_name AS TemplateName,language,parameters FROM public.sf_wa_claim()",cancellationToken:stoppingToken));
                        if(job is null) break;
                        WhatsAppResult result;
                        try {result=await meta.Send(job,protector.Unprotect(job.TenantId,job.PhoneId,job.ProtectedToken),stoppingToken);}
                        catch(CryptographicException){result=new("FAILED",Diagnostic:"Authorization unavailable. Restore server protection keys or reconnect WhatsApp.");}
                        using var ack=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_wa_finish(@Id,@Token,@Status,@ProviderId,@Diagnostic,@DelaySeconds)",new{job.Id,job.Token,result.Status,result.ProviderId,result.Diagnostic,result.DelaySeconds},cancellationToken:ack.Token));
                    }
                    failures=0;
                }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception error)
                {
                    // Never log exception payloads, request URLs, access tokens, phone numbers or template parameters.
                    if(failures++%30==0) log.LogWarning("WhatsApp worker unavailable ({Type}). Check migration 076, worker role and server configuration.",error.GetType().Name);
                }
            }while(await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){}
    }
    private sealed record WhatsAppRefresh(Guid ConnectionId,long TenantId,Guid Generation,string WabaId,string PhoneId,string ProtectedToken);
}
