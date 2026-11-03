using StockFlow.Application.DTOs.Categories;

namespace StockFlow.Application.Interfaces;

public interface ICategoryService
{
    Task<IEnumerable<CategoryResponse>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<CategoryResponse?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateCategoryRequest request);
    Task<bool> UpdateAsync(int id, UpdateCategoryRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> SetStatusAsync(int id, bool active);
}
