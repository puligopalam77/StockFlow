using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class LiveRoleAccessChecks
{
    static void Check(bool value,string message) { if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message); }
    public static async Task Run(NpgsqlConnection setup,DbConnectionFactory factory)
    {
        var repo=new SecurityRepository(factory);var scope=factory.Scope!;var actor=scope.UserId;
        var checks=(IDictionary<string,object>)await setup.QueryFirstAsync(await File.ReadAllTextAsync("database/queries/live_staff_role_access_preflight.sql"));
        Check(checks.Count==5&&checks.Values.All(v=>v is true),"All five live role access preflight checks pass");
        async Task<List<ModulePermission>> Grants(string module) {var rows=(await repo.GetModulesAsync()).ToList();foreach(var p in rows)p.Actions=p.ModuleCode==module?["VIEW"]:[];return rows;}
        var role=await repo.SaveRoleAsync(new(){RoleName="Live access pilot",Permissions=await Grants("DASHBOARD")},1);
        SaveSecurityUserRequest User(string username)=>new(){FullName="Ravi Kumar",Username=username,MobileNumber="9876543210",Email=username+"@example.test",RoleId=role,Password="RoleAccessPilot123!",ConfirmPassword="RoleAccessPilot123!",StoreAccess=new(){StoreIds=[1],DefaultStoreId=1}};
        Check(await repo.SuggestStaffUsernameAsync("Ravi Kumar",1)=="ravi.kumar","Staff name produces an editable username suggestion");
        var linked=await repo.SaveUserAsync(null,User("ravi.kumar"),1);
        Check(await repo.SuggestStaffUsernameAsync("Ravi Kumar",1)=="ravi.kumar1","Username suggestion avoids tenant username collisions");
        try { await repo.SaveUserAsync(null,User("ravi.kumar"),1);throw new Exception("Expected duplicate username rejection"); }
        catch(ValidationException e) {Check(e.Message.Contains("username"),"Saving revalidates username uniqueness");}
        Check((await repo.GetUserAsync(linked))!.UsesRolePermissions,"New staff link directly to their saved role");
        Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM user_action_permissions WHERE user_id=@linked",new{linked})==0,"New linked staff do not receive a duplicate permission copy");
        var legacy=await repo.SaveUserAsync(null,User("legacy.access"),1);var before=(await repo.GetUserAsync(legacy))!;
        await repo.SetPermissionsAsync(legacy,new(){ExpectedVersion=before.SecurityVersion,Permissions=await Grants("EXPENSES")},1);
        var custom=(await repo.GetUserAsync(legacy))!;Check(!custom.UsesRolePermissions,"Explicit custom permission editing retains the supported individual-access policy");
        Check(custom.SecurityVersion==before.SecurityVersion+1,"Changing to custom grants preserves the existing single session-version increment");
        var oldVersion=(await repo.GetUserAsync(linked))!.SecurityVersion;
        await repo.SetRolePermissionsAsync(role,await Grants("INVENTORY"),1);
        var after=(await repo.GetUserAsync(linked))!;
        Check(after.Permissions.Single(p=>p.ModuleCode=="INVENTORY").Allows("VIEW")&&!after.Permissions.Single(p=>p.ModuleCode=="DASHBOARD").Allows("VIEW"),"Role edits add and remove effective permissions for linked staff immediately");
        Check(after.SecurityVersion>oldVersion,"Live role permission changes invalidate linked staff sessions");
        var retained=(await repo.GetUserAsync(legacy))!;
        Check(retained.Permissions.Single(p=>p.ModuleCode=="EXPENSES").Allows("VIEW")&&!retained.Permissions.Single(p=>p.ModuleCode=="INVENTORY").Allows("VIEW")&&retained.SecurityVersion==custom.SecurityVersion,"Existing custom grants remain independent of role action changes");
        scope.UserId=linked;
        await using(var runtime=factory.CreateConnection()) {
            Check(await runtime.ExecuteScalarAsync<bool>("SELECT sf_transfer_permission('INVENTORY','VIEW') AND NOT sf_transfer_permission('DASHBOARD','VIEW')"),"Database authorization helpers and application permissions agree on live role access");
        }
        scope.UserId=1;
        await repo.SetRolePermissionsAsync(role,await Grants("DASHBOARD"),1);
        Check(!(await repo.GetUserAsync(linked))!.Permissions.Single(p=>p.ModuleCode=="INVENTORY").Allows("VIEW"),"Removing a role permission revokes linked access");
        Check(!(await repo.GetUserAsync(1))!.UsesRolePermissions,"Protected owner retains its existing access policy");
        scope.UserId=actor;
    }
}
