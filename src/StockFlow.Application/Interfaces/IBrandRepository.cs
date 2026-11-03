using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface IBrandRepository
{
    Task<bool> RegistrationDetailsAvailableAsync();
    Task<IEnumerable<Brand>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<Brand?> GetByIdAsync(int id);
    Task<int> CreateAsync(Brand entity);
    Task<bool> UpdateAsync(Brand entity);
    Task<bool> DeleteAsync(int id);
    Task<bool> NameExistsAsync(string name, int? excludingId = null);
}
