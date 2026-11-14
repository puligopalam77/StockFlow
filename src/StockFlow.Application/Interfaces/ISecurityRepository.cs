using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.Application.Interfaces;

public interface ISecurityRepository
{
    Task<LoginResult> LoginAsync(LoginRequest request);
    Task<UserAccess?> GetAccessAsync(int userId);
    Task<IEnumerable<SecurityUser>> GetUsersAsync(string? term, int offset, long? storeId = null, string? accountType = null, int? roleId = null, bool? isActive = null);
    Task<StaffRegistrationOptions> StaffRegistrationOptionsAsync(int actor);
    Task<string> SuggestStaffUsernameAsync(string fullName, int actor);
    Task<SecurityUser?> GetUserAsync(int id);
    Task<int> SaveUserAsync(int? id, SaveSecurityUserRequest request, int actor);
    Task SetStatusAsync(int id, UserStatusUpdate request, int actor);
    Task SetPermissionsAsync(int id, PermissionUpdate request, int actor);
    Task ResetPasswordAsync(int id, ResetPasswordRequest request, int actor);
    Task ChangePasswordAsync(int id, ChangePasswordRequest request);
    Task<IEnumerable<ModulePermission>> GetModulesAsync();
    Task<IEnumerable<SecurityRole>> GetRolesAsync();
    Task<IEnumerable<ModulePermission>> GetRolePermissionsAsync(int id);
    Task SetRolePermissionsAsync(int id, List<ModulePermission> permissions, int actor);
    Task<IEnumerable<RoleDirectoryRow>> RoleDirectoryAsync(int actor);
    Task<RoleDetails> RoleDetailsAsync(int id,int actor);
    Task<int> SaveRoleAsync(SaveRoleRequest request,int actor);
    Task SetRoleStatusAsync(int id,RoleStatusRequest request,int actor);
    Task<IEnumerable<RoleTemplateRow>> RoleTemplatesAsync(int actor);
    Task<RoleTemplateDetails> RoleTemplateAsync(string key,int actor);
    Task<string> SaveRoleTemplateAsync(SaveRoleTemplateRequest request,int actor);
    Task<IEnumerable<SecurityAudit>> GetAuditAsync(AuditSearch search, int limit = 100);
}
