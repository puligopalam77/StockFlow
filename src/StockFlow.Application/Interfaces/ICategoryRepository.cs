using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface ICategoryRepository
{
    Task<IEnumerable<Category>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<Category?> GetByIdAsync(int id);
    Task<int> CreateAsync(Category entity);
    Task<bool> UpdateAsync(Category entity);
    Task<bool> DeleteAsync(int id);
    Task<bool> NameExistsAsync(string name, int? excludingId = null);
}
