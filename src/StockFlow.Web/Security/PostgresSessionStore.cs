using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Web.Security;

public sealed class PostgresSessionStore : ISharedSessionStore
{
    private readonly DbConnectionFactory factory;
    private readonly IOptions<SessionSecurityOptions> options;
    // ASP.NET supplies these services when it creates this class.
    public PostgresSessionStore(DbConnectionFactory factory, IOptions<SessionSecurityOptions> options)
    {
        this.factory = factory;
        this.options = options;
    }

    private sealed class Row
    {
        public Guid SessionId
        {
            get; set;
        }
        public long TenantId
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }
        public long SecurityVersion
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public DateTime LastActivityAt
        {
            get; set;
        }
        public DateTime IdleExpiresAt
        {
            get; set;
        }
        public DateTime AbsoluteExpiresAt
        {
            get; set;
        }
        public DateTime? RevokedAt
        {
            get; set;
        }
        public string? RevocationReason
        {
            get; set;
        }
        public string IpAddress { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public string AuditReference { get; set; } = "";
        public DateTime ServerNow
        {
            get; set;
        }
        public long CurrentVersion
        {
            get; set;
        }
        public bool UserActive
        {
            get; set;
        }
    }

    private const string Select = """
        SELECT s.session_id SessionId,s.tenant_id TenantId,s.user_id UserId,s.security_version SecurityVersion,
        s.created_at CreatedAt,s.last_activity_at LastActivityAt,s.idle_expires_at IdleExpiresAt,s.absolute_expires_at AbsoluteExpiresAt,
        s.revoked_at RevokedAt,s.revocation_reason RevocationReason,s.ip_address IpAddress,s.user_agent UserAgent,s.audit_reference AuditReference,
        clock_timestamp() ServerNow,u.security_version CurrentVersion,u.is_active AND r.is_active UserActive
        FROM auth_sessions s JOIN users u USING(tenant_id,user_id) JOIN roles r ON r.role_id=u.role_id
        """;
    private static SessionStatus Status(Row row)
    {
        return new((long)(row.IdleExpiresAt - row.ServerNow).TotalMilliseconds,
(long)(row.AbsoluteExpiresAt - row.ServerNow).TotalMilliseconds);
    }

    public async Task<SessionStatus> CreateAsync(SessionOwner owner, string id, string ip, string device)
    {
        await using var c = factory.CreateSecurityConnection(owner.TenantId, owner.UserId);
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        var reference = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
        var count = await c.ExecuteAsync("""
            INSERT INTO auth_sessions(session_id,tenant_id,user_id,security_version,idle_expires_at,absolute_expires_at,ip_address,user_agent,audit_reference)
            SELECT @id,u.tenant_id,u.user_id,u.security_version,now()+make_interval(mins=>@idle),now()+make_interval(hours=>@absolute),@ip,@device,@reference
            FROM users u JOIN roles r ON r.role_id=u.role_id WHERE u.user_id=@UserId AND u.tenant_id=@TenantId
            AND u.is_active AND r.is_active AND u.security_version=@SecurityVersion
            """,
new
{
    id = Guid.Parse(id),
    owner.TenantId,
    owner.UserId,
    owner.SecurityVersion,
    idle = options.Value.EffectiveIdleMinutes,
    absolute = options.Value.AbsoluteHours,
    ip,
    device,
    reference
},
t);
        if (count != 1)
        {
            throw new UnauthorizedAccessException("This account changed. Sign in again.");
        }

        var row = await c.QuerySingleAsync<Row>(Select + " WHERE s.session_id=@id", new
        {
            id = Guid.Parse(id)
        }, t);
        await Audit(c, t, row, "SESSION_STARTED", "LOGIN");
        await t.CommitAsync();
        return Status(row);
    }

