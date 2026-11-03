using StockFlow.Application.DTOs.Products;

namespace StockFlow.Application.Interfaces;

public interface IProductService
{
    Task ValidateRegistrationAsync(CreateProductRequest request);
    Task<IEnumerable<ProductResponse>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<ProductResponse?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateProductRequest request);
    Task<bool> UpdateAsync(int id, UpdateProductRequest request);
    Task<bool> DeleteAsync(int id);
    Task<ProductResponse?> GetByBarcodeAsync(string barcode);
    Task<CatalogProductDraft?> LookupCatalogAsync(string barcode, int? supplierId);
    Task<ProductImage?> GetImageAsync(int id);
    Task<ProductImage?> GetGalleryImageAsync(int productId, Guid imageId);
}
