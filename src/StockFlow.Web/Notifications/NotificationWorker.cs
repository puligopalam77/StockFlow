using Dapper;
using Npgsql;

namespace StockFlow.Web.Notifications;

public sealed class NotificationWorker(IConfiguration configuration, ILogger<NotificationWorker> logger, INotificationEmailProvider? emailProvider=null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Notifications:WorkerEnabled")) return;
        var cs = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("NotificationWorker")
            ?? configuration.GetConnectionString("DefaultConnection"))
        {
            Options = "-c role=stockflow_notification_worker -c statement_timeout=10000",
            MaxPoolSize = 2
        };
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Notifications:PollSeconds", 10),5,300)));
        var failures = 0;
        try
        {
            do
            {
                try
                {
                    await using var c = new NpgsqlConnection(cs.ConnectionString);
                    await c.OpenAsync(stoppingToken);
                    await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT public.sf_notification_process(25)",
                        commandTimeout:15,cancellationToken:stoppingToken));
                    var advanced=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT to_regprocedure('public.sf_notification_digest()') IS NOT NULL",cancellationToken:stoppingToken));
                    if(advanced)
                    {
                        await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT public.sf_notification_digest()",commandTimeout:15,cancellationToken:stoppingToken));
                        if(configuration.GetValue<bool>("Notifications:Email:Enabled") && emailProvider is not null)
                        for(var i=0;i<5 && !stoppingToken.IsCancellationRequested;i++)
                        {
                            var job=await c.QuerySingleOrDefaultAsync<EmailJob>(new CommandDefinition("SELECT id,tenant_id AS TenantId,token,request_key AS RequestKey,recipient,title,body,sender_email AS SenderEmail,sender_name AS SenderName FROM public.sf_notification_email_claim()",cancellationToken:stoppingToken));
                            if(job is null) break;
                            var result=await emailProvider.SendAsync(job,stoppingToken);
                            using var ack=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            await c.ExecuteAsync(new CommandDefinition("SELECT public.sf_notification_email_finish(@Id,@Token,@Status,@ProviderId,@Diagnostic,@DelaySeconds)",new{job.Id,job.Token,result.Status,result.ProviderId,result.Diagnostic,result.DelaySeconds},cancellationToken:ack.Token));
                        }
                    }
                    failures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    // Log once initially, then every 30 failed polls; never log credentials.
                    if (failures++ % 30 == 0) logger.LogError(error,"Notification delivery unavailable. Verify migration 039 and worker role membership; committed events remain queued.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
