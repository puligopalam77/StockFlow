using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Web.Security;

public class TwoFactorService
{
    private readonly DbConnectionFactory factory;
    private readonly IDataProtectionProvider protection;
    private readonly BusinessScope scope;
    // ASP.NET supplies these services when it creates this class.
    public TwoFactorService(DbConnectionFactory factory, IDataProtectionProvider protection, BusinessScope scope)
    {
        this.factory = factory;
        this.protection = protection;
        this.scope = scope;
    }

    private IDataProtector Protector(int id)
    {
        return protection.CreateProtector("StockFlow.TOTP.v1", scope.TenantId.ToString(), id.ToString());
    }

    private sealed class State
    {
        public bool Enabled
        {
            get; set;
        }
        public string? Secret
        {
            get; set;
        }
        public string? Pending
        {
            get; set;
        }
        public DateTime? PendingUntil
        {
            get; set;
        }
        public DateTime? LockedUntil
        {
            get; set;
        }
        public long? LastStep
        {
            get; set;
        }
        public string Hash { get; set; } = "";
        public bool MustChangePassword
        {
            get; set;
        }
    }

    private const string Select = "SELECT must_change_password MustChangePassword,two_factor_enabled Enabled,two_factor_secret_encrypted Secret,two_factor_pending_encrypted Pending,two_factor_pending_until PendingUntil,two_factor_last_step LastStep,two_factor_locked_until LockedUntil,password_hash Hash FROM users WHERE user_id=@id AND is_active FOR UPDATE";
    private static string RecoveryHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().Replace("-", "").ToUpperInvariant())));
    }

    public async Task<string> Begin(int id, string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 128)
        {
            throw new ValidationException("Enter your current password.");
        }

        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        var state = await c.QuerySingleAsync<State>(Select, new
        {
            id
        }, t);
        if (state.Enabled)
        {
            throw new ValidationException("Authenticator is already enrolled.");
        }

        if (state.LockedUntil > DateTime.UtcNow)
        {
            throw new ValidationException("Too many attempts. Try again in 15 minutes.");
        }

        if (new PasswordHasher<string>().VerifyHashedPassword("", state.Hash, password) == PasswordVerificationResult.Failed)
        {
            await Failure(c, t, id);
            await t.CommitAsync();
            throw new ValidationException("Password is incorrect.");
        }

        var secret = RandomNumberGenerator.GetBytes(20);
        await c.ExecuteAsync("UPDATE users SET two_factor_pending_encrypted=@pending,two_factor_pending_until=now()+interval '10 minutes' WHERE user_id=@id",
new
{
    id,
    pending = Protector(id).Protect(Convert.ToBase64String(secret))
},
t);
        await t.CommitAsync();
        return TotpCodes.Base32(secret);
    }

    public async Task<(bool Success, string[] RecoveryCodes)> Verify(int id, string code)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        var state = await c.QuerySingleAsync<State>(Select, new
        {
            id
        }, t);
        if (state.LockedUntil > DateTime.UtcNow)
        {
            throw new ValidationException("Too many attempts. Try again in 15 minutes.");
        }

        var encrypted = state.Enabled ? state.Secret : state.PendingUntil > DateTime.UtcNow ? state.Pending : null;
        long? matched = null;
        bool recovery = false;
        if (encrypted is not null && code.Trim().Length == 6)
        {
            try
            {
                matched = TotpCodes.Verify(Convert.FromBase64String(Protector(id).Unprotect(encrypted)),
code.Trim(),
DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30,
state.LastStep);
            }
            catch (CryptographicException)
            { /* A moved/rotated key ring cannot bypass MFA; recovery codes remain available. */
            }
        }

        if (state.Enabled && matched is null && code.Length <= 64)
        {
            recovery = await c.ExecuteAsync("UPDATE two_factor_recovery_codes SET used_at=now() WHERE user_id=@id AND code_hash=@hash AND used_at IS NULL",
new
{
    id,
    hash = RecoveryHash(code)
},
t) == 1;
        }

        if (matched is null && !recovery)
        {
            await Failure(c, t, id);
            await t.CommitAsync();
            return (false, []);
        }

        string[] codes = [];
        if (!state.Enabled)
        {
            codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
            await c.ExecuteAsync("DELETE FROM two_factor_recovery_codes WHERE user_id=@id", new
            {
                id
            }, t);
            foreach (var raw in codes)
            {
                await c.ExecuteAsync("INSERT INTO two_factor_recovery_codes(user_id,code_hash) VALUES(@id,@hash)", new
                {
                    id,
                    hash = RecoveryHash(raw)
                }, t);
            }

            await c.ExecuteAsync("UPDATE users SET two_factor_enabled=true,two_factor_secret_encrypted=two_factor_pending_encrypted,two_factor_pending_encrypted=NULL,two_factor_pending_until=NULL,two_factor_enabled_at=now(),security_version=security_version+1 WHERE user_id=@id",
new
{
    id
},
t);
        }

        await c.ExecuteAsync("UPDATE users SET two_factor_last_step=COALESCE(@matched,two_factor_last_step),two_factor_failures=0,two_factor_locked_until=NULL,last_login_at=now() WHERE user_id=@id; INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(@id,'Security',@action,'users',@id)",
new
{
    id,
    matched,
    action = state.Enabled ? (recovery ? "RECOVERY_CODE_USED" : "TWO_FACTOR_VERIFIED") : "TWO_FACTOR_ENABLED"
},
t);
        if (!state.MustChangePassword)
        {
            await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,record_id) VALUES(@id,'Security','LOGIN_SUCCEEDED',@id)",
new
{
    id
},
t);
        }

        await t.CommitAsync();
        return (true, codes);
    }

    private static Task Failure(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction t, int id)
    {
        return c.ExecuteAsync("UPDATE users SET two_factor_failures=CASE WHEN two_factor_locked_until<=now() THEN 1 ELSE two_factor_failures+1 END,two_factor_locked_until=CASE WHEN (CASE WHEN two_factor_locked_until<=now() THEN 1 ELSE two_factor_failures+1 END)>=5 THEN now()+interval '15 minutes' ELSE NULL END WHERE user_id=@id; INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(@id,'Security','TWO_FACTOR_FAILED','users',@id)",
new
{
    id
},
t);
    }
}