    public async Task<SessionStatus> ValidateAsync(SessionOwner owner, string id, long? activityAgeMilliseconds = null)
    {
        await using var c = factory.CreateSecurityConnection(owner.TenantId, owner.UserId);
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        var row = await c.QuerySingleOrDefaultAsync<Row>(Select + " WHERE s.session_id=@id FOR UPDATE OF s", new
        {
            id = Guid.Parse(id)
        }, t);
        if (row is null)
        {
            return new(0, 0, "SESSION_ENDED");
        }

        if (row.RevokedAt is not null)
        {
            return new(0, 0, row.RevocationReason);
        }

        var reason = row.AbsoluteExpiresAt <= row.ServerNow ? "ABSOLUTE_TIMEOUT" : row.IdleExpiresAt <= row.ServerNow ? "IDLE_TIMEOUT" : !row.UserActive || row.CurrentVersion != owner.SecurityVersion || row.SecurityVersion != owner.SecurityVersion ? "SECURITY_CHANGED" : null;
        if (reason is not null)
        {
            await End(c, t, row, reason);
            await t.CommitAsync();
            return new(0, 0, reason);
        }

        if (activityAgeMilliseconds.HasValue)
        {
            var activity = row.ServerNow - TimeSpan.FromMilliseconds(activityAgeMilliseconds.Value);
            var proposed = activity + TimeSpan.FromMinutes(options.Value.EffectiveIdleMinutes);
            row.LastActivityAt = activity > row.LastActivityAt ? activity : row.LastActivityAt;
            row.IdleExpiresAt = proposed > row.IdleExpiresAt ? proposed : row.IdleExpiresAt;
            if (row.IdleExpiresAt > row.AbsoluteExpiresAt)
            {
                row.IdleExpiresAt = row.AbsoluteExpiresAt;
            }

            await c.ExecuteAsync("UPDATE auth_sessions SET last_activity_at=@LastActivityAt,idle_expires_at=@IdleExpiresAt WHERE session_id=@SessionId",
row,
t);
        }

        await t.CommitAsync();
        return Status(row);
    }

    public async Task<bool> RevokeAsync(SessionOwner owner, string id, string reason)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return false;
        }

        await using var c = factory.CreateSecurityConnection(owner.TenantId, owner.UserId);
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        var row = await c.QuerySingleOrDefaultAsync<Row>(Select + " WHERE s.session_id=@guid AND s.revoked_at IS NULL FOR UPDATE OF s", new
        {
            guid
        }, t);
        if (row is null)
        {
            return false;
        }

        // A browser delayed by sleep cannot relabel an already-expired session as a manual logout.
        if (row.AbsoluteExpiresAt <= row.ServerNow)
        {
            reason = "ABSOLUTE_TIMEOUT";
        }
        else if (row.IdleExpiresAt <= row.ServerNow)
        {
            reason = "IDLE_TIMEOUT";
        }

        await End(c, t, row, reason);
        await t.CommitAsync();
        return true;
    }

    private static async Task End(NpgsqlConnection c, NpgsqlTransaction t, Row row, string reason)
    {
        await c.ExecuteAsync("UPDATE auth_sessions SET revoked_at=clock_timestamp(),revocation_reason=@reason WHERE session_id=@SessionId",
new
{
    row.SessionId,
    reason
},
t);
        await Audit(c, t, row, "SESSION_ENDED", reason);
    }

    private static Task Audit(NpgsqlConnection c, NpgsqlTransaction t, Row row, string action, string reason)
    {
        return c.ExecuteAsync("""
        INSERT INTO audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,reason,new_values)
        VALUES(@TenantId,@UserId,'Security',@action,'auth_sessions',@UserId,@reason,CAST(@details AS jsonb))
        """,
new
{
    row.TenantId,
    row.UserId,
    action,
    reason,
    details = JsonSerializer.Serialize(new
    {
        sessionReference = row.AuditReference,
        ipAddress = row.IpAddress,
        device = row.UserAgent,
        reason
    })
},
t);
    }

    public async Task<IReadOnlyList<LoginSession>> ListAsync(SessionOwner owner)
    {
        await using var c = factory.CreateSecurityConnection(owner.TenantId, owner.UserId);
        var rows = await c.QueryAsync<Row>(Select + " WHERE s.revoked_at IS NULL AND s.idle_expires_at>now() AND s.absolute_expires_at>now() AND u.is_active AND r.is_active AND s.security_version=u.security_version ORDER BY s.created_at DESC LIMIT 100");
        return rows.Select(row => new LoginSession(row.SessionId.ToString("N").ToUpperInvariant(),
row.UserAgent,
row.IpAddress,
new(row.CreatedAt),
new(row.LastActivityAt),
new(row.AbsoluteExpiresAt)))
.ToArray();
    }

    public async Task<int> TakeLoginAttemptAsync(string bucket, int limit, int seconds)
    {
        await using var c = factory.CreateSecurityConnection(0, 0);
        return await c.ExecuteScalarAsync<int>("SELECT sf_take_login_attempt(@bucket,@limit,@seconds)", new
        {
            bucket,
            limit,
            seconds
        });
    }

    public async Task SweepAsync()
    {
        await using var c = factory.CreateSecurityConnection(0, 0);
        await c.ExecuteAsync("SELECT sf_expire_auth_sessions()");
    }
}
