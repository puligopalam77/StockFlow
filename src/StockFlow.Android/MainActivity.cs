using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Android.Net.Http;
using Uri = Android.Net.Uri;

namespace StockFlow.Mobile;

[Activity(Label = "StockFlow", MainLauncher = true, Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed partial class MainActivity : Activity
{
    private WebView? web;
    private LinearLayout? root;
    private LinearLayout? notice;
    private ServerAddress? server;
    private IValueCallback? files;
    private Android.Window.IOnBackInvokedCallback? backCallback;
    private const int ChooseFiles = 51;
    private ISharedPreferences Preferences => GetSharedPreferences("connection", FileCreationMode.Private)!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        // Fit content inside status/navigation bars, including Android 15+ edge-to-edge.
        root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Android.Graphics.Color.White);
        root.SetOnApplyWindowInsetsListener(new InsetsListener());
        SetContentView(root);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) {
            backCallback = new BackCallback(this);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, backCallback);
        }
        if (ServerAddress.TryParse(Preferences.GetString("origin", null), out server))
            OpenApp(savedInstanceState);
        else ShowConnection();
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);
    private Button Button(string text, Action action)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => action();
        return button;
    }
    private void ShowConnection()
    {
        root!.RemoveAllViews();
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(24), Dp(24), Dp(24), Dp(24));
        form.AddView(new TextView(this) { Text = "Connect to StockFlow", TextSize = 24 });
        form.AddView(new TextView(this) { Text = "Enter your shop’s hosted HTTPS address. Then sign in with your existing StockFlow account.", TextSize = 16 });
        var input = new EditText(this) { Hint = "https://your-shop.example",
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationUri };
        input.SetSingleLine(true);
        input.SetSelectAllOnFocus(true);
        form.AddView(input);
        var error = new TextView(this);
        error.SetTextColor(Android.Graphics.Color.Rgb(180, 35, 24));
        form.AddView(error);
        form.AddView(Button("Connect", () => {
            if (!ServerAddress.TryParse(input.Text, out var address)) {
                error.Text = "Use the HTTPS server address only, without a page path. Localhost refers to this phone.";
                return;
            }
            server = address;
            Preferences.Edit()!.PutString("origin", server!.Origin.AbsoluteUri)!.Apply();
            OpenApp(null);
        }));
        var scroll = new ScrollView(this); scroll.AddView(form); root.AddView(scroll);
    }

    private void OpenApp(Bundle? state)
    {
        root!.RemoveAllViews();
        var toolbar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        toolbar.AddView(Button("Back", GoBack));
        toolbar.AddView(Button("Connection", ConfirmConnectionChange));
        root.AddView(toolbar);
        notice = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        root.AddView(notice);
        web = new WebView(this);
        var settings = web.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.AllowFileAccess = false;
        settings.AllowContentAccess = true; // Android document picker results only.
        settings.MixedContentMode = MixedContentHandling.NeverAllow;
        settings.SetSupportMultipleWindows(false);
        settings.JavaScriptCanOpenWindowsAutomatically = false;
        settings.MediaPlaybackRequiresUserGesture = true;
        CookieManager.Instance!.SetAcceptCookie(true);
        CookieManager.Instance.SetAcceptThirdPartyCookies(web, false);
        web.SetWebViewClient(new AppClient(this));
        web.SetWebChromeClient(new AppChrome(this));
        web.SetDownloadListener(new AppDownloads(this));
        settings.UserAgentString += " StockFlowAndroid/1.1";
        root.AddView(web, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        // Restore history only; do not manufacture credentials or replay failed operations.
        if (state is null || web.RestoreState(state) is null || !server!.Contains(web.Url))
            web.LoadUrl(server!.SignInUrl);
    }

    private void ConfirmConnectionChange() => new AlertDialog.Builder(this)!
        .SetTitle("Change shop connection?")!
        .SetMessage("This clears this app’s local login and saved drafts. Finish or save your current work first. Browser sessions are separate.")!
        .SetNegativeButton("Cancel", (_, _) => { })!
        .SetPositiveButton("Change connection", (_, _) => {
            ResetDeviceFeatures(); web?.StopLoading(); web?.Destroy(); web = null;
            files?.OnReceiveValue(null); files = null;
            WebStorage.Instance!.DeleteAllData();
            CookieManager.Instance!.RemoveAllCookies(new ClearedCookies(this));
        })!.Show();

    private sealed class ClearedCookies(MainActivity owner) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) => owner.RunOnUiThread(() => {
            CookieManager.Instance!.Flush(); owner.Preferences.Edit()!.Remove("origin")!.Apply();
            owner.server = null; owner.ShowConnection();
        });
    }

    private void GoBack()
    {
        if (web?.CanGoBack() == true) web.GoBack();
        else new AlertDialog.Builder(this)!.SetMessage("Close StockFlow? Save your current work first.")!
            .SetNegativeButton("Stay", (_, _) => { })!.SetPositiveButton("Close", (_, _) => Finish())!.Show();
    }
    public override void OnBackPressed() => GoBack();
    private sealed class BackCallback(MainActivity owner) : Java.Lang.Object, Android.Window.IOnBackInvokedCallback
    { public void OnBackInvoked() => owner.GoBack(); }

    private void ConnectionError()
    {
        notice!.RemoveAllViews(); notice.Visibility = ViewStates.Visible;
        notice.AddView(new TextView(this) { Text = "Unable to load StockFlow. Check your connection. If you were saving, check the result before submitting again.", TextSize = 16 });
        notice.AddView(Button("Dismiss", () => notice.Visibility = ViewStates.Gone));
        notice.AddView(Button("Open sign-in", () => {
            notice.Visibility = ViewStates.Gone; web!.LoadUrl(server!.SignInUrl);
        }));
    }

    private bool Navigate(string? url)
    {
        if (server!.Contains(url)) return false;
        if (System.Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.UserInfo.Length == 0 && uri.Scheme is "https" or "mailto" or "tel") {
            try { StartActivity(new Intent(Intent.ActionView, Uri.Parse(url))); }
            catch (ActivityNotFoundException) { Toast.MakeText(this, "No app available to open this link.", ToastLength.Short)!.Show(); }
        }
        return true;
    }

    private sealed class AppClient(MainActivity owner) : WebViewClient
    {
        public override void OnPageStarted(WebView? view, string? url, Android.Graphics.Bitmap? favicon)
        { owner.ResetDeviceFeatures(); }
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
            => request?.IsForMainFrame == true && owner.Navigate(request.Url?.ToString());
        public override void OnReceivedSslError(WebView? view, SslErrorHandler? handler, SslError? error)
        { handler?.Cancel(); owner.ConnectionError(); }
        public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
        { if (request?.IsForMainFrame == true) owner.ConnectionError(); }
        public override void OnReceivedHttpError(WebView? view, IWebResourceRequest? request, WebResourceResponse? response)
        { if (request?.IsForMainFrame == true && response?.StatusCode >= 500) owner.ConnectionError(); }
        public override void OnPageFinished(WebView? view, string? url)
        { CookieManager.Instance!.Flush(); owner.ConnectDeviceFeatures(); }
    }

    private sealed class AppChrome(MainActivity owner) : WebChromeClient
    {
        public override bool OnShowFileChooser(WebView? view, IValueCallback? callback, FileChooserParams? parameters)
        {
            owner.files?.OnReceiveValue(null); owner.files = callback;
            if (!owner.server!.Contains(view?.Url)) { callback?.OnReceiveValue(null); owner.files = null; return true; }
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable); intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraAllowMultiple, parameters?.Mode == ChromeFileChooserMode.OpenMultiple);
            var types = parameters?.GetAcceptTypes()?.SelectMany(t => t.Split(','))
                .Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.StartsWith('.') ? MimeTypeMap.Singleton?.GetMimeTypeFromExtension(t[1..].ToLowerInvariant()) : t)
                .OfType<string>().Where(t => t.Contains('/')).Distinct().ToArray();
            if (types?.Length > 0) intent.PutExtra(Intent.ExtraMimeTypes, types);
            try { owner.StartActivityForResult(intent, ChooseFiles); }
            catch (ActivityNotFoundException) { callback?.OnReceiveValue(null); owner.files = null; }
            return true;
        }
        public override void OnPermissionRequest(PermissionRequest? request) => owner.RequestMedia(request);
        public override void OnPermissionRequestCanceled(PermissionRequest? request)
        { if (owner.mediaRequest == request) owner.mediaRequest = null; }
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == SaveDownload) { CompleteDownload(resultCode, data); return; }
        if (requestCode != ChooseFiles) return;
        var selected = WebChromeClient.FileChooserParams.ParseResult((int)resultCode, data);
        // Never let a picker return file:// paths into the app's private files.
        if (selected?.All(uri => uri.Scheme == "content") == true) {
            using var values = Android.Runtime.JavaArray<Uri>.FromArray(selected);
            files?.OnReceiveValue(values);
        } else files?.OnReceiveValue(null);
        files = null;
    }
    protected override void OnSaveInstanceState(Bundle outState)
    { web?.SaveState(outState); base.OnSaveInstanceState(outState); }
    protected override void OnPause()
    {
        if (server?.Contains(web?.Url) == true)
            web?.EvaluateJavascript("document.dispatchEvent(new Event('stockflow:device-paused'));", null);
        CookieManager.Instance!.Flush(); web?.OnPause(); base.OnPause();
    }
    protected override void OnResume()
    {
        base.OnResume(); web?.OnResume();
        // Re-check the existing idle deadline after backgrounding; no keep-alive is sent.
        if (server?.Contains(web?.Url) == true)
            web?.EvaluateJavascript("document.dispatchEvent(new Event('visibilitychange'));", null);
    }
    protected override void OnDestroy()
    {
        ResetDeviceFeatures();
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && backCallback is not null)
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(backCallback);
        files?.OnReceiveValue(null); files = null; web?.Destroy(); web = null; base.OnDestroy();
    }

    private sealed class InsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime());
                view?.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            } else view?.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop, insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
            return insets;
        }
    }
}
