using System.Net.Sockets;
using Microsoft.Extensions.Options;
using StockFlow.Web.Help;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

internal static class HelpAiCancellationChecks
{
    public static async Task Run()
    {
        var options = Options.Create(new HelpAiOptions { Enabled = true, Model = "test-only", TimeoutSeconds = 1, PreferLocalAnswers = false });
        using var client = new HttpClient(new InterruptedHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new OllamaHelpProvider(client, options);
        await TimeoutFailure(() => provider.SelectAsync("How do I make a bill?", [], CancellationToken.None));
        await TimeoutFailure(() => provider.InterpretSalesAsync("How many units sold today?", CancellationToken.None));

        using var shortClient = new HttpClient(new InterruptedHandler()) { Timeout = TimeSpan.FromMilliseconds(30) };
        var service = new HelpAiService(new OllamaHelpProvider(shortClient, options), options);
        if (await service.InterpretSalesAsync("How many units sold today?", CancellationToken.None) is not null || !service.InterpretationUnavailable)
            throw new Exception("Model timeout must mark interpretation unavailable, never fabricate totals.");
        var access = new PermissionContext();
        var expected = LocalHelpSearch.Answer("I forgot my password", access);
        var answer = await service.AskAsync("I forgot my password", access, CancellationToken.None);
        if (answer.Status != expected.Status || answer.Message != expected.Message || !answer.Sections.SequenceEqual(expected.Sections))
            throw new Exception("Model timeout did not preserve approved local help fallback.");

        foreach (var interpret in new[] { false, true })
        {
            using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            try
            {
                if (interpret) await service.InterpretSalesAsync("How many units sold today?", caller.Token);
                else await service.AskAsync("I forgot my password", access, caller.Token);
            }
            catch (OperationCanceledException) when (caller.IsCancellationRequested) { continue; }
            throw new Exception("Caller cancellation was swallowed or converted into a model timeout.");
        }
        Console.WriteLine("PASS: provider and HttpClient timeouts, aborted socket, local fallback, unavailable totals and caller cancellation.");
    }

    private static async Task TimeoutFailure(Func<Task> call)
    {
        try { await call(); }
        catch (HttpRequestException error) when (error.InnerException is OperationCanceledException) { return; }
        throw new Exception("Provider timeout escaped without a model failure classification.");
    }

    private sealed class InterruptedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException("The operation was canceled.", new SocketException(995), cancellationToken);
            }
            throw new Exception("An infinite request completed unexpectedly.");
        }
    }
}
