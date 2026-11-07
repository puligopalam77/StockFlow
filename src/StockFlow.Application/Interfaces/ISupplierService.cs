using StockFlow.Application.DTOs.Suppliers;

namespace StockFlow.Application.Interfaces;

public interface ISupplierService
{
    Task<SupplierPage<SupplierResponse>> GetPageAsync(string? term, bool? active, int offset, int limit);
    Task<bool> SetActiveAsync(int id, bool active);
    Task<IEnumerable<SupplierResponse>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<SupplierResponse?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateSupplierRequest request);
    Task<bool> UpdateAsync(int id, UpdateSupplierRequest request);
    Task<bool> DeleteAsync(int id);
}
