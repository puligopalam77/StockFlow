using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;

namespace StockFlow.MasterDataChecks;
public sealed class FakeCatalogSchemaRepository(ICategoryRepository categories) : ICatalogSchemaRepository
{
    public CatalogSchema Schema { get; set; } = new();
    public Task<CatalogSchema> GetAsync() => Task.FromResult(JsonSerializer.Deserialize<CatalogSchema>(JsonSerializer.Serialize(Schema))!);
    public async Task SaveAsync(CatalogSchema schema)
    {
        CatalogSchemaValidator.ValidateDefinition(schema,(await categories.GetAllAsync()).Select(c=>c.CategoryId).ToHashSet());
        if(!Schema.Available)throw new ValidationException("Apply migration 048 first.");
        if(schema.Version!=Schema.Version)throw new ValidationException("Settings changed; reload.");
        schema.Available=true;schema.Version++;Schema=schema;
    }
}
