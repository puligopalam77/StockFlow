using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface ISupplierRepository
{
    Task<StockFlow.Application.DTOs.Suppliers.SupplierPage<Supplier>> GetPageAsync(string? term, bool? active, int offset, int limit);
    Task<bool> SetActiveAsync(int id, bool active);
    Task<IEnumerable<Supplier>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<Supplier?> GetByIdAsync(int id);
    Task<int> CreateAsync(Supplier entity);
    Task<bool> UpdateAsync(Supplier entity);
    Task<bool> DeleteAsync(int id);
}
