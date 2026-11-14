using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class StaffRegistrationChecks
{
    static void Check(bool ok,string text) { if(!ok)throw new Exception(text); Console.WriteLine("PASS: "+text); }
    static async Task Reject(Func<Task> work,string text) { try { await work(); } catch(Exception e) when(e is ValidationException or UnauthorizedAccessException or KeyNotFoundException or PostgresException) { Check(true,text);return; } throw new Exception("Expected rejection: "+text); }
    public static async Task Run(NpgsqlConnection setup,DbConnectionFactory factory)
    {
        var repo=new SecurityRepository(factory); var scope=factory.Scope!; var originalActor=scope.UserId;
        var preflight=(IDictionary<string,object>)await setup.QueryFirstAsync(await File.ReadAllTextAsync("database/queries/staff_registration_preflight.sql"));
        Check(preflight.Count==8&&preflight.Values.All(v=>v is true),"All eight manual staff registration preflight checks are ready");
        async Task<List<ModulePermission>> Grants(bool delegated=false,bool stock=false) {
            var rows=(await repo.GetModulesAsync()).ToList();
            foreach(var p in rows)p.Actions=p.ModuleCode=="DASHBOARD"?["VIEW"]:stock&&p.ModuleCode=="INVENTORY"?["VIEW"]:delegated&&p.ModuleCode=="USER_MANAGEMENT"?p.SupportedActions.Where(a=>new[]{"VIEW","CREATE","EDIT","DEACTIVATE","RESET_PASSWORD","MANAGE_PERMISSIONS"}.Contains(a)).ToArray():delegated&&p.ModuleCode=="STORES"?["VIEW","MANAGE_ACCESS"]:[];
            return rows;
        }
        var employeeRole=await repo.SaveRoleAsync(new(){RoleName="Shop Admin is only a display label",StaffAccountType="EMPLOYEE",Permissions=await Grants()},1);
        var adminRole=await repo.SaveRoleAsync(new(){RoleName="Delegated supervisor",StaffAccountType="SHOP_ADMIN",Permissions=await Grants(true)},1);
        var strongRole=await repo.SaveRoleAsync(new(){RoleName="Unassignable inventory role",Permissions=await Grants(stock:true)},1);
        var shops=await new BranchRepository(factory,scope).CreateStoreAsync(new(){StoreCode="STAFF-OTHER",StoreName="Staff test other shop"});
        SaveSecurityUserRequest Request(string username,int role,long shop=1,string type="EMPLOYEE")=>new(){FullName="Synthetic staff account",Username=username,Email=username+"@example.test",MobileNumber="9876543210",AccountType=type,RoleId=role,Password="StaffRegistration123!",ConfirmPassword="StaffRegistration123!",MustChangePassword=false,StoreAccess=new(){StoreIds=[shop],DefaultStoreId=shop}};
        var admin=await repo.SaveUserAsync(null,Request("staff.supervisor",adminRole,type:"SHOP_ADMIN"),1);
        var employee=await repo.SaveUserAsync(null,Request("staff.employee",employeeRole),1);
        var other=await repo.SaveUserAsync(null,Request("staff.other",employeeRole,shops),1);
        var stronger=await repo.SaveUserAsync(null,Request("staff.stronger",strongRole),1);
        var otherTenant=await setup.ExecuteScalarAsync<long>("INSERT INTO tenants(tenant_code,business_name) VALUES('STAFF-ISOLATION','Synthetic other tenant') RETURNING tenant_id");
        var foreignShop=await setup.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(@otherTenant,'FOREIGN-STAFF','Other tenant shop') RETURNING store_id",new{otherTenant});
        var foreignRole=await setup.ExecuteScalarAsync<int>("INSERT INTO roles(tenant_id,role_name) VALUES(@otherTenant,'Other tenant staff role') RETURNING role_id",new{otherTenant});
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.foreigntenant",employeeRole,foreignShop),1),"Owner cannot assign an actual shop belonging to another tenant");
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.foreignrole",foreignRole),1),"Owner cannot assign an actual role belonging to another tenant");
        Check((await repo.GetUserAsync(admin))!.AccountType=="SHOP_ADMIN","Owner creates a classified Shop Admin without assigning ownership");
        Check(!(await repo.GetUserAsync(admin))!.IsTenantOwner,"Shop Admin classification never grants business ownership");
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.mismatch",employeeRole,type:"SHOP_ADMIN"),1),"Role compatibility uses classification instead of its display name");
        var all=await repo.StaffRegistrationOptionsAsync(1);Check(all.CanCreateShopAdmin&&all.CanUseAdvancedAccess&&all.Roles.Any(r=>r.RoleId==adminRole),"Owner options contain actual grantable classified roles and authorized shops");
        var byEmail=(await repo.GetUsersAsync("staff.employee@example.test",0,1,"EMPLOYEE",employeeRole,true)).ToList();Check(byEmail.Count==1&&byEmail[0].UserId==employee,"Email, shop, account type, role and status filters combine on the server");
        scope.UserId=admin;
        var choices=await repo.StaffRegistrationOptionsAsync(admin);Check(!choices.CanCreateShopAdmin&&!choices.CanUseAdvancedAccess&&choices.Stores.Count==1&&choices.Stores[0].StoreId==1&&!choices.Roles.Any(r=>r.RoleId==strongRole),"Delegated options exclude other shops, Shop Admin roles and actions beyond authority");
        var delegated=await repo.SaveUserAsync(null,Request("staff.delegated",employeeRole),admin);Check(delegated>0,"Delegated Shop Admin creates an Employee in their assigned shop");
        await using(var runtime=factory.CreateConnection()) {
            await Reject(()=>runtime.ExecuteAsync("UPDATE users SET is_tenant_owner=true WHERE user_id=@delegated",new{delegated}),"Direct runtime SQL cannot promote staff to business ownership");
            await Reject(()=>runtime.ExecuteAsync("UPDATE roles SET staff_account_type='SHOP_ADMIN' WHERE role_id=@employeeRole",new{employeeRole}),"Direct runtime SQL cannot reclassify a role without owner authorization");
            await Reject(()=>runtime.ExecuteAsync("INSERT INTO user_store_access(user_id,store_id,is_default) VALUES(@delegated,@shops,false)",new{delegated,shops}),"Database guard rejects direct cross-shop assignment");
        }
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.forbiddenadmin",adminRole,type:"SHOP_ADMIN"),admin),"Shop Admin cannot create another Shop Admin");
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.forbiddenshop",employeeRole,shops),admin),"Shop Admin cannot create staff in another shop");
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.forgedshop",employeeRole,long.MaxValue),admin),"Forged or cross-tenant shop IDs are rejected");
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.strongrole",strongRole),admin),"Shop Admin cannot assign a role above their actual permissions");
        Check(await repo.GetUserAsync(other) is null,"Other-shop staff details are unavailable to a delegated Shop Admin");
        Check(await repo.GetUserAsync(stronger) is null,"Staff with permissions above the manager are not manageable even in the same shop");
        Check(!(await repo.GetUsersAsync(null,0)).Any(u=>u.UserId==other),"Other-shop staff are absent from the delegated user list");
        await Reject(()=>repo.ResetPasswordAsync(other,new(){Password="NewStaffRegistration123!",ConfirmPassword="NewStaffRegistration123!",ExpectedVersion=1},admin),"Delegated password reset cannot target another shop");
        scope.UserId=employee;
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.employeecreate",employeeRole),employee),"Employee cannot create users");
        scope.UserId=1;
        scope.UserId=0;
        var login=await repo.LoginAsync(new(){TenantCode="DEFAULT",Username="staff.supervisor",Password="StaffRegistration123!"});
        Check(login.Success&&login.UserId==admin,"Existing login and automatic default-shop routing work after the registration migration");
        scope.UserId=1;
        var prior=(await repo.GetUserAsync(employee))!;var update=Request("staff.employee",employeeRole,shops);update.ExpectedVersion=prior.SecurityVersion;update.Password=null;update.ConfirmPassword=null;
        await repo.SaveUserAsync(employee,update,1);var moved=(await repo.GetUserAsync(employee))!;
        Check(moved.StoreAccess.StoreIds.SequenceEqual([shops])&&moved.SecurityVersion>prior.SecurityVersion,"Changing shop removes old-shop access and invalidates prior sessions");
        await repo.SetStatusAsync(employee,new(){IsActive=false,ExpectedVersion=moved.SecurityVersion},1);
        Check((await repo.GetUserAsync(employee)) is {IsActive:false} disabled&&disabled.SecurityVersion>moved.SecurityVersion,"Deactivation preserves the user and invalidates sessions");
        var inactive=await new BranchRepository(factory,scope).CreateStoreAsync(new(){StoreCode="STAFF-INACTIVE",StoreName="Inactive staff shop",IsActive=false});
        await Reject(()=>repo.SaveUserAsync(null,Request("staff.inactive",employeeRole,inactive),1),"Inactive shops cannot receive staff");
        var tenant=scope.TenantId;scope.TenantId=999;Check(await repo.GetUserAsync(admin) is null,"Staff queries retain tenant isolation");scope.TenantId=tenant;
        scope.UserId=originalActor;
    }
}
