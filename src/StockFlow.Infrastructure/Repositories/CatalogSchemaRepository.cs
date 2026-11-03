using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class CatalogSchemaRepository(DbConnectionFactory factory, ICategoryRepository categories) : ICatalogSchemaRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<CatalogSchema> GetAsync()
    {
        using var c = factory.CreateConnection();
        if (!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.catalog_attribute_schemas') IS NOT NULL")) return new();
        var row = await c.QuerySingleOrDefaultAsync<(long Version, string Definition)>("""
            SELECT version AS Version,definition::text AS Definition FROM catalog_attribute_schemas
            WHERE tenant_id=sf_tenant() AND business_profile_code=sf_current_business_profile()
            """);
        var result = row.Definition is null ? new CatalogSchema() : JsonSerializer.Deserialize<CatalogSchema>(row.Definition, Json)!;
        result.Available = true;
        result.CategoryDetailsAvailable=await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='display_order')");
        result.RegistrationAvailable = await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_save_category_configuration(integer,jsonb)') IS NOT NULL");
        result.Version = row.Version;
        result.LockedCategoryIds = (await c.QueryAsync<int>("SELECT * FROM sf_catalog_locked_categories()")).ToList();
        result.InheritanceAvailable = await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='inherit_parent_fields')");
        if (result.InheritanceAvailable) CategoryFieldInheritance.Resolve(result, await categories.GetAllAsync());
        return result;
    }
    public async Task SaveAsync(CatalogSchema schema)
    {
        var all = (await categories.GetAllAsync()).ToList();
        CatalogSchemaValidator.ValidateDefinition(schema, all.Select(c => c.CategoryId).ToHashSet());
        // Effective rules are derived from trusted hierarchy data, never from the posted document.
        schema.EffectiveCategories = null;
        if ((await GetAsync()).InheritanceAvailable) CategoryFieldInheritance.Resolve(schema, all);
        if (factory.Scope?.CatalogRegistrationEnhancementsAvailable != true && (schema.Attributes.Any(a => a.Type == "TEXT_LIST") ||
            schema.Categories.Any(c => c.Fields.Any(f => !f.Visible || !f.Searchable || f.DefaultValue is not null))))
            throw new ValidationException("Apply migration 052 manually before using visibility, search, defaults or multiple-value fields.");
        if (!(await GetAsync()).Available) throw new ValidationException("Apply manual migration 048 before configuring category attributes.");
        using var c = factory.CreateConnection();
        // SQL function also enforces tenant-wide locks, category identity and optimistic concurrency.
        var definition = factory.Scope?.CatalogRegistrationEnhancementsAvailable == true
            ? JsonSerializer.Serialize(new { schema.Attributes, schema.Categories }, Json)
            : JsonSerializer.Serialize(new { schema.Attributes, Categories = schema.Categories.Select(category => new { category.CategoryId,
                Fields = category.Fields.Select(field => new { field.Code, field.Required, field.VariantAxis }) }) }, Json);
        await c.ExecuteScalarAsync<long>("SELECT sf_save_catalog_schema(CAST(@Definition AS jsonb),@Version)", new { Definition = definition, schema.Version });
    }
}
