using System.ComponentModel.DataAnnotations;
using System.Data;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class RoleTemplateChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    static async Task Reject(Func<Task> work,string message){try{await work();}catch(Exception e)when(e is ValidationException or UnauthorizedAccessException or KeyNotFoundException or DBConcurrencyException or PostgresException){Check(true,message);return;}throw new Exception("Expected rejection: "+message);}
    public static async Task Run(NpgsqlConnection setup,DbConnectionFactory factory)
    {
        var repo=new SecurityRepository(factory);
        var all=(await repo.RoleTemplatesAsync(1)).ToList();Check(all.Count==8&&all.All(t=>t.IsSystem),"Eight recommendations are available without inserting roles or tenant ownership");
        var inventory=await repo.RoleTemplateAsync("inventory-staff",1);
        foreach(var code in new[]{"INVENTORY","STOCK_ADJUSTMENT","REPLENISHMENT","STOCK_TRANSFER","STOCK_OUTFLOW","PRODUCTS","BARCODES","PURCHASES","SUPPLIERS","STOCK_INFLOW"}){
            var permission=inventory.Permissions.Single(p=>p.ModuleCode==code);
            Check(permission.SupportedActions.All(a=>permission.Allows(a)),code+" selects every default child action");
        }
        var cashier=await repo.RoleTemplateAsync("cashier",1);Check(cashier.Permissions.Single(p=>p.ModuleCode=="SALES").Allows("CREATE")&&!cashier.Permissions.Single(p=>p.ModuleCode=="REPORTS").Allows("VIEW"),"Cashier defaults use billing without claiming own-sales-only reporting");
        var blank=(await repo.GetModulesAsync()).ToList();foreach(var p in blank)p.Actions=[];
        var role=await repo.SaveRoleAsync(new(){RoleName="Template cashier test",TemplateKey="cashier",ExpectedTemplateVersion=cashier.Template.Version,CustomizePermissions=false,Permissions=blank},1);
        var created=await repo.RoleDetailsAsync(role,1);Check(created.Permissions.Single(p=>p.ModuleCode=="SALES").Allows("CREATE"),"Server loads authoritative defaults instead of trusting a posted default selection");
        Check(!await setup.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE role_id=@role)",new{role}),"Creating a role from a template does not create or reassign users");
        var key=await repo.SaveRoleTemplateAsync(new(){TemplateName="Custom receiving template",Description="Synthetic template",Permissions=blank},1);var stored=await repo.RoleTemplateAsync(key,1);
        Check(stored.Template.TemplateId>0&&!stored.Template.IsSystem,"Custom templates are persisted separately from assignable roles");
        await Reject(()=>repo.SaveRoleTemplateAsync(new(){TemplateName="Custom receiving template",Permissions=blank},1),"Duplicate business template names are rejected");
        await Reject(()=>repo.SaveRoleTemplateAsync(new(){TemplateName="Main Owner",Permissions=blank},1),"Templates cannot create owner identities");
        var save=new SaveRoleTemplateRequest{TemplateKey="cashier",ExpectedVersion=cashier.Template.Version,TemplateName="Cashier",Description="Reviewed local cashier",Permissions=blank};
        save.Permissions.Single(p=>p.ModuleCode=="DASHBOARD").Actions=["VIEW"];
        await repo.SaveRoleTemplateAsync(save,1);await Reject(()=>repo.SaveRoleTemplateAsync(save,1),"Stale template saves are rejected");
        var revised=await repo.RoleTemplateAsync("cashier",1);Check(!revised.Template.IsSystem&&revised.Template.Version!=cashier.Template.Version,"Editing a recommendation creates a versioned tenant override");
        Check((await repo.RoleDetailsAsync(role,1)).Permissions.Single(p=>p.ModuleCode=="SALES").Allows("CREATE"),"Template edits preserve existing saved role permissions");
        await Reject(()=>repo.SaveRoleAsync(new(){RoleName="Stale template role",TemplateKey="cashier",ExpectedTemplateVersion=cashier.Template.Version,Permissions=blank},1),"Role creation rejects a template that changed after preview");
        var before=await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM role_templates");await Reject(()=>repo.SaveRoleTemplateAsync(new(){TemplateName="Invalid template",Permissions=[]},1),"Templates validate the full catalog before storage");Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM role_templates")==before,"Failed template validation leaves no orphan data");
        save.ExpectedVersion=revised.Template.Version;save.IsActive=false;await repo.SaveRoleTemplateAsync(save,1);var inactive=await repo.RoleTemplateAsync("cashier",1);
        await Reject(()=>repo.SaveRoleAsync(new(){RoleName="Inactive template role",TemplateKey="cashier",ExpectedTemplateVersion=inactive.Template.Version,Permissions=blank},1),"Inactive templates cannot create roles");
        var staff=await repo.SaveUserAsync(null,new(){FullName="Template scoped staff",Username="template.scoped",Email="template.scoped@example.test",MobileNumber="9876543210",RoleId=role,Password="TemplatePilot123!",ConfirmPassword="TemplatePilot123!",StoreAccess=new(){StoreIds=[1],DefaultStoreId=1}},1);
        await Reject(()=>repo.SaveRoleTemplateAsync(new(){TemplateName="Forbidden staff template",Permissions=blank},staff),"Undelegated staff cannot configure templates");
        var originalActor=factory.Scope!.UserId;factory.Scope.UserId=staff;
        await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();Check(await runtime.ExecuteScalarAsync<int>("SELECT count(*) FROM role_templates")==0,"Database RLS hides templates from staff without role access");await Reject(()=>runtime.ExecuteAsync("INSERT INTO role_templates(template_key,template_name) VALUES('forged','Forged staff template')"),"Database RLS rejects direct undelegated template writes");}
        factory.Scope.UserId=originalActor;
        var managerRows=(await repo.GetModulesAsync()).ToList();foreach(var p in managerRows)p.Actions=p.ModuleCode=="ROLE_MANAGEMENT"?["VIEW","MANAGE_PERMISSIONS"]:p.ModuleCode=="DASHBOARD"?["VIEW"]:[];
        var managerRole=await repo.SaveRoleAsync(new(){RoleName="Template delegated manager",StaffAccountType="SHOP_ADMIN",Permissions=managerRows},1);
        var manager=await repo.SaveUserAsync(null,new(){FullName="Template delegated manager",Username="template.manager",Email="template.manager@example.test",MobileNumber="9876543210",RoleId=managerRole,Password="TemplatePilot123!",ConfirmPassword="TemplatePilot123!",StoreAccess=new(){StoreIds=[1],DefaultStoreId=1}},1);
        factory.Scope.UserId=manager;
        var limited=await repo.RoleTemplateAsync("shop-admin",manager);Check(limited.OmittedActions>0&&limited.Permissions.Where(p=>p.Actions!.Length>0).All(p=>p.ModuleCode=="DASHBOARD"),"Recommended defaults are clipped to the manager's actual grant authority");
        var limitedRole=await repo.SaveRoleAsync(new(){RoleName="Limited recommended role",StaffAccountType="EMPLOYEE",TemplateKey="shop-admin",ExpectedTemplateVersion=limited.Template.Version,CustomizePermissions=false,Permissions=blank},manager);
        Check((await repo.RoleDetailsAsync(limitedRole,manager)).Permissions.Where(p=>p.Actions!.Length>0).All(p=>p.ModuleCode=="DASHBOARD"),"Default role creation cannot escalate a delegated manager's permissions");
        await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();await Reject(()=>runtime.ExecuteAsync("INSERT INTO role_template_action_permissions(template_id,module_action_id) SELECT @id,a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='INVENTORY' AND a.action_code='VIEW'",new{id=stored.Template.TemplateId}),"Database policies reject an undelegated action even with template management access");}
        factory.Scope.UserId=originalActor;
        await using(var runtime=factory.CreateConnection()){await runtime.OpenAsync();await Reject(()=>runtime.ExecuteAsync("INSERT INTO role_template_action_permissions(template_id,module_action_id) SELECT @id,a.module_action_id FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='PRODUCTS' AND a.action_code='CREATE'",new{id=stored.Template.TemplateId}),"Deferred database guard rejects template actions without VIEW");}
        var tenant=factory.Scope.TenantId;factory.Scope.TenantId=999;await Reject(()=>repo.RoleTemplateAsync(key,1),"Cross-tenant custom templates are unavailable");factory.Scope.TenantId=tenant;
        Check(await setup.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE table_name='role_templates' AND action_name='ROLE_DEFAULT_TEMPLATE_SAVE'")>=3,"Template saves retain audit provenance independently of roles");
    }
}
