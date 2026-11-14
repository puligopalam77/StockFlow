using System.ComponentModel.DataAnnotations;
using Dapper;
using StockFlow.Application.DTOs.Security;
namespace StockFlow.Infrastructure.Repositories;

public partial class SecurityRepository
{
 private const string RoleRows="""
 SELECT r.staff_account_type StaffAccountType,r.role_id RoleId,r.role_name RoleName,r.description,r.is_active IsActive,r.requires_two_factor RequiresTwoFactor,
 (SELECT count(*) FROM users u WHERE u.tenant_id=r.tenant_id AND u.role_id=r.role_id) AssignedUsers,
 (lower(btrim(r.role_name)) IN('admin','owner','main owner') OR EXISTS(SELECT 1 FROM users u WHERE u.tenant_id=r.tenant_id AND u.role_id=r.role_id AND u.is_tenant_owner)) IsProtected,
 md5(to_jsonb(r)::text||coalesce((SELECT string_agg(p.module_action_id::text||':'||p.is_allowed::text,',' ORDER BY p.module_action_id) FROM role_action_permissions p WHERE p.tenant_id=r.tenant_id AND p.role_id=r.role_id),'')) Version,
 a.created_at UpdatedAt,a.full_name UpdatedBy
 FROM roles r LEFT JOIN LATERAL(SELECT l.created_at,u.full_name FROM audit_logs l LEFT JOIN users u ON u.tenant_id=l.tenant_id AND u.user_id=l.user_id WHERE l.tenant_id=r.tenant_id AND l.table_name='roles' AND l.record_id=r.role_id ORDER BY l.created_at DESC,l.audit_log_id DESC LIMIT 1) a ON true
 WHERE r.tenant_id=sf_tenant()
 """;
 public async Task<IEnumerable<RoleDirectoryRow>> RoleDirectoryAsync(int actor)
 {
  await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
  await Require(c,t,actor,"ROLE_MANAGEMENT","VIEW");
  return (await c.QueryAsync<RoleDirectoryRow>(RoleRows+" ORDER BY r.role_name,r.role_id",transaction:t)).ToArray();
 }
 public async Task<RoleDetails> RoleDetailsAsync(int id,int actor)
 {
  await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
  await Require(c,t,actor,"ROLE_MANAGEMENT","VIEW");
  var row=await c.QuerySingleOrDefaultAsync<RoleDirectoryRow>(RoleRows+" AND r.role_id=@id",new{id},t)??throw new KeyNotFoundException("Role is unavailable in this business.");
  var permissions=await c.QueryAsync<ModulePermission>(PermissionSql.Replace("effective_user_action_permissions","role_action_permissions").Replace("p.user_id","p.role_id"),new{id},t);
  return new(row,permissions.ToArray());
 }
 public async Task<int> SaveRoleAsync(SaveRoleRequest r,int actor)
 {
  r.RoleName=r.RoleName?.Trim()??"";r.Description=string.IsNullOrWhiteSpace(r.Description)?null:r.Description.Trim();Validator.ValidateObject(r,new(r),true);
  if(!r.RoleName.Any(char.IsLetterOrDigit)||new[]{"admin","owner","main owner"}.Contains(r.RoleName.ToLowerInvariant()))throw new ValidationException("Choose a staff role name; owner and system identities are reserved.");
  await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
  await c.ExecuteAsync("SET LOCAL lock_timeout='5s'; SELECT pg_advisory_xact_lock(170017)",transaction:t);
  await Require(c,t,actor,"ROLE_MANAGEMENT","MANAGE_PERMISSIONS");RoleDirectoryRow? old=null;
  if(r.TemplateKey is not null){
   if(r.RoleId.HasValue)throw new ValidationException("Apply templates when creating a new role. Review existing role changes separately.");
   var template=await TemplateDetail(c,t,r.TemplateKey,actor);
   if(template.Template.Version!=r.ExpectedTemplateVersion)throw new System.Data.DBConcurrencyException("Selected template changed. Reload and review it before saving the role.");
   if(!r.CustomizePermissions)r.Permissions=template.Permissions.ToList();
  }
  if(r.RoleId is int existing){
   await c.ExecuteAsync("SELECT role_id FROM roles WHERE tenant_id=sf_tenant() AND role_id=@existing FOR UPDATE",new{existing},t);
   old=await c.QuerySingleOrDefaultAsync<RoleDirectoryRow>(RoleRows+" AND r.role_id=@existing",new{existing},t)??throw new KeyNotFoundException("Role unavailable.");
   if(old.IsProtected)throw new UnauthorizedAccessException("Owner and protected system roles cannot be changed here.");
   if(old.Version!=r.ExpectedVersion||old.AssignedUsers!=r.ExpectedAssignedUsers)throw new System.Data.DBConcurrencyException("Role or assigned users changed. Reload before saving.");
   if(old.AssignedUsers>0&&!r.ConfirmAssignedUsers)throw new ValidationException("Confirm the assigned-user impact before saving this role.");
   if(!r.IsActive&&old.AssignedUsers>0)throw new ValidationException("Reassign all users before deactivating this role.");
  }
  r.StaffAccountType??=old?.StaffAccountType??(r.TemplateKey=="shop-admin"?"SHOP_ADMIN":"EMPLOYEE");
  if((old is null && r.StaffAccountType=="SHOP_ADMIN" || old is not null && old.StaffAccountType!=r.StaffAccountType) && !await c.ExecuteScalarAsync<bool>("SELECT sf_owner()",transaction:t))throw new UnauthorizedAccessException("Only the owner can classify Shop Admin roles.");
  if(old is not null && old.StaffAccountType!=r.StaffAccountType && old.AssignedUsers>0)throw new ValidationException("Create a new classified role instead of changing a role assigned to users.");
  if(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM roles WHERE tenant_id=sf_tenant() AND lower(btrim(role_name))=lower(@RoleName) AND (@RoleId IS NULL OR role_id<>@RoleId))",r,t))throw new ValidationException("Role name already exists in this business.");
  var id=r.RoleId??await c.ExecuteScalarAsync<int>("INSERT INTO roles(role_name,description,is_active,requires_two_factor) VALUES(@RoleName,@Description,@IsActive,@RequiresTwoFactor) RETURNING role_id",r,t);
  if(r.RoleId.HasValue)await c.ExecuteAsync("UPDATE roles SET role_name=@RoleName,description=@Description,is_active=@IsActive,requires_two_factor=@RequiresTwoFactor WHERE tenant_id=sf_tenant() AND role_id=@RoleId",r,t);
  await c.ExecuteAsync("UPDATE roles SET staff_account_type=@StaffAccountType WHERE tenant_id=sf_tenant() AND role_id=@id",new{id,r.StaffAccountType},t);
  var prior=await c.QueryAsync<ModulePermission>(PermissionSql.Replace("effective_user_action_permissions","role_action_permissions").Replace("p.user_id","p.role_id"),new{id},t);
  await WritePermissions(c,t,id,r.Permissions,actor,true);
  if(old is not null&&(old.RequiresTwoFactor!=r.RequiresTwoFactor||old.IsActive!=r.IsActive))await c.ExecuteAsync("UPDATE users SET security_version=security_version+1 WHERE tenant_id=sf_tenant() AND role_id=@id",new{id},t);
  await Audit(c,t,actor,"ROLE_TEMPLATE_UPDATE",id,new{Role=old,Permissions=prior},new{r.RoleName,r.StaffAccountType,r.Description,r.IsActive,r.RequiresTwoFactor,r.Permissions,r.TemplateKey,r.ExpectedTemplateVersion,r.CustomizePermissions});await t.CommitAsync();return id;
 }
 public async Task SetRoleStatusAsync(int id,RoleStatusRequest r,int actor)
 {
  var detail=await RoleDetailsAsync(id,actor);
  await SaveRoleAsync(new(){RoleId=id,RoleName=detail.Role.RoleName,Description=detail.Role.Description,IsActive=r.IsActive,RequiresTwoFactor=detail.Role.RequiresTwoFactor,ExpectedVersion=r.ExpectedVersion,ExpectedAssignedUsers=detail.Role.AssignedUsers,ConfirmAssignedUsers=false,Permissions=detail.Permissions.ToList()},actor);
 }
}
