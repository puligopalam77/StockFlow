using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace StockFlow.Web.Help;

public record HelpHealth(string ModelStatus, string Model, long? LatencyMilliseconds, string VoiceStatus, DateTimeOffset CheckedAt);

// A short cached connectivity/model-presence check, never a business-data prompt.
public sealed class HelpHealthService(IHttpClientFactory clients, IOptions<HelpAiOptions> options,
    IConfiguration config, IMemoryCache cache)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<HelpHealth> CheckAsync(CancellationToken cancellation)
    {
        if (cache.TryGetValue<HelpHealth>("stockbot:runtime-health", out var cached))
            return cached!;
        await gate.WaitAsync(cancellation);
        try
        {
            if (cache.TryGetValue<HelpHealth>("stockbot:runtime-health", out cached))
                return cached!;
            using var client = clients.CreateClient("StockBotHealth");
            var status = options.Value.Enabled ? "unavailable" : "disabled";
            long? latency = null;
            if (options.Value.Enabled)
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    using var response = await client.GetAsync(new Uri(new Uri(options.Value.Endpoint), "api/tags"), cancellation);
                    response.EnsureSuccessStatusCode();
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
                    var wanted = options.Value.Model.Contains(':') ? options.Value.Model : options.Value.Model + ":latest";
                    status = json.RootElement.GetProperty("models").EnumerateArray().Any(model =>
                        model.TryGetProperty("name", out var name) && name.GetString() == wanted) ? "available" : "model-missing";
                    latency = watch.ElapsedMilliseconds;
                }
                catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException || error is OperationCanceledException && !cancellation.IsCancellationRequested) { }
            }
            var voice = "unavailable";
            if (Uri.TryCreate(config["HelpVoice:Endpoint"] ?? "http://127.0.0.1:8081/", UriKind.Absolute, out var endpoint) && endpoint.IsLoopback && endpoint.Scheme == "http")
            {
                try
                {
                    using var response = await client.GetAsync(endpoint, cancellation);
                    if (response.IsSuccessStatusCode)
                        voice = "available";
                }
                catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !cancellation.IsCancellationRequested) { }
            }
            var health = new HelpHealth(status, options.Value.Model, latency, voice, DateTimeOffset.UtcNow);
            cache.Set("stockbot:runtime-health", health, TimeSpan.FromSeconds(20));
            return health;
        }
        finally { gate.Release(); }
    }
}
