using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public partial class SecurityRepository : ISecurityRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public SecurityRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private static readonly PasswordHasher<string> Hasher = new();
    private const string UserSql = "SELECT u.is_tenant_owner IsTenantOwner,u.require_two_factor RequireTwoFactor,u.two_factor_enabled TwoFactorEnabled,(u.require_two_factor OR r.requires_two_factor OR EXISTS(SELECT 1 FROM tenant_security_settings WHERE require_two_factor)) TwoFactorRequired,(SELECT string_agg(s.store_name,', ' ORDER BY s.store_name) FROM user_store_access a JOIN stores s USING(store_id) WHERE a.user_id=u.user_id AND a.is_active) AssignedStores,u.user_id UserId,u.role_id RoleId,r.role_name RoleName,u.full_name FullName,u.username Username,u.mobile_number MobileNumber,u.email Email,u.is_active IsActive,u.must_change_password MustChangePassword,u.last_login_at LastLoginAt,u.locked_until LockedUntil,u.security_version SecurityVersion FROM users u JOIN roles r ON r.role_id=u.role_id";
    private const string PermissionSql = """
        SELECT m.module_id ModuleId,m.parent_module_id ParentModuleId,parent.module_name ParentModuleName,
        m.module_code ModuleCode,m.module_name ModuleName,m.description Description,m.route Route,m.sort_order SortOrder,m.is_active IsActive,
        a.supported_actions SupportedActions,a.actions Actions, 'VIEW'=ANY(a.actions) CanView,'CREATE'=ANY(a.actions) CanCreate,
        (a.actions && ARRAY['EDIT','UPDATE','MANAGE','MANAGE_PERMISSIONS']::varchar[]) CanEdit,
        'DEACTIVATE'=ANY(a.actions) CanDelete,(a.actions && ARRAY['APPROVE','DELIVER']::varchar[]) CanApprove,'EXPORT'=ANY(a.actions) CanExport
        FROM modules m LEFT JOIN modules parent ON parent.module_id=m.parent_module_id CROSS JOIN LATERAL (
          SELECT COALESCE(array_agg(ma.action_code ORDER BY ma.module_action_id),ARRAY[]::varchar[]) supported_actions,
          COALESCE(array_agg(ma.action_code ORDER BY ma.module_action_id) FILTER(WHERE p.is_allowed),ARRAY[]::varchar[]) actions
          FROM module_actions ma LEFT JOIN user_action_permissions p ON p.module_action_id=ma.module_action_id AND p.user_id=@id
          WHERE ma.module_id=m.module_id AND ma.is_active
        ) a WHERE m.is_active AND EXISTS(SELECT 1 FROM module_actions x WHERE x.module_id=m.module_id AND x.is_active)
        ORDER BY COALESCE(parent.sort_order,m.sort_order),parent.module_id NULLS FIRST,m.sort_order,m.module_id
        """;
    private const string NavigationSql = """
        SELECT m.module_id ModuleId,m.parent_module_id ParentModuleId,m.module_code ModuleCode,m.module_name ModuleName,
        m.route Route,m.navigation_icon Icon,m.sort_order SortOrder,m.is_active IsActive,
        COALESCE(array_agg(p.module_code||':'||r.action_code ORDER BY p.module_code,r.action_code)
          FILTER(WHERE p.module_id IS NOT NULL),ARRAY[]::text[]) RequiredPermissions
        FROM modules m LEFT JOIN module_navigation_permissions r ON r.navigation_module_id=m.module_id
        LEFT JOIN modules p ON p.module_id=r.permission_module_id AND p.is_active
        WHERE m.is_active AND m.show_in_navigation
        GROUP BY m.module_id ORDER BY m.sort_order,m.module_id
        """;
    private static async Task CheckPasswordPolicy(NpgsqlConnection c, NpgsqlTransaction t, string password)
    {
        var minimum = await c.ExecuteScalarAsync<int?>("SELECT minimum_password_length FROM tenant_security_settings", transaction: t) ?? 8;
        if (password.Length < minimum)
        {
            throw new ValidationException($"This business requires at least {minimum} password characters.");
        }
    }

    private static bool Verify(string hash, string password)
    {
        try
        {
            return Hasher.VerifyHashedPassword("", hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        if (factory.Scope is { } loginScope)
        {
            using var lookup = factory.CreateConnection();
            loginScope.TenantId = await lookup.ExecuteScalarAsync<long?>("SELECT sf_login_tenant(@code)", new
            {
                code = request.TenantCode
            }) ?? 0;
            if (loginScope.TenantId == 0)
            {
                return new()
                {
                    ErrorMessage = "Invalid business code or credentials."
                };
            }
        }

        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        var row = await c.QuerySingleOrDefaultAsync<LoginRow>("SELECT u.user_id UserId,u.username Username,u.full_name FullName,r.role_name RoleName,u.password_hash Hash,u.is_active AND r.is_active Active,u.locked_until>CURRENT_TIMESTAMP Locked,u.security_version SecurityVersion,u.must_change_password MustChangePassword,(u.two_factor_enabled OR u.require_two_factor OR r.requires_two_factor OR EXISTS(SELECT 1 FROM tenant_security_settings WHERE require_two_factor)) NeedsTwoFactor FROM users u JOIN roles r ON r.role_id=u.role_id WHERE lower(u.username)=lower(@username) FOR UPDATE OF u",
new
{
    username = request.Username?.Trim()
},
t);
        var failure = new LoginResult
        {
            ErrorMessage = "Invalid credentials, inactive account or temporary lockout. Try again later."
        };
        if (row is null || row.Locked)
        {
            await c.ExecuteAsync("INSERT INTO audit_logs(module_name,action_name) VALUES('Security','LOGIN_FAILED')", transaction: t);
            await t.CommitAsync();
            return failure;
        }

        if (SecurityRules.IsMfaExempt(row.RoleName))
        {
            row.NeedsTwoFactor = false;
        }

        var limits = await c.QuerySingleOrDefaultAsync<LoginLimits>("SELECT max_login_attempts Attempts,lockout_minutes Minutes FROM tenant_security_settings", transaction: t) ?? new();
        if (!Verify(row.Hash, request.Password ?? ""))
        {
            await c.ExecuteAsync("UPDATE users SET failed_login_count=CASE WHEN locked_until<=CURRENT_TIMESTAMP THEN 1 ELSE failed_login_count+1 END,locked_until=CASE WHEN (CASE WHEN locked_until<=CURRENT_TIMESTAMP THEN 1 ELSE failed_login_count+1 END)>=@Attempts THEN CURRENT_TIMESTAMP+make_interval(mins=>@Minutes) ELSE NULL END WHERE user_id=@id",
new
{
    id = row.UserId,
    limits.Attempts,
    limits.Minutes
},
t);
            await Audit(c, t, row.UserId, "LOGIN_FAILED", row.UserId, null, new
            {
                failed = true
            });
            await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,record_id) SELECT @id,'Security','ACCOUNT_LOCKED',@id FROM users WHERE user_id=@id AND locked_until>now()",
new
{
    id = row.UserId
},
t);
            await t.CommitAsync();
            return failure;
        }

        // Reveal inactive status only after checking the password. Never sign in
        // an inactive user or a user whose role has been disabled.
        if (!row.Active)
        {
            await Audit(c, t, row.UserId, "LOGIN_FAILED", row.UserId, null, new { reason = "ACCOUNT_INACTIVE" });
            await t.CommitAsync();
            return new LoginResult { ErrorCode = LoginResult.AccountInactive, ErrorMessage = LoginResult.InactiveMessage };
        }

        await c.ExecuteAsync("UPDATE users SET failed_login_count=0,locked_until=NULL,last_login_at=CASE WHEN @complete THEN CURRENT_TIMESTAMP ELSE last_login_at END WHERE user_id=@id",
new
{
    id = row.UserId,
    complete = !row.NeedsTwoFactor && !row.MustChangePassword
},
t);
        await Audit(c, t, row.UserId, "PASSWORD_VERIFIED", row.UserId, null, new
        {
            verified = true
        });
        if (!row.NeedsTwoFactor && !row.MustChangePassword)
        {
            await Audit(c, t, row.UserId, "LOGIN_SUCCEEDED", row.UserId, null, new
            {
                authenticated = true
            });
        }

        await t.CommitAsync();
        long tenantId = 0, storeId = 0, locationId = 0;
        if (factory.Scope is { } scope)
        {
            scope.UserId = row.UserId;
            tenantId = scope.TenantId;
            using var scoped = factory.CreateConnection();
            var state = await scoped.QuerySingleAsync<BranchUserState>("SELECT u.is_tenant_owner IsOwner,lower(btrim(r.role_name))='admin' IsAdmin FROM users u JOIN roles r ON r.role_id=u.role_id WHERE u.user_id=@UserId",
new
{
    row.UserId
});
            var locations = await scoped.QueryAsync<BranchLocation>("""
                SELECT l.location_id AS LocationId,l.store_id AS StoreId,l.location_type AS LocationType,l.is_active AS IsActive,
                    EXISTS(SELECT 1 FROM user_store_access a WHERE a.user_id=@UserId AND a.store_id=l.store_id AND a.is_default AND a.is_active) AS IsDefault
                FROM store_locations l LEFT JOIN stores s ON s.store_id=l.store_id
                WHERE l.is_active AND l.location_type<>'TRANSIT' AND (l.store_id IS NULL OR s.is_active)
                """,
new
{
    row.UserId
});
            var location = StoreContextRules.DefaultLocation(locations, state.IsOwner || state.IsAdmin);
            if (location is null)
            {
                return new()
                {
                    ErrorMessage = "No default store with an active sales location, or assigned warehouse, is available. Contact your business administrator."
                };
            }

            storeId = location.StoreId ?? 0;
            locationId = location.LocationId;
        }

        return new LoginResult
        {
            Success = true,
            UserId = row.UserId,
            Username = row.Username,
            FullName = row.FullName,
            RoleName = row.RoleName,
            SecurityVersion = row.SecurityVersion,
            MustChangePassword = row.MustChangePassword,
            TenantId = tenantId,
            StoreId = storeId,
            LocationId = locationId
        };
    }

    private sealed class LoginLimits
    {
        public int Attempts { get; set; } = 5;
        public int Minutes { get; set; } = 15;
    }

    private sealed class LoginRow
    {
        public bool NeedsTwoFactor
        {
            get; set;
        }
        public int UserId
        {
            get; set;
        }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RoleName { get; set; } = "";
        public string Hash { get; set; } = "";
        public bool Active
        {
            get; set;
        }
        public bool Locked
        {
            get; set;
        }
        public long SecurityVersion
        {
            get; set;
        }
        public bool MustChangePassword
        {
            get; set;
        }
    }

    public async Task<UserAccess?> GetAccessAsync(int userId)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var row = await c.QuerySingleOrDefaultAsync<UserAccess>("SELECT u.two_factor_enabled TwoFactorEnabled,(u.require_two_factor OR r.requires_two_factor OR EXISTS(SELECT 1 FROM tenant_security_settings WHERE require_two_factor)) TwoFactorRequired,u.user_id UserId,u.username Username,u.full_name FullName,u.email Email,u.mobile_number MobileNumber,r.role_name RoleName,u.is_active AND r.is_active IsActive,u.must_change_password MustChangePassword,u.security_version SecurityVersion FROM users u JOIN roles r ON r.role_id=u.role_id WHERE u.user_id=@id",
new
{
    id = userId
},
t);
        if (row is not null)
        {
            row.Permissions = (await c.QueryAsync<ModulePermission>(PermissionSql, new
            {
                id = userId
            }, t)).ToList();
            row.NavigationModules = (await c.QueryAsync<NavigationModule>(NavigationSql, transaction: t)).ToList();
        }

        await t.CommitAsync();
        return row;
    }

    public async Task<IEnumerable<SecurityUser>> GetUsersAsync(string? term, int offset)
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<SecurityUser>(UserSql.Insert(7,"COUNT(*) OVER() AS TotalCount,") + " WHERE (CAST(@term AS text) IS NULL OR u.username ILIKE @term OR u.full_name ILIKE @term) ORDER BY u.user_id LIMIT 100 OFFSET @offset",
new
{
    term = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim() + "%",
    offset = Math.Max(0, offset)
});
    }

    public async Task<SecurityUser?> GetUserAsync(int id)
    {
        using var c = factory.CreateConnection();
        var u = await c.QuerySingleOrDefaultAsync<SecurityUser>(UserSql + " WHERE u.user_id=@id", new
        {
            id
        });
        if (u is not null)
        {
            u.Permissions = (await c.QueryAsync<ModulePermission>(PermissionSql, new
            {
                id
            })).ToList();
            u.StoreAccess = await ReadStoreAccess(c, id);
        }

        return u;
    }

    public async Task<IEnumerable<ModulePermission>> GetModulesAsync()
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<ModulePermission>(PermissionSql, new
        {
            id = 0
        });
    }

    public async Task<IEnumerable<SecurityRole>> GetRolesAsync()
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<SecurityRole>("SELECT role_id RoleId,role_name RoleName,description Description,is_active IsActive,requires_two_factor RequiresTwoFactor FROM roles ORDER BY role_id");
    }

    public async Task<IEnumerable<ModulePermission>> GetRolePermissionsAsync(int id)
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<ModulePermission>(PermissionSql.Replace("user_action_permissions", "role_action_permissions").Replace("p.user_id", "p.role_id"),
new
{
    id
});
    }

    private static async Task Require(NpgsqlConnection c, NpgsqlTransaction t, int actor, string module, string action)
    {
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users u JOIN roles r USING(role_id) WHERE u.user_id=@actor AND u.is_active AND r.is_active)",
new
{
    actor
},
t))
        {
            throw new UnauthorizedAccessException("Account is inactive.");
        }

        var p = (await c.QueryAsync<ModulePermission>(PermissionSql, new
        {
            id = actor
        }, t)).SingleOrDefault(p => p.ModuleCode == module);
        if (p?.Allows(action) != true)
        {
            throw new UnauthorizedAccessException("Permission denied.");
        }
    }

    private async Task<SecurityUser> Target(NpgsqlConnection c, NpgsqlTransaction t, int id, long version)
    {
        if (factory.Scope is not null && await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE user_id=@id AND is_tenant_owner) AND NOT sf_owner()", new
        {
            id
        }, t))
        {
            throw new UnauthorizedAccessException("Only the business owner can manage an owner account.");
        }

        var u = await c.QuerySingleOrDefaultAsync<SecurityUser>(UserSql + " WHERE u.user_id=@id FOR UPDATE OF u", new
        {
            id
        }, t) ?? throw new KeyNotFoundException("User not found.");
        if (u.SecurityVersion != version)
        {
            throw new ValidationException("This user changed. Reload before saving.");
        }

        u.Permissions = (await c.QueryAsync<ModulePermission>(PermissionSql, new
        {
            id
        }, t)).ToList();
        return u;
    }

    private static Task Audit(NpgsqlConnection c,
NpgsqlTransaction t,
int actor,
string action,
int id,
object? oldValue,
object? newValue)
    {
        return c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,old_values,new_values) VALUES(@actor,'Administration',@action,@tableName,@id,CAST(@old AS jsonb),CAST(@next AS jsonb))",
