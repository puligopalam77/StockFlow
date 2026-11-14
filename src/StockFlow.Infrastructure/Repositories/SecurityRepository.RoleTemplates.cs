using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
namespace StockFlow.Infrastructure.Repositories;

public partial class SecurityRepository
{
    private const string TemplateRows = """
        SELECT t.template_id TemplateId,t.template_key TemplateKey,t.template_name TemplateName,t.description,t.is_active IsActive,
        'stored:'||t.version::text||':'||md5(coalesce((SELECT string_agg(p.module_action_id::text,',' ORDER BY p.module_action_id)
          FROM role_template_action_permissions p WHERE p.tenant_id=t.tenant_id AND p.template_id=t.template_id),'')) Version
        FROM role_templates t WHERE t.tenant_id=sf_tenant()
        """;
    private static async Task TemplateSchema(NpgsqlConnection c,NpgsqlTransaction t)
    {
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.role_templates') IS NOT NULL AND to_regclass('public.role_template_action_permissions') IS NOT NULL",transaction:t))
            throw new ValidationException("Role Templates setup is pending. Manually apply database/migrations/097_role_templates.sql after 096, then reload.");
    }
    private static RoleTemplateRow BuiltIn(DefaultRoleTemplates.Definition d) => new() {TemplateKey=d.Key,TemplateName=d.Name,Description=d.Description,IsSystem=true,Version="system-v2",Limitations=d.Limitations};
    private static async Task<RoleTemplateDetails> TemplateDetail(NpgsqlConnection c,NpgsqlTransaction t,string key,int actor,bool allowInactive=false)
    {
        await TemplateSchema(c,t);
        var definition=DefaultRoleTemplates.All.SingleOrDefault(d=>d.Key==key);
        var saved=await c.QuerySingleOrDefaultAsync<RoleTemplateRow>(TemplateRows+" AND t.template_key=@key",new{key},t);
        var header=saved??(definition is not null?BuiltIn(definition):throw new KeyNotFoundException("Template is unavailable in this business."));
        if(!allowInactive&&!header.IsActive)throw new ValidationException("Choose an active role template.");
        List<ModulePermission> permissions;
        if(saved is not null) permissions=(await c.QueryAsync<ModulePermission>(PermissionSql.Replace("effective_user_action_permissions","role_template_action_permissions").Replace("p.user_id","p.template_id"),new{id=saved.TemplateId},t)).ToList();
        else permissions=DefaultRoleTemplates.Apply(definition!,await c.QueryAsync<ModulePermission>(PermissionSql,new{id=0},t));
        header.Limitations=saved is null ? definition!.Limitations : "Template changes affect future roles only. Shop assignments, ownership, financial approvals and feature availability follow existing security rules.";
        var own=(await c.QueryAsync<ModulePermission>(PermissionSql,new{id=actor},t)).ToDictionary(p=>p.ModuleCode);
        var omitted=0;
        foreach(var p in permissions){var before=p.Actions!.Length;p.Actions=p.Actions.Where(a=>own.TryGetValue(p.ModuleCode,out var grant)&&grant.Allows(a)).ToArray();if(!p.Actions.Contains("VIEW"))p.Actions=[];omitted+=before-p.Actions.Length;}
        return new(header,permissions,omitted);
    }
    public async Task<IEnumerable<RoleTemplateRow>> RoleTemplatesAsync(int actor)
    {
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await Require(c,t,actor,"ROLE_MANAGEMENT","VIEW");await TemplateSchema(c,t);
        var saved=(await c.QueryAsync<RoleTemplateRow>(TemplateRows,transaction:t)).ToDictionary(r=>r.TemplateKey);
        return DefaultRoleTemplates.All.Select(d=>saved.Remove(d.Key,out var row)?row:BuiltIn(d)).Concat(saved.Values.OrderBy(r=>r.TemplateName)).ToArray();
    }
    public async Task<RoleTemplateDetails> RoleTemplateAsync(string key,int actor)
    {
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await Require(c,t,actor,"ROLE_MANAGEMENT","VIEW");return await TemplateDetail(c,t,key,actor,true);
    }
    public async Task<string> SaveRoleTemplateAsync(SaveRoleTemplateRequest r,int actor)
    {
        r.TemplateName=r.TemplateName?.Trim()??"";r.Description=string.IsNullOrWhiteSpace(r.Description)?null:r.Description.Trim();Validator.ValidateObject(r,new(r),true);
        if(!r.TemplateName.Any(char.IsLetterOrDigit)||new[]{"admin","owner","main owner"}.Contains(r.TemplateName.ToLowerInvariant()))throw new ValidationException("Choose a staff template name; owner and system identities are reserved.");
        await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
        await c.ExecuteAsync("SET LOCAL lock_timeout='5s'; SELECT pg_advisory_xact_lock(170017)",transaction:t);
        await Require(c,t,actor,"ROLE_MANAGEMENT","MANAGE_PERMISSIONS");await TemplateSchema(c,t);
        RoleTemplateDetails? prior=null;
        if(r.TemplateKey is not null){prior=await TemplateDetail(c,t,r.TemplateKey,actor,true);if(prior.Template.Version!=r.ExpectedVersion)throw new DBConcurrencyException("Template changed. Reload before saving.");if(prior.OmittedActions>0)throw new ValidationException("This template contains grants outside your authority. Ask its owner to update it or create a new template.");}
        var key=r.TemplateKey??"custom-"+Guid.NewGuid().ToString("N");
        if(DefaultRoleTemplates.All.Any(d=>d.Key!=key&&d.Name.Equals(r.TemplateName,StringComparison.OrdinalIgnoreCase))||await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM role_templates WHERE tenant_id=sf_tenant() AND lower(btrim(template_name))=lower(@TemplateName) AND template_key<>@key)",new{r.TemplateName,key},t))throw new ValidationException("Template name already exists in this business.");
        await ValidateGrantablePermissions(c,t,r.Permissions,actor);
        var id=prior?.Template.TemplateId;
        if(id.HasValue)await c.ExecuteAsync("UPDATE role_templates SET template_name=@TemplateName,description=@Description,is_active=@IsActive WHERE tenant_id=sf_tenant() AND template_id=@id",new{r.TemplateName,r.Description,r.IsActive,id},t);
        else id=await c.ExecuteScalarAsync<int>("INSERT INTO role_templates(template_key,template_name,description,is_active) VALUES(@key,@TemplateName,@Description,@IsActive) RETURNING template_id",new{key,r.TemplateName,r.Description,r.IsActive},t);
        await c.ExecuteAsync("DELETE FROM role_template_action_permissions WHERE tenant_id=sf_tenant() AND template_id=@id",new{id},t);
        foreach(var p in r.Permissions)foreach(var action in p.Actions!)await c.ExecuteAsync("INSERT INTO role_template_action_permissions(template_id,module_action_id) SELECT @id,module_action_id FROM module_actions WHERE module_id=@ModuleId AND action_code=@action AND is_active",new{id,p.ModuleId,action},t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,old_values,new_values) VALUES(@actor,'Roles & Permissions','ROLE_DEFAULT_TEMPLATE_SAVE','role_templates',@id,CAST(@prior AS jsonb),CAST(@next AS jsonb))",new{actor,id,prior=prior is null?null:JsonSerializer.Serialize(prior),next=JsonSerializer.Serialize(new{key,r.TemplateName,r.Description,r.IsActive,r.Permissions})},t);
        await t.CommitAsync();return key;
    }
}
