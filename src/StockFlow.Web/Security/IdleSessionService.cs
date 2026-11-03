using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace StockFlow.Web.Security;
// Session deadlines are authoritative on the server. Ordinary requests do not extend them.
public sealed class IdleSessionService
{
    private readonly ISharedSessionStore store;
    private readonly IOptions<SessionSecurityOptions> options;
    // ASP.NET supplies these services when it creates this class.
    public IdleSessionService(ISharedSessionStore store, IOptions<SessionSecurityOptions> options)
    {
        this.store = store;
        this.options = options;
    }

    public const string ClaimType = "StockFlowSessionId";
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);
    public TimeSpan IdleTimeout => TimeSpan.FromMinutes(options.Value.EffectiveIdleMinutes);

    public static SessionOwner Owner(ClaimsPrincipal principal)
    {
        return new(long.Parse(principal.FindFirst("TenantId")!.Value),
int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value),
long.Parse(principal.FindFirst("SecurityVersion")!.Value));
    }

    public async Task<(string Id, SessionStatus Status)> StartAsync(ClaimsPrincipal principal, HttpContext context)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var agent = context.Request.Headers.UserAgent.ToString();
        var status = await store.CreateAsync(Owner(principal),
id,
context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
agent[..Math.Min(agent.Length, 512)]);
        return (id, status);
    }

    public Task<SessionStatus> ValidateAsync(ClaimsPrincipal principal, long? age = null)
    {
        var id = principal.FindFirst(ClaimType)?.Value;
        if (id is null || !Guid.TryParse(id, out _))
        {
            return Task.FromResult(new SessionStatus(0, 0, "SESSION_ENDED"));
        }

        return store.ValidateAsync(Owner(principal), id, age);
    }

    public Task<bool> EndAsync(ClaimsPrincipal principal, string reason = "LOGOUT")
    {
        var id = principal.FindFirst(ClaimType)?.Value;
        if (id is null)
        {
            return Task.FromResult(false);
        }
        else
        {
            return store.RevokeAsync(Owner(principal), id, reason);
        }
    }
}