new
{
    actor,
    action,
    id,
    tableName = action == "ROLE_TEMPLATE_UPDATE" ? "roles" : "users",
    old = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
    next = newValue is null ? null : JsonSerializer.Serialize(newValue)
},
t);
    }

    private static async Task GuardAdmins(NpgsqlConnection c, NpgsqlTransaction t)
    {
        var users = await c.QueryAsync<int>("SELECT u.user_id FROM users u JOIN roles r USING(role_id) WHERE u.is_active AND r.is_active AND (u.is_tenant_owner OR r.role_name='Admin')",
transaction: t);
        foreach (var id in users)
        {
            var permissions = (await c.QueryAsync<ModulePermission>(PermissionSql, new
            {
                id
            }, t)).ToList();
            if (permissions.Any(p => p.ModuleCode == "USER_MANAGEMENT" && p.Allows("MANAGE_PERMISSIONS")) && permissions.Any(p => p.ModuleCode == "SECURITY" && p.Allows("MANAGE")))
            {
                return;
            }
        }

        throw new ValidationException("At least one active administrator with user permission and security management access must remain.");
    }

    private static async Task ProtectAdmin(NpgsqlConnection c, NpgsqlTransaction t, int actor, int roleId)
    {
        if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM roles WHERE role_id=@roleId AND role_name='Admin') AND NOT EXISTS(SELECT 1 FROM users u JOIN roles r USING(role_id) WHERE u.user_id=@actor AND r.role_name='Admin')",
