using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.Infrastructure.Repositories;

public partial class SecurityRepository
{
    private static async Task StaffSchema(NpgsqlConnection c, NpgsqlTransaction? t = null)
    {
        if (!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.effective_user_action_permissions') IS NOT NULL", transaction:t))
            throw new ValidationException("Staff access setup is pending. Manually apply migrations 098 and 099_live_staff_role_access.sql in order, then reload.");
    }

    public async Task<string> SuggestStaffUsernameAsync(string fullName,int actor)
    {
        if(string.IsNullOrWhiteSpace(fullName)||fullName.Length>100)throw new ValidationException("Enter a staff name of up to 100 characters.");
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
        await Require(c,t,actor,"USER_MANAGEMENT","CREATE");
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_staff_manager()",transaction:t))throw new UnauthorizedAccessException("Staff management is not delegated.");
        var name=System.Text.RegularExpressions.Regex.Replace(fullName.Trim().ToLowerInvariant(),@"[^\p{L}\p{N}]+",".").Trim('.');
        if(name.Length<3)name="staff"; if(name.Length>40)name=name[..40].TrimEnd('.');
        var suggestion=await c.ExecuteScalarAsync<string?>("SELECT @name||CASE WHEN n=0 THEN '' ELSE n::text END FROM generate_series(0,9999) n WHERE NOT EXISTS(SELECT 1 FROM users WHERE tenant_id=sf_tenant() AND lower(username)=lower(@name||CASE WHEN n=0 THEN '' ELSE n::text END)) ORDER BY n LIMIT 1",new{name},t);
        return suggestion??throw new ValidationException("Enter a different username.");
    }

    public async Task<StaffRegistrationOptions> StaffRegistrationOptionsAsync(int actor)
    {
        await using var c=factory.CreateConnection(); await c.OpenAsync(); await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await StaffSchema(c,t); await Require(c,t,actor,"USER_MANAGEMENT","VIEW");
        var owner=await c.ExecuteScalarAsync<bool>("SELECT sf_owner()",transaction:t);
        var own=(await c.QueryAsync<ModulePermission>(PermissionSql,new{id=actor},t)).ToDictionary(p=>p.ModuleId);
        var result=new StaffRegistrationOptions {
            CanManageStaff=await c.ExecuteScalarAsync<bool>("SELECT sf_staff_manager()",transaction:t) && own.Values.Any(p=>p.ModuleCode=="USER_MANAGEMENT" && p.Allows("CREATE") && p.Allows("MANAGE_PERMISSIONS")) && own.Values.Any(p=>p.ModuleCode=="STORES" && p.Allows("MANAGE_ACCESS")),
            BusinessName=await c.ExecuteScalarAsync<string>("SELECT CASE WHEN t.business_name='Existing business' THEN coalesce((SELECT nullif(btrim(c.company_name),'') FROM company_settings c WHERE c.tenant_id=t.tenant_id AND c.company_settings_id=1),t.business_name) ELSE t.business_name END FROM tenants t WHERE t.tenant_id=sf_tenant()",transaction:t)??"",
            CanCreateShopAdmin=owner,
            CanUseAdvancedAccess=owner && own.Values.Any(p=>p.ModuleCode=="STORES" && p.Allows("MANAGE_ACCESS"))
        };
        var roles=await c.QueryAsync<SecurityRole>("SELECT r.role_id RoleId,r.role_name RoleName,r.staff_account_type StaffAccountType,r.is_active IsActive FROM roles r WHERE r.tenant_id=sf_tenant() AND r.is_active AND lower(btrim(r.role_name)) NOT IN('admin','owner','main owner') AND NOT EXISTS(SELECT 1 FROM users u WHERE u.tenant_id=r.tenant_id AND u.role_id=r.role_id AND u.is_tenant_owner) AND (@owner OR r.staff_account_type='EMPLOYEE') ORDER BY r.role_name",new{owner},t);
        foreach(var role in roles) {
            var permissions=await c.QueryAsync<ModulePermission>(PermissionSql.Replace("effective_user_action_permissions","role_action_permissions").Replace("p.user_id","p.role_id"),new{id=role.RoleId},t);
            if(permissions.All(p=>(p.Actions??[]).All(a=>own.TryGetValue(p.ModuleId,out var grant)&&grant.Allows(a)))) result.Roles.Add(role);
        }
        result.Stores=(await c.QueryAsync<StaffShopOption>("SELECT store_id StoreId,store_name StoreName FROM stores WHERE tenant_id=sf_tenant() AND is_active AND sf_staff_shop(store_id) ORDER BY store_name",transaction:t)).ToList();
        if(result.CanUseAdvancedAccess) result.Warehouses=(await c.QueryAsync<BranchLocation>("SELECT location_id LocationId,location_name LocationName,location_type LocationType,is_active IsActive FROM store_locations WHERE tenant_id=sf_tenant() AND store_id IS NULL AND location_type='WAREHOUSE' AND is_active ORDER BY location_name",transaction:t)).ToList();
        await t.CommitAsync(); return result;
    }

    private static async Task ValidateStaffAssignment(NpgsqlConnection c,NpgsqlTransaction t,SaveSecurityUserRequest r,SecurityUser? old)
    {
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_staff_manager()",transaction:t)) throw new UnauthorizedAccessException("Staff management has not been delegated to this account.");
        var type=await c.ExecuteScalarAsync<string?>("SELECT staff_account_type FROM roles WHERE tenant_id=sf_tenant() AND role_id=@RoleId AND is_active",r,t)
            ??throw new ValidationException("Choose an active role in this business.");
        if(r.AccountType is not null && r.AccountType!=type) throw new ValidationException("The role is not compatible with the selected account type.");
        r.AccountType=type;
        var owner=await c.ExecuteScalarAsync<bool>("SELECT sf_owner()",transaction:t);
        if(!owner && (type!="EMPLOYEE" || old?.AccountType=="SHOP_ADMIN" || old?.IsTenantOwner==true)) throw new UnauthorizedAccessException("Only the business owner can manage Shop Admin accounts.");
        if(old?.IsTenantOwner==true) return; // Ownership is never posted or changed here.
        var access=r.StoreAccess;
        if(access is null || access.StoreIds is null || access.WarehouseLocationIds is null) throw new ValidationException("Choose an assigned shop.");
        if(!owner && (access.StoreIds.Count!=1 || access.WarehouseLocationIds.Count!=0)) throw new UnauthorizedAccessException("Delegated staff management is limited to the assigned shop.");
        if(access.StoreIds.Count==0 || access.DefaultStoreId is null) throw new ValidationException("Choose an active assigned shop and a default from its assignments.");
        foreach(var shop in access.StoreIds)
            if(!await c.ExecuteScalarAsync<bool>("SELECT sf_staff_shop(@shop)",new{shop},t)) throw new UnauthorizedAccessException("Selected shop is inactive or outside your authorized business access.");
    }
}
