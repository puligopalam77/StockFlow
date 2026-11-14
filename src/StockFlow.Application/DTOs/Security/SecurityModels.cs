using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Security;

public class ModulePermission
{
    private string[]? supportedActions;
    public string[]? Actions
    {
        get; set;
    }
    public string[] SupportedActions
    {
        get => supportedActions ?? StockFlow.Application.Common.PermissionCatalog.Modules.GetValueOrDefault(ModuleCode, []); set => supportedActions = value;
    }
    public int ModuleId
    {
        get; set;
    }
    public int? ParentModuleId
    {
        get; set;
    }
    public string? ParentModuleName
    {
        get; set;
    }
    public string ModuleCode { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string? Description
    {
        get; set;
    }
    public string? Route
    {
        get; set;
    }
    public int SortOrder
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public bool CanView
    {
        get; set;
    }
    public bool CanCreate
    {
        get; set;
    }
    public bool CanEdit
    {
        get; set;
    }
    public bool CanDelete
    {
        get; set;
    }
    public bool CanApprove
    {
        get; set;
    }
    public bool CanExport
    {
        get; set;
    }

    public bool Allows(string action)
    {
        if (Actions is not null)
        {
            return Actions.Contains("VIEW") && Actions.Contains(StockFlow.Application.Common.PermissionCatalog.Canonical(ModuleCode, action));
        }
        else
        {
            return CanView && action switch
            {
                "VIEW" => true,
                "CREATE" => CanCreate,
                "EDIT" => CanEdit,
                "DELETE" => CanDelete,
                "APPROVE" => CanApprove,
                "EXPORT" => CanExport,
                _ => false
            };
        }
    }
}

public class UserAccess
{
    public bool TwoFactorRequired
    {
        get; set;
    }
    public bool TwoFactorEnabled
    {
        get; set;
    }
    public string FullName { get; set; } = "";
    public string? Email
    {
        get; set;
    }
    public string? MobileNumber
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public string Username { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool IsActive
    {
        get; set;
    }
    public bool MustChangePassword
    {
        get; set;
    }
    public long SecurityVersion
    {
        get; set;
    }
    public List<ModulePermission> Permissions { get; set; } = [];
    public List<NavigationModule> NavigationModules { get; set; } = [];

    public bool Can(string module, string action = "VIEW")
    {
        if (module.StartsWith("TRANSFER_"))
        {
            action = action == "VIEW" ? "VIEW" : module[9..];
            module = "STOCK_TRANSFER";
        }

        if (module == "INVENTORY" && action == "APPROVE")
        {
            module = "STOCK_ADJUSTMENT";
            action = "ADJUST";
        }

        return IsActive && Permissions.Any(p => p.ModuleCode == module && p.Allows(action));
    }
}

public class NavigationModule
{
    public int ModuleId
    {
        get; set;
    }
    public int? ParentModuleId
    {
        get; set;
    }
    public string ModuleCode { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string? Route
    {
        get; set;
    }
    public string? Icon
    {
        get; set;
    }
    public int SortOrder
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public string[] RequiredPermissions { get; set; } = [];
}

public class SecurityUser
{
    public bool UsesRolePermissions { get; set; }
    public string AccountType { get; set; } = "EMPLOYEE";
    public long? TotalCount { get; set; }
    public bool IsTenantOwner
    {
        get; set;
    }
    public bool RequireTwoFactor
    {
        get; set;
    }
    public bool TwoFactorRequired
    {
        get; set;
    }
    public bool TwoFactorEnabled
    {
        get; set;
    }
    public string AssignedStores { get; set; } = "";
    public StoreAccessRequest StoreAccess { get; set; } = new();
    public int UserId
    {
        get; set;
    }
    public int RoleId
    {
        get; set;
    }
    public string RoleName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string? MobileNumber
    {
        get; set;
    }
    public string? Email
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public bool MustChangePassword
    {
        get; set;
    }
    public DateTime? LastLoginAt
    {
        get; set;
    }
    public DateTime? LockedUntil
    {
        get; set;
    }
    public long SecurityVersion
    {
        get; set;
    }
    public List<ModulePermission> Permissions { get; set; } = [];
}

public class SaveSecurityUserRequest
{
    [RegularExpression("^(EMPLOYEE|SHOP_ADMIN)$")]
    public string? AccountType { get; set; }
    // Resolve role defaults on the server; preserve existing access on unchanged roles.
    public bool UseRoleTemplate { get; set; }
    public bool RequireTwoFactor
    {
        get; set;
    }
    public StoreAccessRequest StoreAccess { get; set; } = new();
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = "";
    [Required, StringLength(10), RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone, ErrorMessage = StockFlow.Application.Common.FieldPatterns.PhoneMessage)]
    public string? MobileNumber
    {
        get; set;
    }
    [Required, EmailAddress, StringLength(150)]
    public string? Email
    {
        get; set;
    }
    public int RoleId
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; } = true;
    public long ExpectedVersion
    {
        get; set;
    }
    public string? Password
    {
        get; set;
    }
    public string? ConfirmPassword
    {
        get; set;
    }
    public List<ModulePermission> Permissions { get; set; } = [];
}

public class ResetPasswordRequest
{
    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
    public bool MustChangePassword { get; set; } = true;
    public long ExpectedVersion
    {
        get; set;
    }
}

public class ChangePasswordRequest
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 8), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = "";
}

public class SecurityRole
{
    public string StaffAccountType { get; set; } = "EMPLOYEE";
    public bool IsProtected {get;set;}
    public bool RequiresTwoFactor
    {
        get; set;
    }
    public int RoleId
    {
        get; set;
    }
    public string RoleName { get; set; } = "";
    public string? Description
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
}

public class PermissionUpdate
{
    public long ExpectedVersion
    {
        get; set;
    }
    public List<ModulePermission> Permissions { get; set; } = [];
}

public class UserStatusUpdate
{
    public bool IsActive
    {
        get; set;
    }
    public long ExpectedVersion
    {
        get; set;
    }
}

public class AuditSearch
{
    public string? Action
    {
        get; set;
    }
    public int? UserId
    {
        get; set;
    }
    public DateTime? From
    {
        get; set;
    }
    public DateTime? To
    {
        get; set;
    }
    public int Offset
    {
        get; set;
    }
}

public class SecurityAudit
{
    public long? TotalCount { get; set; }
    public long AuditLogId
    {
        get; set;
    }
    public string? Username
    {
        get; set;
    }
    public string? ModuleName
    {
        get; set;
    }
    public string? ActionName
    {
        get; set;
    }
    public long? RecordId
    {
        get; set;
    }
    public string? OldValues
    {
        get; set;
    }
    public string? NewValues
    {
        get; set;
    }
    public string? Reason
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
