using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Print;
using Android.Webkit;
using System.Text.Json;
using Uri = Android.Net.Uri;

namespace StockFlow.Mobile;

public sealed partial class MainActivity
{
    private const int MediaPermissions = 61, SaveDownload = 62;
    private PermissionRequest? mediaRequest;
    private string[] mediaResources = [];
    private WebMessagePort? devicePort;
    private MemoryStream? download;
    private string downloadName = "stockflow-document", downloadMime = "application/octet-stream";
    private CancellationTokenSource? downloadCancellation;
    private int deviceGeneration;
    private int saveGeneration;
    private bool printActive;

    private void Say(string message) => Android.Widget.Toast.MakeText(this, message, Android.Widget.ToastLength.Long)!.Show();
    private void RequestMedia(PermissionRequest? request)
    {
        if (request is null) return;
        var resources = request.GetResources() ?? [];
        if (mediaRequest is not null || server?.Contains(request.Origin?.ToString()) != true
            || server.Contains(web?.Url) != true || resources.Length == 0
            || resources.Any(r => r != PermissionRequest.ResourceAudioCapture && r != PermissionRequest.ResourceVideoCapture)) {
            request.Deny(); return;
        }
        mediaRequest = request; mediaResources = resources;
        var permissions = resources.Select(r => r == PermissionRequest.ResourceAudioCapture
            ? Android.Manifest.Permission.RecordAudio : Android.Manifest.Permission.Camera)
            .Where(p => CheckSelfPermission(p) != Permission.Granted).Distinct().ToArray();
        if (permissions.Length == 0) CompleteMediaPermission();
        else RequestPermissions(permissions, MediaPermissions);
    }
    private void CompleteMediaPermission()
    {
        var request = mediaRequest; mediaRequest = null;
        if (request is null) return;
        var allowed = server?.Contains(request.Origin?.ToString()) == true && server.Contains(web?.Url)
            && mediaResources.All(r => CheckSelfPermission(r == PermissionRequest.ResourceAudioCapture
                ? Android.Manifest.Permission.RecordAudio : Android.Manifest.Permission.Camera) == Permission.Granted);
        if (allowed) request.Grant(mediaResources);
        else { request.Deny(); Say("Camera or microphone permission was denied. You can enable it in Android app settings."); }
    }
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == MediaPermissions) CompleteMediaPermission();
    }
    private void ResetDeviceFeatures()
    {
        deviceGeneration++;
        mediaRequest?.Deny(); mediaRequest = null;
        devicePort?.Close(); devicePort = null;
        downloadCancellation?.Cancel(); downloadCancellation?.Dispose(); downloadCancellation = null;
        download?.Dispose(); download = null;
    }
    private void ConnectDeviceFeatures()
    {
        if (web is null || devicePort is not null || server?.Contains(web.Url) != true) return;
        using var reader = new StreamReader(Assets!.Open("device-client.js"));
        web.EvaluateJavascript(reader.ReadToEnd(), new ScriptReady(this, deviceGeneration));
    }
    private sealed class ScriptReady(MainActivity owner, int generation) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            if (generation != owner.deviceGeneration || owner.web is null || owner.server?.Contains(owner.web.Url) != true) return;
            var ports = owner.web.CreateWebMessageChannel();
            owner.devicePort = ports[0];
            ports[0].SetWebMessageCallback(new Messages(owner, generation));
            owner.web.PostWebMessage(new WebMessage("stockflow-device-ready", [ports[1]]), Uri.Parse(owner.server.Origin.GetLeftPart(UriPartial.Authority))!);
        }
    }
    private sealed class Messages(MainActivity owner, int generation) : WebMessagePort.WebMessageCallback
    {
        public override void OnMessage(WebMessagePort? port, WebMessage? message)
        {
            if (generation != owner.deviceGeneration || owner.server?.Contains(owner.web?.Url) != true) return;
            try {
                if (message?.Data is not { Length: <= 100000 } data) return;
                using var document = JsonDocument.Parse(data);
                var json = document.RootElement;
                switch (json.GetProperty("action").GetString()) {
                    case "print": owner.PrintPage(); break;
                    case "download-start":
                        if (owner.download is not null) { owner.Reply("busy"); break; }
                        owner.download = new MemoryStream();
                        owner.downloadName = DevicePolicy.FileName(json.GetProperty("name").GetString());
                        owner.downloadMime = DevicePolicy.Mime(json.GetProperty("mime").GetString());
                        owner.Reply("ready"); break;
                    case "download-chunk":
                        if (owner.download is null) break;
                        var bytes = Convert.FromBase64String(json.GetProperty("data").GetString()!);
                        if (owner.download.Length + bytes.Length > DevicePolicy.MaxDownloadBytes) throw new InvalidDataException();
                        owner.download.Write(bytes); owner.Reply("next"); break;
                    case "download-end": if (owner.download is not null) owner.PickDownloadDestination(); break;
                    case "download-cancel": owner.download?.Dispose(); owner.download = null; break;
                }
            } catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or InvalidDataException) {
                owner.download?.Dispose(); owner.download = null; owner.Reply("error"); owner.Say("Unable to save this file. Maximum file size is 20 MB.");
            }
        }
    }
    private void Reply(string status) => devicePort?.PostMessage(new WebMessage(status));
    private void PickDownloadDestination()
    {
        saveGeneration = deviceGeneration;
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable); intent.SetType(downloadMime);
        intent.PutExtra(Intent.ExtraTitle, downloadName);
        try { StartActivityForResult(intent, SaveDownload); }
        catch (ActivityNotFoundException) { download?.Dispose(); download = null; Reply("error"); Say("No file-saving app is available."); }
    }
    private void CompleteDownload(Result result, Intent? data)
    {
        if (saveGeneration != deviceGeneration) return;
        try {
            if (result == Result.Ok && data?.Data?.Scheme == "content" && download is not null) {
                using var output = ContentResolver!.OpenOutputStream(data.Data, "wt") ?? throw new IOException();
                download.Position = 0; download.CopyTo(output); Say("File saved."); Reply("saved");
            } else Reply("cancelled");
        } catch (Exception error) when (error is IOException or Java.Lang.SecurityException) { Say("Unable to save the file. Try another folder."); Reply("error"); }
        finally { download?.Dispose(); download = null; }
    }
    private sealed class AppDownloads(MainActivity owner) : Java.Lang.Object, IDownloadListener
    {
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength)
            => _ = owner.DownloadUrl(url, userAgent, contentDisposition, mimetype);
    }
    private async Task DownloadUrl(string? url, string? userAgent, string? disposition, string? mime)
    {
        if (server?.Contains(url) != true) { Say("Only files from your StockFlow server can be saved."); return; }
        if (download is not null) { Say("Finish the current download first."); return; }
        download = new MemoryStream(); downloadCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var generation = deviceGeneration; var buffer = download; var token = downloadCancellation.Token;
        try {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Cookie", CookieManager.Instance!.GetCookie(url));
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type == "text/html") throw new IOException("Sign in again before downloading.");
            if (response.Content.Headers.ContentLength > DevicePolicy.MaxDownloadBytes) throw new IOException("File exceeds 20 MB.");
            using var input = await response.Content.ReadAsStreamAsync(token);
            var chunk = new byte[65536]; int read;
            while ((read = await input.ReadAsync(chunk, token)) != 0) {
                if (buffer.Length + read > DevicePolicy.MaxDownloadBytes) throw new IOException("File exceeds 20 MB.");
                buffer.Write(chunk, 0, read);
            }
            if (generation != deviceGeneration) return;
            downloadName = DevicePolicy.FileName(response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? URLUtil.GuessFileName(url, disposition, mime));
            downloadMime = DevicePolicy.Mime(type ?? mime); PickDownloadDestination();
        } catch (Exception error) when (error is HttpRequestException or IOException or System.OperationCanceledException or ObjectDisposedException) {
            if (generation == deviceGeneration) { download?.Dispose(); download = null; Say("Download failed. Check your connection and sign-in, then try again."); }
        }
    }
    private void PrintPage()
    {
        if (web is null || server?.Contains(web.Url) != true || printActive) { Reply("error"); return; }
        printActive = true;
        web.EvaluateJavascript("(()=>{window.dispatchEvent(new Event('beforeprint'));return document.body.classList.contains('invoice-thermal')?'receipt':document.body.classList.contains('label-thermal')?'label':'a4';})()", new PrintReady(this, deviceGeneration));
    }
    private sealed class PrintReady(MainActivity owner, int generation) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            if (generation != owner.deviceGeneration || owner.web is null) { owner.printActive = false; return; }
            var manager = (PrintManager?)owner.GetSystemService(PrintService);
            if (manager is null) { owner.printActive = false; owner.Reply("error"); owner.Say("Android printing is unavailable."); return; }
            var title = DevicePolicy.FileName(owner.web.Title);
            var media = value?.ToString() switch {
                "\"receipt\"" => new PrintAttributes.MediaSize("stockflow-receipt", "80 mm receipt", 3150, 11693),
                "\"label\"" => new PrintAttributes.MediaSize("stockflow-label", "63 x 38 mm label", 2480, 1496),
                _ => PrintAttributes.MediaSize.IsoA4!
            };
            try {
                manager.Print(title, new PrintedDocument(owner, owner.web.CreatePrintDocumentAdapter(title), generation),
                    new PrintAttributes.Builder()!.SetMediaSize(media)!.SetMinMargins(PrintAttributes.Margins.NoMargins!)!.SetColorMode(PrintColorMode.Color)!.Build()!);
            } catch (Java.Lang.IllegalStateException) {
                owner.printActive = false;
                owner.web.EvaluateJavascript("window.dispatchEvent(new Event('afterprint'));", null);
                owner.Reply("error"); owner.Say("Unable to open printing. Return to the document and try again.");
            }
        }
    }
    private sealed class PrintedDocument(MainActivity owner, PrintDocumentAdapter inner, int generation) : PrintDocumentAdapter
    {
        public override void OnStart() => inner.OnStart();
        public override void OnLayout(PrintAttributes? oldAttributes, PrintAttributes? newAttributes, CancellationSignal? signal, LayoutResultCallback? callback, Bundle? extras)
            => inner.OnLayout(oldAttributes, newAttributes, signal, callback, extras);
        public override void OnWrite(PageRange[]? pages, ParcelFileDescriptor? destination, CancellationSignal? signal, WriteResultCallback? callback)
            => inner.OnWrite(pages, destination, signal, callback);
        public override void OnFinish()
        { inner.OnFinish(); owner.printActive = false; if (generation == owner.deviceGeneration) { owner.web?.EvaluateJavascript("window.dispatchEvent(new Event('afterprint'));", null); owner.Reply("printed"); } }
    }
}
