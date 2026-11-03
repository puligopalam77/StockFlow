using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
static class AdministrationRolloutDatabaseChecks
{
    public static async Task<int> CreatePilot(NpgsqlConnection c,DbConnectionFactory factory)
    {
        var repository=new SecurityRepository(factory);
        var modules=(await repository.GetModulesAsync()).ToList();
        Check(modules.Count==PermissionCatalog.Modules.Count&&modules.All(m=>PermissionCatalog.Modules.ContainsKey(m.ModuleCode)),"active database modules match the latest application catalog");
        Check(modules.Where(m=>m.ModuleCode is "PURCHASES" or "SALES" or "INVENTORY" or "SERVICE" or "USER_MANAGEMENT").All(m=>m.ParentModuleId>0&&!string.IsNullOrWhiteSpace(m.ParentModuleName)),"permission modules read parent module names from the database");
        foreach(var module in modules){var actions=await c.QueryAsync<string>("SELECT action_code FROM module_actions WHERE module_id=@ModuleId AND is_active",module);Check(actions.Order().SequenceEqual(module.SupportedActions.Order()),"database action catalog matches "+module.ModuleCode);}
        var ownerAccess=await repository.GetAccessAsync(1);
        Check(ownerAccess!.NavigationModules.Count(m=>m.ParentModuleId is null)==7&&ownerAccess.NavigationModules.Any(m=>m.ModuleCode=="NEW_SALE"&&m.RequiredPermissions.Contains("SALES:CREATE")),"application reads active module and submodule navigation from the database");
        var role=await c.ExecuteScalarAsync<int>("INSERT INTO roles(tenant_id,role_name,is_active,requires_two_factor) VALUES(1,'Pilot Cashier',true,false) RETURNING role_id");
        foreach(var module in modules)module.Actions=module.ModuleCode switch{"DASHBOARD" or "INVENTORY" or "REPORTS"=>["VIEW"],"SALES" or "CUSTOMERS"=>["VIEW","CREATE"],"SCAN"=>["VIEW","SCAN"],"INVOICES"=>["VIEW","PRINT"],_=>[]};
        await repository.SetRolePermissionsAsync(role,modules,1);
        var template=(await repository.GetRolePermissionsAsync(role)).ToList();
        var id=await repository.SaveUserAsync(null,new(){FullName="Pilot cashier",Username="pilot.cashier",Email="pilot@example.com",MobileNumber="9876543210",UseRoleTemplate=true,RoleId=role,Password="Pilot12!",ConfirmPassword="Pilot12!",MustChangePassword=false,StoreAccess=new(){StoreIds=[1],DefaultStoreId=1},Permissions=[]},1);
        var saved=(await repository.GetUserAsync(id))!;
        Check(saved.StoreAccess.DefaultStoreId==1&&saved.Permissions.Single(p=>p.ModuleCode=="SALES").Allows("CREATE"),"user creation saves the role template and assigned shop together");
        var login=await repository.LoginAsync(new(){TenantCode="DEFAULT",Username="pilot.cashier",Password="Pilot12!"});
        Check(login.Success&&login.StoreId==1&&login.LocationId==1,"new cashier login selects the assigned sales floor");
        if(factory.Scope is {} scope){scope.UserId=1;scope.IsOwner=true;}
        template.Single(p=>p.ModuleCode=="SALES").Actions=["VIEW"];
        await repository.SetRolePermissionsAsync(role,template,1);
        saved=(await repository.GetUserAsync(id))!;
        Check(saved.SecurityVersion==1&&saved.Permissions.Single(p=>p.ModuleCode=="SALES").Allows("CREATE"),"template updates preserve existing user grants and sessions");
        await c.ExecuteAsync("UPDATE users SET require_two_factor=true WHERE user_id=@id",new{id});
        await repository.SaveUserAsync(id,new(){FullName=saved.FullName,Username=saved.Username,Email=saved.Email,MobileNumber=saved.MobileNumber,RoleId=saved.RoleId,IsActive=true,MustChangePassword=false,ExpectedVersion=saved.SecurityVersion,StoreAccess=saved.StoreAccess,UseRoleTemplate=true,RequireTwoFactor=false,Permissions=[]},1);
        saved=(await repository.GetUserAsync(id))!;
        Check(saved.RequireTwoFactor && saved.Permissions.Single(p=>p.ModuleCode=="SALES").Allows("CREATE"),"simplified staff edit preserves MFA and existing custom grants instead of clearing them");
        await c.ExecuteAsync("UPDATE users SET require_two_factor=false WHERE user_id=@id",new{id});
        return id;
    }
    public static async Task ChangeRole(NpgsqlConnection c,DbConnectionFactory factory,int userId)
    {
        var repository=new SecurityRepository(factory);
        var saved=(await repository.GetUserAsync(userId))!;
        var roleId=await c.ExecuteScalarAsync<int>("INSERT INTO roles(tenant_id,role_name,is_active) VALUES(1,'Changed staff role',true) RETURNING role_id");
        var template=(await repository.GetModulesAsync()).ToList();
        foreach(var module in template)module.Actions=module.ModuleCode=="DASHBOARD"?["VIEW"]:[];
        await repository.SetRolePermissionsAsync(roleId,template,1);
        await repository.SaveUserAsync(userId,new(){FullName=saved.FullName,Username=saved.Username,Email=saved.Email,MobileNumber=saved.MobileNumber,RoleId=roleId,IsActive=true,MustChangePassword=false,ExpectedVersion=saved.SecurityVersion,StoreAccess=saved.StoreAccess,UseRoleTemplate=true,Permissions=[]},1);
        var updated=(await repository.GetUserAsync(userId))!;
        Check(updated.RoleId==roleId && template.All(p=>p.Actions!.Order().SequenceEqual(updated.Permissions.Single(x=>x.ModuleId==p.ModuleId).Actions!.Order())),"changing staff role applies server role permissions with no screen-access fields posted");
    }
    public static async Task Revoke(DbConnectionFactory factory,int userId)
    {
        var repository=new SecurityRepository(factory);var saved=(await repository.GetUserAsync(userId))!;
        saved.Permissions.Single(p=>p.ModuleCode=="SALES").Actions=["VIEW"];
        await repository.SetPermissionsAsync(userId,new(){ExpectedVersion=saved.SecurityVersion,Permissions=saved.Permissions},1);
        Check((await repository.GetUserAsync(userId))!.SecurityVersion==saved.SecurityVersion+1,"permission revocation increments the session security version");
        try{await repository.SetPermissionsAsync(userId,new(){ExpectedVersion=saved.SecurityVersion,Permissions=saved.Permissions},1);throw new Exception("Stale edit accepted");}catch(System.ComponentModel.DataAnnotations.ValidationException){Console.WriteLine("PASS stale permission edit is rejected");}
    }
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
}
