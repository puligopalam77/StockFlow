using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace StockFlow.Web.Help;

// Starts only an already installed trusted local runtime. Does not download software/models.
public class LocalAiWorker(IConfiguration config,IOptions<HelpAiOptions> options,ILogger<LocalAiWorker> logger) : BackgroundService
{
    private Process? owned;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !config.GetValue<bool>("HelpAi:StartLocalRuntime",true)) return;
        var endpoint=new Uri(options.Value.Endpoint);
        if(endpoint.Host is not ("127.0.0.1" or "localhost") || endpoint.Port!=11434 || endpoint.Scheme!="http")
        {logger.LogInformation("Automatic AI startup requires the local loopback endpoint; configure the remote runtime separately.");return;}
        using var client=new HttpClient {Timeout=TimeSpan.FromSeconds(2)};
        async Task<bool> Running()
        {
            try {using var response=await client.GetAsync(new Uri(endpoint,"api/version"),stoppingToken);return response.IsSuccessStatusCode;}
            catch(Exception error) when(error is HttpRequestException or OperationCanceledException) {return false;}
        }
        if(await Running()) return;
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StockFlowAI");
        var runtime=config["HelpAi:RuntimePath"] ?? Path.Combine(root,"runtime","ollama.exe");
        if(!File.Exists(runtime)) {logger.LogWarning("Local AI runtime is not installed for the application service account. Built-in queries remain available.");return;}
        var start=new ProcessStartInfo(Path.GetFullPath(runtime),"serve") {UseShellExecute=false,CreateNoWindow=true};
        start.Environment["OLLAMA_HOST"]="127.0.0.1:11434";
        start.Environment["OLLAMA_NO_CLOUD"]="1";
        start.Environment["OLLAMA_MODELS"]=config["HelpAi:ModelsPath"] ?? Path.Combine(root,"models");
        start.Environment["OLLAMA_CONTEXT_LENGTH"]="4096";
        start.Environment["OLLAMA_NUM_PARALLEL"]="1";
        try
        {
            owned=Process.Start(start);
            while(!stoppingToken.IsCancellationRequested)
            {
                if(owned is null || owned.HasExited) {logger.LogWarning("Local AI runtime stopped. Built-in queries remain available.");return;}
                await Task.Delay(TimeSpan.FromSeconds(10),stoppingToken);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        catch(Exception error) when(error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {logger.LogWarning("Local AI could not start ({Type}). Built-in queries remain available.",error.GetType().Name);}
        finally
        {
            // Never stop a server that was running before this application started.
            if(owned is not null) {try {if(!owned.HasExited) owned.Kill(entireProcessTree:true);} catch(InvalidOperationException){} owned.Dispose();owned=null;}
        }
    }
}

public class HelpModelGate
{
    private readonly SemaphoreSlim slots=new(1,1);
    public async Task<IDisposable> Enter(CancellationToken cancellation)
    {
        if(!await slots.WaitAsync(TimeSpan.FromMilliseconds(250),cancellation)) throw new HttpRequestException("The local model is busy.");
        return new Lease(slots);
    }
    private sealed class Lease(SemaphoreSlim slots) : IDisposable {public void Dispose()=>slots.Release();}
}
