using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Security;

public sealed class RoleDirectoryRow:SecurityRole
{
 public long AssignedUsers {get;set;}
 public string Version {get;set;}="";
 public DateTime? UpdatedAt {get;set;}
 public string? UpdatedBy {get;set;}
}
public sealed record RoleDetails(RoleDirectoryRow Role,IEnumerable<ModulePermission> Permissions);
public sealed class SaveRoleRequest
{
 [RegularExpression("^(EMPLOYEE|SHOP_ADMIN)$")] public string? StaffAccountType {get;set;}
 public int? RoleId {get;set;}
 [Required,StringLength(50,MinimumLength=2)] public string RoleName {get;set;}="";
 [StringLength(200)] public string? Description {get;set;}
 public bool IsActive {get;set;}=true;
 public bool RequiresTwoFactor {get;set;}
 public string? TemplateKey {get;set;}
 public string? ExpectedTemplateVersion {get;set;}
 public bool CustomizePermissions {get;set;}
 public string? ExpectedVersion {get;set;}
 public long ExpectedAssignedUsers {get;set;}
 public bool ConfirmAssignedUsers {get;set;}
 [Required] public List<ModulePermission> Permissions {get;set;}=[];
}
public sealed class RoleStatusRequest
{
 [Required] public string ExpectedVersion {get;set;}="";
 public bool IsActive {get;set;}
}
