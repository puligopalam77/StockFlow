namespace StockFlow.Mobile;

// One HTTPS origin. No credentials, paths or tokens are stored as configuration.
public sealed class ServerAddress
{
    public Uri Origin { get; }
    private ServerAddress(Uri origin) => Origin = origin;
    public static bool TryParse(string? value, out ServerAddress? address)
    {
        address = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.IsLoopback || uri.HostNameType == UriHostNameType.Unknown)
            return false;
        address = new ServerAddress(uri);
        return true;
    }
    public bool Contains(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
        && string.Equals(uri.IdnHost, Origin.IdnHost, StringComparison.OrdinalIgnoreCase)
        && uri.Port == Origin.Port;
    public string SignInUrl => new Uri(Origin, "Account/Login").AbsoluteUri;
}
