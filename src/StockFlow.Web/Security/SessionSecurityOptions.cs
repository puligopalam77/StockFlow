namespace StockFlow.Web.Security;

public sealed class SessionSecurityOptions
{
    // Zero disables inactivity expiry; the absolute login lifetime still applies.
    public int IdleMinutes { get; set; } = 0;
    public int EffectiveIdleMinutes => IdleMinutes == 0 ? AbsoluteHours * 60 : IdleMinutes;
    public int AbsoluteHours { get; set; } = 8;
    public int LoginAttemptsPerIp { get; set; } = 30;
    public int LoginWindowMinutes { get; set; } = 10;
    public int GlobalLoginAttemptsPerMinute { get; set; } = 300;
    public string? KeyCertificatePath
    {
        get; set;
    }
    public string? KeyCertificatePassword
    {
        get; set;
    }
    public string[] TrustedProxies { get; set; } = [];
}

public sealed record SessionOwner(long TenantId, int UserId, long SecurityVersion);
public sealed record SessionStatus(long RemainingMilliseconds, long AbsoluteRemainingMilliseconds, string? Reason = null)
{
    public bool Active => RemainingMilliseconds > 0;
}

public sealed record LoginSession(string SessionId,
string Device,
string IpAddress,
DateTimeOffset CreatedAt,
DateTimeOffset LastActivityAt,
DateTimeOffset AbsoluteExpiresAt);
public interface ISharedSessionStore
{
    Task<SessionStatus> CreateAsync(SessionOwner owner, string id, string ip, string device);
    Task<SessionStatus> ValidateAsync(SessionOwner owner, string id, long? activityAgeMilliseconds = null);
    Task<bool> RevokeAsync(SessionOwner owner, string id, string reason);
    Task<IReadOnlyList<LoginSession>> ListAsync(SessionOwner owner);
    Task<int> TakeLoginAttemptAsync(string bucket, int limit, int seconds);
    Task SweepAsync();
}
