using StockFlow.Application.DTOs.Products;
namespace StockFlow.Application.Interfaces;
public interface ICatalogSchemaRepository
{
    Task<CatalogSchema> GetAsync();
    Task SaveAsync(CatalogSchema schema);
}
