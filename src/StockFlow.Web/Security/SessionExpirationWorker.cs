namespace StockFlow.Web.Security;

public sealed class SessionExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<SessionExpirationWorker> logger;
    // ASP.NET supplies these services when it creates this class.
    public SessionExpirationWorker(IServiceScopeFactory scopes, ILogger<SessionExpirationWorker> logger)
    {
        this.scopes = scopes;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ISharedSessionStore>().SweepAsync();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "Could not audit expired login sessions.");
                }

                // Stopping the app cancels this wait. That is an expected shutdown.
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Finish normally instead of leaving the background task canceled.
        }
    }
}
