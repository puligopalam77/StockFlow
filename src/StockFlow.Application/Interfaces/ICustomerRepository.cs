using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface ICustomerRepository
{
    Task<IEnumerable<Customer>> GetAllAsync(bool activeOnly = false, string? term = null);
    Task<Customer?> GetByIdAsync(int id);
    Task<int> CreateAsync(Customer entity, bool preventIdentifierDuplicates = false);
    Task<bool> UpdateAsync(Customer entity);
    Task<bool> DeleteAsync(int id);
}
