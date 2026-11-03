using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace StockFlow.Web.Help;

// Only the configured local speech runtime receives microphone audio.
public sealed class HelpVoiceService(HttpClient client, IConfiguration config)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public Uri Endpoint => new(config["HelpVoice:Endpoint"] ?? "http://127.0.0.1:8081/");
    public async Task<string> Transcribe(byte[] wave, CancellationToken cancellation)
    {
        if (!Endpoint.IsLoopback || Endpoint.Scheme != "http") throw new InvalidOperationException("Speech must use a local runtime.");
        if (!await Gate.WaitAsync(0, cancellation)) throw new InvalidOperationException("Another recording is being transcribed. Try again shortly.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            using var body = new MultipartFormDataContent();
            var audio = new ByteArrayContent(wave); audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            body.Add(audio, "file", "question.wav");
            body.Add(new StringContent("json"), "response_format");
            body.Add(new StringContent("auto"), "language");
            body.Add(new StringContent("0"), "temperature");
            using var response = await client.PostAsync(new Uri(Endpoint, "inference"), body, deadline.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
            return json.RootElement.GetProperty("text").GetString()?.Trim() ?? "";
        }
        finally { Gate.Release(); }
    }
}

public sealed class LocalVoiceWorker(IConfiguration config, ILogger<LocalVoiceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellation)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockFlowAI", "voice");
        var runtime = config["HelpVoice:RuntimePath"] ?? (Directory.Exists(Path.Combine(root, "runtime")) ? Directory.EnumerateFiles(Path.Combine(root, "runtime"), "whisper-server.exe", SearchOption.AllDirectories).FirstOrDefault() : null);
        var model = config["HelpVoice:ModelPath"] ?? Path.Combine(root, "ggml-base.bin");
        var endpoint = new Uri(config["HelpVoice:Endpoint"] ?? "http://127.0.0.1:8081/");
        if (!endpoint.IsLoopback || endpoint.Scheme != "http" || !File.Exists(runtime) || !File.Exists(model)) return;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try { using var response = await client.GetAsync(endpoint, cancellation); if (response.IsSuccessStatusCode) return; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
        var start = new ProcessStartInfo(runtime!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(runtime)! };
        foreach (var argument in new[] { "--host", "127.0.0.1", "--port", endpoint.Port.ToString(), "--model", model, "--threads", "4" }) start.ArgumentList.Add(argument);
        Process? process = null;
        try { process = Process.Start(start); await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { logger.LogWarning("Local speech runtime could not start ({Type}).", error.GetType().Name); }
        finally { if (process is not null) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } process.Dispose(); } }
    }
}
