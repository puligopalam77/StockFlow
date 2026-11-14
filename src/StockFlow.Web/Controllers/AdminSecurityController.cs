using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/admin")]
public class AdminSecurityController : ControllerBase
{
    private readonly DbConnectionFactory factory;
    private readonly PermissionContext access;
    private readonly IBranchRepository branches;
    private readonly TwoFactorService twoFactor;
    // ASP.NET supplies these services when it creates this class.
    public AdminSecurityController(DbConnectionFactory factory,
PermissionContext access,
IBranchRepository branches,
TwoFactorService twoFactor)
    {
        this.factory = factory;
        this.access = access;
        this.branches = branches;
        this.twoFactor = twoFactor;
    }

    public class Policy
    {
        [Range(8, 128)]
        public int MinimumPasswordLength { get; set; } = 8;
        public bool RequireTwoFactor
        {
            get; set;
        }

        [Range(3, 10)]
        public int MaxLoginAttempts { get; set; } = 5;

        [Range(5, 60)]
        public int LockoutMinutes { get; set; } = 15;
    }

    public class RolePolicy
    {
        public bool RequiresTwoFactor
        {
            get; set;
        }
    }

    public class NewRole
    {
        [Required, StringLength(50, MinimumLength = 2)]
        public string RoleName { get; set; } = "";

        [StringLength(200)]
        public string? Description
        {
            get; set;
        }
        public bool RequiresTwoFactor
        {
            get; set;
        }
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole(NewRole r)
    {
        r.RoleName = r.RoleName.Trim();
        if(new[]{"admin","owner","main owner"}.Contains(r.RoleName.ToLowerInvariant()))throw new ValidationException("Owner and system identities are reserved.");
        if (r.RoleName.Length < 2 || !r.RoleName.Any(char.IsLetterOrDigit))
        {
            throw new ValidationException("Enter a meaningful role name.");
        }

        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM roles WHERE lower(btrim(role_name))=lower(@RoleName))", r, t))
        {
            throw new ValidationException("Role name already exists.");
        }

        var id = await c.ExecuteScalarAsync<int>("INSERT INTO roles(role_name,description,is_active,requires_two_factor) VALUES(@RoleName,@Description,true,@RequiresTwoFactor) RETURNING role_id",
r,
t);
        await Audit(c, t, "ROLE_CREATED", id, null, r);
        await t.CommitAsync();
        return Ok(new
        {
            roleId = id
        });
    }

    public class AuthenticatorReset
    {
        [Required, StringLength(500, MinimumLength = 10)]
        public string Reason { get; set; } = "";
        public bool IdentityVerified
        {
            get; set;
        }

        [Required, StringLength(64)]
        public string Code { get; set; } = "";
        public long ExpectedVersion
        {
            get; set;
        }
    }

    [HttpGet("store-options")]
    public async Task<IActionResult> Stores()
    {
        return Ok(new
        {
            stores = await branches.StoresAsync(),
            warehouses = (await branches.LocationsAsync()).Where(l => l.StoreId is null && l.LocationType == "WAREHOUSE")
        });
    }

    [HttpGet("security/policy")]
    public async Task<IActionResult> GetPolicy()
    {
        using var c = factory.CreateConnection();
        return Ok(await c.QuerySingleOrDefaultAsync<Policy>("SELECT minimum_password_length MinimumPasswordLength,require_two_factor RequireTwoFactor,max_login_attempts MaxLoginAttempts,lockout_minutes LockoutMinutes FROM tenant_security_settings") ?? new());
    }

