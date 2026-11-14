using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class RoleManagementChecks
{
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
 static async Task Reject(Func<Task> action,string label){try{await action();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or KeyNotFoundException or System.Data.DBConcurrencyException or PostgresException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
 public static async Task Run(NpgsqlConnection setup,DbConnectionFactory factory)
 {
  Check(StockFlow.Web.Security.PermissionMiddleware.Requirement("/admin/roles/create","GET")==("ROLE_MANAGEMENT","MANAGE_PERMISSIONS"),"Role creation page requires the existing management action");
  var repo=new SecurityRepository(factory);var modules=(await repo.GetModulesAsync()).ToList();foreach(var p in modules)p.Actions=p.ModuleCode=="DASHBOARD"?["VIEW"]:[];
  var request=new SaveRoleRequest{RoleName="Role screen pilot",Description="Synthetic role",Permissions=modules};var role=await repo.SaveRoleAsync(request,1);
  var detail=await repo.RoleDetailsAsync(role,1);Check(detail.Role.IsActive&&detail.Permissions.Single(p=>p.ModuleCode=="DASHBOARD").Allows("VIEW"),"Role creation saves metadata and authoritative permissions atomically");
  await Reject(()=>repo.SaveRoleAsync(request,1),"Tenant role names are unique");await Reject(()=>repo.SaveRoleAsync(new(){RoleName="Main Owner",Permissions=modules},1),"Owner identity cannot be an assignable role");
  var protectedRole=(await repo.RoleDirectoryAsync(1)).First(r=>r.IsProtected);await Reject(()=>repo.SetRolePermissionsAsync(protectedRole.RoleId,modules,1),"Legacy permission endpoint also protects owner/system roles");
  var custom=await repo.SaveUserAsync(null,new(){FullName="Role pilot staff",Username="role.pilot",Email="role.pilot@example.test",MobileNumber="9876543210",RoleId=role,Password="RolePilot123!",ConfirmPassword="RolePilot123!",UseRoleTemplate=false,Permissions=[],MustChangePassword=false,StoreAccess=new(){StoreIds=[1],DefaultStoreId=1}},1);
  var user=(await repo.GetUserAsync(custom))!;Check(user.Permissions.Single(p=>p.ModuleCode=="DASHBOARD").Allows("VIEW"),"User registration copies server role permissions even with a forged template flag");
  await Reject(()=>repo.SaveRoleAsync(new(){RoleName="Undelegated role",Permissions=modules},custom),"Staff cannot create roles without delegated management permission");
  detail=await repo.RoleDetailsAsync(role,1);Check(detail.Role.AssignedUsers==1,"Role directory counts assigned users from PostgreSQL");
  var edit=new SaveRoleRequest{RoleId=role,RoleName=detail.Role.RoleName,Description="Revised",ExpectedVersion=detail.Role.Version,ExpectedAssignedUsers=1,Permissions=detail.Permissions.ToList()};
  await Reject(()=>repo.SaveRoleAsync(edit,1),"Assigned-role edits require impact confirmation");edit.ConfirmAssignedUsers=true;edit.RequiresTwoFactor=true;edit.Permissions.Single(p=>p.ModuleCode=="INVENTORY").Actions=["VIEW"];
  await repo.SaveRoleAsync(edit,1);var after=(await repo.GetUserAsync(custom))!;Check(after.SecurityVersion>user.SecurityVersion,"Role changes invalidate assigned sessions");Check(after.UsesRolePermissions&&after.Permissions.Single(p=>p.ModuleCode=="INVENTORY").Allows("VIEW"),"Linked staff immediately follow saved role permissions");
  await Reject(()=>repo.SaveRoleAsync(edit,1),"Stale role saves are rejected");detail=await repo.RoleDetailsAsync(role,1);await Reject(()=>repo.SetRoleStatusAsync(role,new(){ExpectedVersion=detail.Role.Version,IsActive=false},1),"Assigned roles cannot be deactivated");
  var unused=await repo.SaveRoleAsync(new(){RoleName="Unused role pilot",Permissions=modules},1);var unusedDetail=await repo.RoleDetailsAsync(unused,1);await repo.SetRoleStatusAsync(unused,new(){ExpectedVersion=unusedDetail.Role.Version,IsActive=false},1);Check(!(await repo.RoleDetailsAsync(unused,1)).Role.IsActive,"Unused roles deactivate without deletion");
  var count=await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM roles");await Reject(()=>repo.SaveRoleAsync(new(){RoleName="Invalid actions pilot",Permissions=[]},1),"Invalid permissions reject the entire role save");Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM roles")==count,"Failed save leaves no orphan role");
  var scope=factory.Scope!;var tenant=scope.TenantId;scope.TenantId=999;await Reject(()=>repo.RoleDetailsAsync(role,1),"Cross-tenant role access is denied");scope.TenantId=tenant;
  Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE table_name='roles' AND record_id=@role",new{role})>=2,"Role changes retain before/after audit history");
 }
}
