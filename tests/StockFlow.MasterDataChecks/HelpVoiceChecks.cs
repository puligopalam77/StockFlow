using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using StockFlow.Web.Help;

namespace StockFlow.MasterDataChecks;

internal static class HelpVoiceChecks
{
    public static async Task Run(Factory factory, bool live)
    {
        if (live)
        {
            using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Test-Role", "Admin");
            await client.GetAsync("/Purchases/Create"); // Start the application's installed local runtime.
            var service = factory.Services.GetRequiredService<HelpVoiceService>();
            var sample = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockFlowAI", "voice", "check.wav");
            string? text = null;
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            for (var attempt=0; attempt<10; attempt++)
            {
                try { text = await service.Transcribe(await File.ReadAllBytesAsync(sample), CancellationToken.None); break; }
                catch(HttpRequestException) { await Task.Delay(1000); }
            }
            if(text is null || !text.Contains("country",StringComparison.OrdinalIgnoreCase)) throw new Exception("Local speech sample did not transcribe.");
            Console.WriteLine($"PASS: installed local speech runtime transcribes the official English sample without browser speech or a paid API ({elapsed.Elapsed.TotalSeconds:F1}s including startup/retries)."); return;
        }
        var handler = new SpeechHandler();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<HelpVoiceService>();
            services.AddSingleton(new HelpVoiceService(new HttpClient(handler), new ConfigurationBuilder().Build()));
        }));
        using var owner = configured.CreateClient(); owner.DefaultRequestHeaders.Add("Test-Role", "Admin");
        static MultipartFormDataContent Audio(byte[] bytes) { var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(bytes),"audio","question.wav"); return form; }
        var wave = new byte[32044];
        using(var stream = new MemoryStream(wave)) using(var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8); writer.Write(wave.Length-8); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(wave.Length-44);
        }
        using(var form=Audio(wave)) {var response=await owner.PostAsync("/api/help/voice",form); if(response.StatusCode!=HttpStatusCode.BadRequest) throw new Exception("Voice must require CSRF: "+response.StatusCode+" "+await response.Content.ReadAsStringAsync());}
        var csrf = await owner.GetFromJsonAsync<JsonObject>("/api/antiforgery"); owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf!["requestToken"]!.GetValue<string>());
        using(var form=Audio(wave)) {var result=await owner.PostAsync("/api/help/voice",form); result.EnsureSuccessStatusCode(); var json=await result.Content.ReadFromJsonAsync<JsonObject>(); if(json!["text"]!.GetValue<string>()!="sales today")throw new Exception("Transcription response missing.");}
        using(var form=Audio(new byte[64])) if((await owner.PostAsync("/api/help/voice",form)).StatusCode!=HttpStatusCode.BadRequest) throw new Exception("Invalid audio accepted.");
        using(var form=Audio(new byte[1_920_046])) if((await owner.PostAsync("/api/help/voice",form)).StatusCode!=HttpStatusCode.BadRequest) throw new Exception("Oversized recording accepted.");
        if(handler.Calls!=1)throw new Exception("Invalid recordings reached provider.");
        using var anonymous=configured.CreateClient(new(){AllowAutoRedirect=false});
        using(var form=Audio(wave)) if((await anonymous.PostAsync("/api/help/voice",form)).IsSuccessStatusCode) throw new Exception("Anonymous audio accepted.");
        Console.WriteLine("PASS: authenticated transcription, CSRF, WAV format, duration limit and provider isolation.");
    }
    private sealed class SpeechHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        { Calls++; if(request.RequestUri?.AbsolutePath!="/inference" || request.Content is not MultipartFormDataContent)throw new Exception("Wrong local speech protocol."); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{text="sales today"})}); }
    }
}