new
{
    actor,
    roleId
},
t))
        {
            throw new UnauthorizedAccessException("Only an administrator may manage an administrator account.");
        }
    }

    private static async Task WritePermissions(NpgsqlConnection c,
NpgsqlTransaction t,
int id,
List<ModulePermission> rows,
int actor,
bool role = false)
    {
        await Require(c, t, actor, role ? "ROLE_MANAGEMENT" : "USER_MANAGEMENT", "MANAGE_PERMISSIONS");
        var modules = (await c.QueryAsync<ModulePermission>(PermissionSql, new
        {
            id = 0
        }, t)).ToDictionary(p => p.ModuleId);
        if (rows.Count != modules.Count || rows.Any(p => !modules.ContainsKey(p.ModuleId) || p.Actions is null))
        {
            throw new ValidationException("Submit supported actions for every active module.");
        }

        foreach (var row in rows)
        {
            row.ModuleCode = modules[row.ModuleId].ModuleCode;
            row.SupportedActions = modules[row.ModuleId].SupportedActions;
            row.CanView = false;
            row.CanCreate = false;
            row.CanEdit = false;
            row.CanDelete = false;
            row.CanApprove = false;
            row.CanExport = false;
        }

        SecurityRules.Permissions(rows);
        var own = (await c.QueryAsync<ModulePermission>(PermissionSql, new
        {
            id = actor
        }, t)).ToDictionary(p => p.ModuleId);
        foreach (var p in rows)
        {
            foreach (var action in p.Actions!)
            {
                if (!own[p.ModuleId].Allows(action))
                {
                    throw new ValidationException("You cannot grant an action you do not have.");
                }
            }
        }

        var table = role ? "role_action_permissions" : "user_action_permissions";
        var key = role ? "role_id" : "user_id";
        await c.ExecuteAsync($"DELETE FROM {table} WHERE {key}=@id", new
        {
            id
        }, t);
        foreach (var p in rows)
        {
            foreach (var action in p.Actions!)
            {
                await c.ExecuteAsync($"INSERT INTO {table}({key},module_action_id,is_allowed,assigned_by) SELECT @id,module_action_id,true,@actor FROM module_actions WHERE module_id=@ModuleId AND action_code=@action AND is_active",
new
{
    id,
    p.ModuleId,
    action,
    actor
},
t);
            }
        }

        if (!role && id == actor)
        {
            await Require(c, t, actor, "USER_MANAGEMENT", "MANAGE_PERMISSIONS");
            await Require(c, t, actor, "SECURITY", "MANAGE");
        }
    }

    public async Task<int> SaveUserAsync(int? id, SaveSecurityUserRequest r, int actor)
    {
        SecurityRules.User(r, id is null);
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        await Require(c, t, actor, "USER_MANAGEMENT", id is null ? "CREATE" : "EDIT");
        if (id is null)
        {
            await CheckPasswordPolicy(c, t, r.Password!);
        }

        var old = id.HasValue ? await Target(c, t, id.Value, r.ExpectedVersion) : null;
        if (r.UseRoleTemplate)
        {
            // Do not trust hidden client permission arrays or reset an existing MFA policy.
            r.RequireTwoFactor = old?.RequireTwoFactor ?? false;
            var sameRole = old is not null && old.RoleId == r.RoleId;
            var sql = sameRole ? PermissionSql : PermissionSql.Replace("user_action_permissions", "role_action_permissions").Replace("p.user_id", "p.role_id");
            r.Permissions = (await c.QueryAsync<ModulePermission>(sql, new { id = sameRole ? id!.Value : r.RoleId }, t)).ToList();
        }
        await ProtectAdmin(c, t, actor, r.RoleId);
        if (old is not null)
        {
            await ProtectAdmin(c, t, actor, old.RoleId);
        }

        if (id == actor && (!r.IsActive || r.RoleId != old!.RoleId))
        {
            throw new ValidationException("You cannot deactivate yourself or change your own role.");
        }

        if (old is not null && old.IsActive != r.IsActive)
        {
            await Require(c, t, actor, "USER_MANAGEMENT", "DELETE");
        }

        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM roles WHERE role_id=@RoleId AND is_active)", r, t))
        {
            throw new ValidationException("Choose an active role.");
        }

        if (id is null)
        {
            id = await c.ExecuteScalarAsync<int>("INSERT INTO users(role_id,full_name,username,password_hash,mobile_number,email,is_active,must_change_password,created_by,password_changed_at) VALUES(@RoleId,@FullName,@Username,@hash,@MobileNumber,@Email,@IsActive,@MustChangePassword,@actor,CURRENT_TIMESTAMP) RETURNING user_id",
new
{
    r.RoleId,
    r.FullName,
    r.Username,
    hash = Hasher.HashPassword("", r.Password!),
    r.MobileNumber,
    r.Email,
    r.IsActive,
    r.MustChangePassword,
    actor
},
t);
        }
        else
        {
            await c.ExecuteAsync("UPDATE users SET role_id=@RoleId,full_name=@FullName,username=@Username,mobile_number=@MobileNumber,email=@Email,is_active=@IsActive,must_change_password=@MustChangePassword,updated_by=@actor,updated_at=CURRENT_TIMESTAMP,security_version=security_version+1 WHERE user_id=@id",
new
{
    id,
    r.RoleId,
    r.FullName,
    r.Username,
    r.MobileNumber,
    r.Email,
    r.IsActive,
    r.MustChangePassword,
    actor
},
t);
        }

        await c.ExecuteAsync("UPDATE users SET require_two_factor=@RequireTwoFactor WHERE user_id=@id", new { id, r.RequireTwoFactor }, t);
        await SaveStoreAccess(c, t, id.Value, r.StoreAccess, actor);
        await WritePermissions(c, t, id.Value, r.Permissions, actor);
        await GuardAdmins(c, t);
        await Audit(c,
t,
actor,
old is null ? "USER_CREATE" : "USER_UPDATE",
id.Value,
old,
new
{
    r.RoleId,
    r.FullName,
    r.Username,
    r.MobileNumber,
    r.Email,
    r.IsActive,
    r.MustChangePassword,
    r.RequireTwoFactor,
    r.StoreAccess,
    r.Permissions
});
        await t.CommitAsync();
        return id.Value;
    }

    public async Task SetStatusAsync(int id, UserStatusUpdate r, int actor)
    {
        await Mutate(id,
r.ExpectedVersion,
actor,
"DELETE",
"USER_STATUS",
async (c, t, old) =>
        {
            if (id == actor && !r.IsActive)
            {
                throw new ValidationException("You cannot deactivate yourself.");
            }

            await c.ExecuteAsync("UPDATE users SET is_active=@IsActive WHERE user_id=@id", new
            {
                id,
                r.IsActive
            }, t);
            return new
            {
                r.IsActive
            };
        });
    }

    public async Task SetPermissionsAsync(int id, PermissionUpdate r, int actor)
    {
        await Mutate(id,
r.ExpectedVersion,
actor,
"MANAGE_PERMISSIONS",
"USER_PERMISSIONS",
async (c, t, old) =>
        {
            await WritePermissions(c, t, id, r.Permissions, actor);
            return r.Permissions;
        });
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordRequest r, int actor)
    {
        SecurityRules.Password(r.Password, r.ConfirmPassword);
        r.MustChangePassword = true;
        await Mutate(id,
r.ExpectedVersion,
actor,
"RESET_PASSWORD",
"PASSWORD_RESET",
async (c, t, old) =>
        {
            await CheckPasswordPolicy(c, t, r.Password);
            await c.ExecuteAsync("UPDATE users SET password_hash=@hash,must_change_password=@MustChangePassword,failed_login_count=0,locked_until=NULL,password_changed_at=CURRENT_TIMESTAMP WHERE user_id=@id",
new
{
    id,
    hash = Hasher.HashPassword("", r.Password),
    r.MustChangePassword
},
t);
            return new
            {
                r.MustChangePassword
            };
        });
    }

    private async Task Mutate(int id,
long version,
int actor,
string action,
string audit,
Func<NpgsqlConnection, NpgsqlTransaction, SecurityUser, Task<object>> change)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        await Require(c, t, actor, "USER_MANAGEMENT", action);
        var old = await Target(c, t, id, version);
        await ProtectAdmin(c, t, actor, old.RoleId);
        var next = await change(c, t, old);
        await c.ExecuteAsync("UPDATE users SET security_version=security_version+1,updated_at=CURRENT_TIMESTAMP,updated_by=@actor WHERE user_id=@id",
new
{
    id,
    actor
},
t);
        await GuardAdmins(c, t);
        await Audit(c, t, actor, audit, id, old, next);
        await t.CommitAsync();
    }

    public async Task ChangePasswordAsync(int id, ChangePasswordRequest r)
    {
        SecurityRules.Password(r.Password, r.ConfirmPassword);
        if (r.Password == r.CurrentPassword)
        {
            throw new ValidationException("Choose a different password.");
        }

        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await CheckPasswordPolicy(c, t, r.Password);
        var hash = await c.QuerySingleOrDefaultAsync<string>("SELECT password_hash FROM users WHERE user_id=@id AND is_active FOR UPDATE", new
        {
            id
        }, t);
        if (hash is null || !Verify(hash, r.CurrentPassword))
        {
            throw new ValidationException("Current password is incorrect.");
        }

        await c.ExecuteAsync("UPDATE users SET password_hash=@hash,must_change_password=false,password_changed_at=CURRENT_TIMESTAMP,failed_login_count=0,locked_until=NULL,security_version=security_version+1,updated_at=CURRENT_TIMESTAMP,updated_by=@id WHERE user_id=@id",
new
{
    id,
    hash = Hasher.HashPassword("", r.Password)
},
t);
        await Audit(c, t, id, "PASSWORD_CHANGE", id, null, new
        {
            changed = true
        });
        await t.CommitAsync();
    }

    public async Task SetRolePermissionsAsync(int id, List<ModulePermission> rows, int actor)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(170017)", transaction: t);
        await Require(c, t, actor, "ROLE_MANAGEMENT", "MANAGE_PERMISSIONS");
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM roles WHERE role_id=@id AND is_active)", new
        {
            id
        }, t))
        {
            throw new ValidationException("Choose an active role.");
        }

        var old = await c.QueryAsync<ModulePermission>(PermissionSql.Replace("user_action_permissions", "role_action_permissions").Replace("p.user_id", "p.role_id"),
new
{
    id
},
t);
        await WritePermissions(c, t, id, rows, actor, true);
        await Audit(c, t, actor, "ROLE_TEMPLATE_UPDATE", id, old, rows);
        await t.CommitAsync();
    }

    public async Task<IEnumerable<SecurityAudit>> GetAuditAsync(AuditSearch s, int limit = 100)
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<SecurityAudit>("SELECT COUNT(*) OVER() TotalCount,a.audit_log_id AuditLogId,u.username Username,a.module_name ModuleName,a.action_name ActionName,a.record_id RecordId,a.old_values::text OldValues,a.new_values::text NewValues,a.reason Reason,a.created_at CreatedAt FROM audit_logs a LEFT JOIN users u USING(user_id) WHERE (CAST(@action AS text) IS NULL OR a.action_name=@action) AND (CAST(@userId AS integer) IS NULL OR a.user_id=@userId) AND (CAST(@fromDate AS timestamp) IS NULL OR a.created_at>=@fromDate) AND (CAST(@toDate AS timestamp) IS NULL OR a.created_at<@toDate) ORDER BY a.created_at DESC,a.audit_log_id DESC LIMIT @limit OFFSET @offset",
new
{
    action = string.IsNullOrWhiteSpace(s.Action) ? null : s.Action,
    userId = s.UserId,
    fromDate = s.From,
    toDate = s.To?.Date.AddDays(1),
    limit = Math.Clamp(limit, 1, 1000),
    offset = Math.Max(0, s.Offset)
});
    }
}
