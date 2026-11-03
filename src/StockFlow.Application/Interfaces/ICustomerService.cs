using StockFlow.Application.DTOs.Customers;

namespace StockFlow.Application.Interfaces;

public interface ICustomerService
{
    Task<IEnumerable<CustomerResponse>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<CustomerResponse?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreateCustomerRequest request, bool preventIdentifierDuplicates = false);
    Task<bool> UpdateAsync(int id, UpdateCustomerRequest request);
    Task<bool> DeleteAsync(int id);
}
