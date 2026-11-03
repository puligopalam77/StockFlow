using StockFlow.Application.DTOs.Brands;

namespace StockFlow.Application.Interfaces;

public interface IBrandService
{
    Task<bool> RegistrationDetailsAvailableAsync();
    Task<BrandLogo?> GetLogoAsync(int id);
    Task<IEnumerable<BrandResponse>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<BrandResponse?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateBrandRequest request);
    Task<bool> UpdateAsync(int id, UpdateBrandRequest request);
    Task<bool> DeleteAsync(int id);
}