    [HttpPut("security/policy")]
    public async Task<IActionResult> SavePolicy(Policy p)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        var old = await c.QuerySingleOrDefaultAsync<Policy>("SELECT minimum_password_length MinimumPasswordLength,require_two_factor RequireTwoFactor,max_login_attempts MaxLoginAttempts,lockout_minutes LockoutMinutes FROM tenant_security_settings",
transaction: t);
        await c.ExecuteAsync("INSERT INTO tenant_security_settings(tenant_id,minimum_password_length,require_two_factor,max_login_attempts,lockout_minutes) VALUES(sf_tenant(),@MinimumPasswordLength,@RequireTwoFactor,@MaxLoginAttempts,@LockoutMinutes) ON CONFLICT(tenant_id) DO UPDATE SET minimum_password_length=EXCLUDED.minimum_password_length,require_two_factor=EXCLUDED.require_two_factor,max_login_attempts=EXCLUDED.max_login_attempts,lockout_minutes=EXCLUDED.lockout_minutes; UPDATE users SET security_version=security_version+1",
p,
t);
        await Audit(c, t, "SECURITY_POLICY_CHANGED", null, old, p);
        await t.CommitAsync();
        return NoContent();
    }

    [HttpPut("roles/{id:int}/security")]
    public async Task<IActionResult> SetRolePolicy(int id, RolePolicy p)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        if(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM roles r WHERE r.role_id=@id AND (lower(btrim(r.role_name)) IN('admin','owner','main owner') OR EXISTS(SELECT 1 FROM users u WHERE u.tenant_id=r.tenant_id AND u.role_id=r.role_id AND u.is_tenant_owner)))",new{id},t))throw new UnauthorizedAccessException("Protected role policy cannot be changed here.");
        var old = await c.QuerySingleOrDefaultAsync<RolePolicy>("SELECT requires_two_factor RequiresTwoFactor FROM roles WHERE role_id=@id AND is_active FOR UPDATE", new
        {
            id
        }, t);
        if (old is null)
        {
            return NotFound();
        }

        // Role policy cannot weaken a mandatory tenant policy; effective policy always ORs them.
        await c.ExecuteAsync("UPDATE roles SET requires_two_factor=@RequiresTwoFactor WHERE role_id=@id; UPDATE users SET security_version=security_version+1 WHERE role_id=@id",
new
{
    id,
    p.RequiresTwoFactor
},
t);
        await Audit(c, t, "ROLE_SECURITY_CHANGED", id, old, p);
        await t.CommitAsync();
        return NoContent();
    }

    [HttpPost("users/{id:int}/reset-authenticator")]
    public async Task<IActionResult> Reset(int id, AuthenticatorReset r)
    {
        if (id == access.User!.UserId || !r.IdentityVerified || !access.User.TwoFactorEnabled)
        {
            throw new ValidationException("Verify the user's identity and enroll your own authenticator first. Use recovery codes for your own account.");
        }

        if (!(await twoFactor.Verify(access.User.UserId, r.Code)).Success)
        {
            throw new ValidationException("Your authenticator code is invalid.");
        }

        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE user_id=@id AND is_tenant_owner) AND NOT sf_owner()", new
        {
            id
        }, t))
        {
            return Forbid();
        }

        var changed = await c.ExecuteAsync("UPDATE users SET two_factor_enabled=false,require_two_factor=true,two_factor_secret_encrypted=NULL,two_factor_pending_encrypted=NULL,two_factor_pending_until=NULL,two_factor_enabled_at=NULL,two_factor_last_step=NULL,two_factor_failures=0,two_factor_locked_until=NULL,security_version=security_version+1 WHERE tenant_id=sf_tenant() AND user_id=@id AND sf_staff_target(user_id) AND security_version=@ExpectedVersion",
new
{
    id,
    r.ExpectedVersion
},
t);
        if (changed != 1)
        {
            throw new ValidationException("User changed or is unavailable. Reload before resetting.");
        }

        await c.ExecuteAsync("DELETE FROM two_factor_recovery_codes WHERE user_id=@id", new
        {
            id
        }, t);
        await Audit(c, t, "TWO_FACTOR_RESET", id, null, new
        {
            r.Reason,
            r.IdentityVerified
        });
        await t.CommitAsync();
        return NoContent();
    }

    private Task Audit(Npgsql.NpgsqlConnection c,
Npgsql.NpgsqlTransaction t,
string action,
int? id,
object? before,
object after)
    {
        return c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,record_id,old_values,new_values) VALUES(@actor,'Security',@action,@id,CAST(@before AS jsonb),CAST(@after AS jsonb))",
new
{
    actor = access.User!.UserId,
    action,
    id,
    before = JsonSerializer.Serialize(before),
    after = JsonSerializer.Serialize(after)
},
t);
    }
}
