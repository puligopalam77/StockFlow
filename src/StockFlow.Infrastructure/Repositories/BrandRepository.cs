using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class BrandRepository : IBrandRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public BrandRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<bool> RegistrationDetailsAvailableAsync(){using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_brand_registration_details()') IS NOT NULL");}
    private const string SelectSql = "SELECT t.brand_id AS BrandId, to_jsonb(t)->>'official_website' AS OfficialWebsite, (to_jsonb(t)->>'updated_at')::timestamptz AS UpdatedAt, COALESCE(to_jsonb(t)->'brand_logo','null'::jsonb)<>'null'::jsonb AS HasLogo, (SELECT count(*)::integer FROM products p WHERE p.tenant_id=t.tenant_id AND p.brand_id=t.brand_id) AS ProductCount, t.brand_code AS BrandCode, ARRAY(SELECT jsonb_array_elements_text(COALESCE(to_jsonb(t)->'aliases','[]'::jsonb))) AS Aliases, t.brand_name AS BrandName, t.description AS Description, t.is_active AS IsActive, t.created_at AS CreatedAt, ARRAY(SELECT bc.category_id FROM brand_categories bc WHERE bc.brand_id=t.brand_id AND bc.tenant_id=t.tenant_id ORDER BY bc.category_id) AS CategoryIds FROM brands t";
    public async Task<IEnumerable<Brand>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        using var connection = factory.CreateConnection();
        return await connection.QueryAsync<Brand>(SelectSql + " WHERE (" + (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? "TRUE" : "EXISTS(SELECT 1 FROM brand_categories bc JOIN categories c ON c.category_id=bc.category_id WHERE bc.brand_id=t.brand_id AND bc.tenant_id=t.tenant_id AND COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))") + ") AND (NOT @ActiveOnly OR t.is_active) AND (@Term IS NULL OR t.brand_code ILIKE '%' || @Term || '%' OR t.description ILIKE '%' || @Term || '%' OR t.brand_name ILIKE '%' || @Term || '%' OR COALESCE(to_jsonb(t)->'aliases','[]'::jsonb)::text ILIKE '%' || @Term || '%') ORDER BY t.brand_name, t.brand_id;",
new
{
    ActiveOnly = activeOnly,
    Term = string.IsNullOrWhiteSpace(term) ? null : term.Trim()
});
    }

    public async Task<Brand?> GetByIdAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Brand>(SelectSql.Replace(" FROM brands t",", (to_jsonb(t)->'brand_logo')::text AS LogoJson FROM brands t") + " WHERE t.brand_id = @Id;", new
        {
            Id = id
        });
    }

    public async Task<int> CreateAsync(Brand entity)
    {
        using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        var id = await connection.ExecuteScalarAsync<int>("INSERT INTO brands (brand_name, description, is_active) VALUES (@BrandName, @Description, @IsActive) RETURNING brand_id;",
entity,
transaction);
        await connection.ExecuteAsync("INSERT INTO brand_categories(tenant_id,brand_id,category_id) SELECT tenant_id,brand_id,unnest(@CategoryIds) FROM brands WHERE brand_id=@Id",
new
{
    Id = id,
    entity.CategoryIds
},
transaction);
        if (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true)
            await connection.ExecuteAsync("UPDATE brands SET aliases=@Aliases WHERE brand_id=@BrandId", new { entity.Aliases, BrandId = entity.BrandId == 0 ? id : entity.BrandId }, transaction);
        if(await RegistrationDetailsAvailableAsync())await connection.ExecuteAsync("UPDATE brands SET brand_code=COALESCE(NULLIF(@BrandCode,''),brand_code),official_website=@OfficialWebsite,brand_logo=CAST(@LogoJson AS jsonb) WHERE brand_id=@BrandId",new{entity.BrandCode,entity.OfficialWebsite,entity.LogoJson,BrandId=id},transaction);
        await transaction.CommitAsync();
        return id;
    }

    public async Task<bool> UpdateAsync(Brand entity)
    {
        using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(connection, transaction);
        var changed = await connection.ExecuteAsync("UPDATE brands SET brand_name = @BrandName, description = @Description, is_active = @IsActive WHERE brand_id = @BrandId;",
entity,
transaction) > 0;
        if (changed)
        {
            // Insert first, then remove only unselected assignments; retained mappings remain intact.
            await connection.ExecuteAsync("INSERT INTO brand_categories(tenant_id,brand_id,category_id) SELECT b.tenant_id,b.brand_id,ids.category_id FROM brands b CROSS JOIN unnest(@CategoryIds) ids(category_id) WHERE b.brand_id=@BrandId AND NOT EXISTS(SELECT 1 FROM brand_categories bc WHERE bc.brand_id=b.brand_id AND bc.tenant_id=b.tenant_id AND bc.category_id=ids.category_id) ON CONFLICT DO NOTHING",
entity,
transaction);
            await connection.ExecuteAsync("DELETE FROM brand_categories WHERE brand_id=@BrandId AND NOT(category_id=ANY(@CategoryIds))", entity, transaction);
        }

        if (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true)
            await connection.ExecuteAsync("UPDATE brands SET aliases=@Aliases WHERE brand_id=@BrandId", new { entity.Aliases, BrandId = entity.BrandId }, transaction);
        if(changed&&await RegistrationDetailsAvailableAsync())
            await connection.ExecuteAsync("UPDATE brands SET brand_code=COALESCE(NULLIF(@BrandCode,''),brand_code),official_website=@OfficialWebsite,brand_logo=CASE WHEN @RemoveLogo THEN NULL WHEN @LogoJson IS NOT NULL THEN CAST(@LogoJson AS jsonb) ELSE brand_logo END WHERE brand_id=@BrandId",entity,transaction);
        await transaction.CommitAsync();
        return changed;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE brands SET is_active = FALSE WHERE brand_id = @Id;", new
        {
            Id = id
        }) > 0;
    }

    public Task<bool> NameExistsAsync(string name, int? excludingId = null)
    {
        return ExistsAsync("(LOWER(regexp_replace(BTRIM(t.brand_name),'\\s+',' ','g')) = LOWER(regexp_replace(BTRIM(@Value),'\\s+',' ','g')) OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(COALESCE(to_jsonb(t)->'aliases','[]'::jsonb)) a WHERE lower(btrim(a))=lower(@Value)))", name, excludingId);
    }

    private async Task<bool> ExistsAsync(string predicate, string value, int? excludingId)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>($"SELECT EXISTS(SELECT 1 FROM brands t WHERE {predicate} AND (@ExcludingId IS NULL OR t.brand_id <> @ExcludingId));",
new
{
    Value = value,
    ExcludingId = excludingId
});
    }
}
