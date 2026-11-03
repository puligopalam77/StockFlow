using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface IProductRepository
{
    Task<int?> FindRegistrationAsync(Guid requestId,string requestHash) => Task.FromResult<int?>(null);
    async Task<bool> ParentCodeExistsAsync(string code,int? excludingId) => (await GetAllAsync(false)).Any(p=>p.ProductId!=excludingId && string.Equals(p.ParentProductCode,code,StringComparison.OrdinalIgnoreCase));
    Task<StockFlow.Application.DTOs.Products.ProductImage?> GetGalleryImageAsync(int productId, Guid imageId) => Task.FromResult<StockFlow.Application.DTOs.Products.ProductImage?>(null);
    Task<IEnumerable<Product>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<Product?> GetByIdAsync(int id);
    Task<int> CreateAsync(Product entity);
    Task<int> SaveRegistrationAsync(Product entity, List<StockFlow.Application.DTOs.Products.RegistrationVariantRequest> variants)
    {
        throw new NotSupportedException("Combined product registration is unavailable.");
    }

    Task<bool> UpdateAsync(Product entity);
    Task<bool> DeleteAsync(int id);
    Task<Product?> GetByBarcodeAsync(string barcode);
    Task<bool> SkuExistsAsync(string sku, int? excludingId = null);
    Task<bool> BarcodeExistsAsync(string barcode, int? excludingId = null);
    Task<StockFlow.Application.DTOs.Products.CatalogProductDraft?> LookupCatalogAsync(string barcode, int? supplierId)
    {
        return Task.FromResult<StockFlow.Application.DTOs.Products.CatalogProductDraft?>(null);
    }

    Task<StockFlow.Application.DTOs.Products.ProductImage?> GetImageAsync(int id)
    {
        return Task.FromResult<StockFlow.Application.DTOs.Products.ProductImage?>(null);
    }
}
