using StockFlow.Application.DTOs.Service;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Interfaces;

public interface IServiceRepository
{
    Task<long> CreateAsync(CreateServiceRequest request, int userId);
    Task<ServiceRequestResponse?> GetByIdAsync(long id);
    Task<IEnumerable<ServiceRequestResponse>> GetAllAsync(ServiceSearch search);
    Task UpdateAsync(long id, UpdateServiceRequest request, int userId);
    Task UpdateStatusAsync(long id, UpdateServiceStatusRequest request, int userId);
    Task<long> AddPartAsync(long id, AddServicePartRequest request, int userId);
    Task<IEnumerable<ServiceHistory>> GetHistoryAsync(long id, int offset);
    Task<IEnumerable<ServicePart>> GetPartsAsync(long id, int offset);
    Task<IEnumerable<ServiceTechnician>> GetTechniciansAsync();
    Task<ServiceUnitContext?> GetUnitContextAsync(long unitId);
}
