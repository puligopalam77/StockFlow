using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace StockFlow.Web.Security;

public sealed class LoginThrottleMiddleware
{
    private readonly RequestDelegate next;
    // ASP.NET supplies these services when it creates this class.
    public LoginThrottleMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISharedSessionStore store, IOptions<SessionSecurityOptions> settings)
    {
        var path = context.Request.Path.Value;
        if (!HttpMethods.IsPost(context.Request.Method) || !(string.Equals(path, "/Account/Login", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "/Account/TwoFactor", StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var limits = settings.Value;
        // RemoteIpAddress is authoritative. Do not trust caller-supplied forwarding headers.
        var retry = await store.TakeLoginAttemptAsync(Hash("global-login"), limits.GlobalLoginAttemptsPerMinute, 60);
        if (retry == 0)
        {
            retry = await store.TakeLoginAttemptAsync(Hash("login-ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown")),
limits.LoginAttemptsPerIp,
limits.LoginWindowMinutes * 60);
        }

        if (retry > 0)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers.RetryAfter = retry.ToString();
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync("Too many sign-in attempts. Please wait and try again.");
            return;
        }

        await next(context);
    }
}
