using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

internal static class SessionWorkerChecks
{
    public static async Task Run()
    {
        using var services = new ServiceCollection()
            .AddSingleton<ISharedSessionStore>(new FakeSharedSessionStore(TimeProvider.System, Options.Create(new SessionSecurityOptions())))
            .BuildServiceProvider();
        foreach (var fail in new[] { false, true })
        {
            var scopes = new TestScopes(services.GetRequiredService<IServiceScopeFactory>(), fail);
            var logger = new TestLogger();
            using var worker = new SessionExpirationWorker(scopes, logger);
            await worker.StartAsync(CancellationToken.None);
            await scopes.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(deadline.Token);
            if (worker.ExecuteTask is null || !worker.ExecuteTask.IsCompletedSuccessfully)
                throw new Exception("Session worker did not complete normally during shutdown.");
            if (logger.Errors != (fail ? 1 : 0)) throw new Exception("Shutdown cancellation was logged as an error, or a real cleanup error was lost.");
            Console.WriteLine(fail
                ? "PASS: real cleanup errors remain logged and shutdown still completes normally."
                : "PASS: stopping the worker cancels the timer without a faulted or canceled background task.");
        }
    }

    private sealed class TestScopes(IServiceScopeFactory inner, bool fail) : IServiceScopeFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IServiceScope CreateScope()
        {
            Started.TrySetResult();
            if (fail) throw new InvalidOperationException("Test cleanup failure");
            return inner.CreateScope();
        }
    }

    private sealed class TestLogger : ILogger<SessionExpirationWorker>
    {
        public int Errors { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Error) Errors++; }
    }
}
