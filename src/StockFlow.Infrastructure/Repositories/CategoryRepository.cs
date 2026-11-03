using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public CategoryRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private const string SelectSql = "SELECT to_jsonb(t)->>'sku_prefix' AS SkuPrefix, COALESCE((to_jsonb(t)->>'sku_prefix_used')::boolean,false) AS SkuPrefixUsed, t.category_id AS CategoryId, t.category_code AS CategoryCode, COALESCE((to_jsonb(t)->>'display_order')::integer,0) AS DisplayOrder, t.category_name AS CategoryName, t.parent_category_id AS ParentCategoryId, t.description AS Description, t.is_active AS IsActive, t.created_at AS CreatedAt, COALESCE(to_jsonb(t)->>'business_profile_code','ELECTRICAL') AS BusinessProfileCode, COALESCE((to_jsonb(t)->>'inherit_parent_fields')::boolean,true) AS InheritsParentFields, (SELECT count(*)::integer FROM products p WHERE p.tenant_id=t.tenant_id AND p.category_id=t.category_id) AS ProductCount, (to_jsonb(t)->'registration_defaults')::text AS RegistrationDefaultsJson FROM categories t";
    private string Profile => factory.Scope?.BusinessProfileCode ?? "ELECTRICAL";
    public async Task<IEnumerable<Category>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        using var connection = factory.CreateConnection();
        return await connection.QueryAsync<Category>(SelectSql + " WHERE NOT COALESCE((to_jsonb(t)->>'is_retired_grouping')::boolean,false) AND COALESCE(to_jsonb(t)->>'business_profile_code','ELECTRICAL')=@Profile AND (NOT @ActiveOnly OR t.is_active) AND (@Term IS NULL OR t.category_name ILIKE '%' || @Term || '%' OR t.description ILIKE '%' || @Term || '%') ORDER BY t.category_name, t.category_id;",
new
{
    ActiveOnly = activeOnly,
    Profile,
    Term = string.IsNullOrWhiteSpace(term) ? null : term.Trim()
});
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Category>(SelectSql + " WHERE t.category_id = @Id AND COALESCE(to_jsonb(t)->>'business_profile_code','ELECTRICAL')=@Profile;", new
        {
    Id = id, Profile
        });
    }

    public async Task<int> CreateAsync(Category entity)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var arguments = new DynamicParameters(entity); arguments.Add("Profile", Profile);
        var sql = factory.Scope?.BusinessProfileConfigurationAvailable == true
            ? "INSERT INTO categories(category_name,description,parent_category_id,is_active,business_profile_code) VALUES(@CategoryName,@Description,@ParentCategoryId,@IsActive,@Profile) RETURNING category_id"
            : "INSERT INTO categories(category_name,description,parent_category_id,is_active) VALUES(@CategoryName,@Description,@ParentCategoryId,@IsActive) RETURNING category_id";
        var details=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='display_order')",transaction:transaction);
        if(details){sql=sql.Replace("category_name,description,", "category_name,description,category_code,display_order,").Replace("@CategoryName,@Description,", "@CategoryName,@Description,NULLIF(@CategoryCode,''),COALESCE(@DisplayOrder,0),");}
        var prefixes=await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.catalog_prefix_registry') IS NOT NULL",transaction:transaction);
        if(entity.SkuPrefix is not null&&!prefixes)throw new System.ComponentModel.DataAnnotations.ValidationException("Apply migration 067 before assigning SKU prefixes.");
        if(prefixes)sql=sql.Replace("category_name,description,","category_name,description,sku_prefix,").Replace("@CategoryName,@Description,","@CategoryName,@Description,@SkuPrefix,");
        var id=await connection.ExecuteScalarAsync<int>(sql,arguments,transaction);
        if(entity.ConfigurationJson is not null)
            await connection.ExecuteAsync("SELECT sf_save_category_configuration(@id,CAST(@configuration AS jsonb))",new{id,configuration=entity.ConfigurationJson},transaction);
        await transaction.CommitAsync();return id;
    }

    public async Task<bool> UpdateAsync(Category entity)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var arguments = new DynamicParameters(entity); arguments.Add("Profile", Profile);
        var details=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='display_order')",transaction:transaction);
        var prefixes=await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.catalog_prefix_registry') IS NOT NULL",transaction:transaction);
        if(entity.SkuPrefix is not null&&!prefixes)throw new System.ComponentModel.DataAnnotations.ValidationException("Apply migration 067 before assigning SKU prefixes.");
        if(prefixes)await connection.ExecuteAsync("UPDATE categories SET sku_prefix=COALESCE(@SkuPrefix,sku_prefix) WHERE category_id=@CategoryId AND business_profile_code=@Profile",arguments,transaction);
        var metadata=details?"category_code=COALESCE(NULLIF(@CategoryCode,''),category_code),display_order=COALESCE(@DisplayOrder,display_order),":"";
        var changed=await connection.ExecuteAsync("UPDATE categories SET "+metadata+"category_name=@CategoryName,description=@Description,parent_category_id=@ParentCategoryId,is_active=@IsActive WHERE category_id=@CategoryId AND COALESCE(to_jsonb(categories)->>'business_profile_code','ELECTRICAL')=@Profile",arguments,transaction)>0;
        if(changed&&entity.ConfigurationJson is not null)
            await connection.ExecuteAsync("SELECT sf_save_category_configuration(@id,CAST(@configuration AS jsonb))",new{id=entity.CategoryId,configuration=entity.ConfigurationJson},transaction);
        await transaction.CommitAsync();return changed;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE categories SET is_active = FALSE WHERE category_id = @Id AND COALESCE(to_jsonb(categories)->>'business_profile_code','ELECTRICAL')=@Profile;", new
        {
    Id = id, Profile
        }) > 0;
    }

    public Task<bool> NameExistsAsync(string name, int? excludingId = null)
    {
        return ExistsAsync("LOWER(BTRIM(t.category_name)) = LOWER(@Value)", name, excludingId);
    }

    private async Task<bool> ExistsAsync(string predicate, string value, int? excludingId)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>($"SELECT EXISTS(SELECT 1 FROM categories t WHERE COALESCE(to_jsonb(t)->>'business_profile_code','ELECTRICAL')=@Profile AND {predicate} AND (@ExcludingId IS NULL OR t.category_id <> @ExcludingId));",
new
{
    Value = value,
    Profile,
    ExcludingId = excludingId
});
    }
}
