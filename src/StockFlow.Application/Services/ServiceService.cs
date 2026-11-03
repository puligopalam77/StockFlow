using System.ComponentModel.DataAnnotations;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Service;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Services;

public class ServiceService : IServiceService
{
    private readonly IServiceRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public ServiceService(IServiceRepository repository)
    {
        this.repository = repository;
    }

    public Task<long> CreateAsync(CreateServiceRequest request, int userId)
    {
        ServiceRules.Validate(request);
        return repository.CreateAsync(request, userId);
    }

    public async Task<ServiceRequestResponse?> GetByIdAsync(long id)
    {
        var item = await repository.GetByIdAsync(id);
        if (item is not null)
        {
            item.AllowedTransitions = ServiceRules.Transitions(item.Status);
        }

        return item;
    }

    public Task<IEnumerable<ServiceRequestResponse>> GetAllAsync(ServiceSearch search)
    {
        ServiceRules.Validate(search);
        return repository.GetAllAsync(search);
    }

    public Task UpdateAsync(long id, UpdateServiceRequest request, int userId)
    {
        ServiceRules.Validate(request);
        return repository.UpdateAsync(id, request, userId);
    }

    public Task UpdateStatusAsync(long id, UpdateServiceStatusRequest request, int userId)
    {
        ServiceRules.Validate(request);
        return repository.UpdateStatusAsync(id, request, userId);
    }

    public Task<long> AddPartAsync(long id, AddServicePartRequest request, int userId)
    {
        ServiceRules.Validate(request);
        return repository.AddPartAsync(id, request, userId);
    }

    public Task<IEnumerable<ServiceHistory>> GetHistoryAsync(long id, int offset)
    {
        Offset(offset);
        return repository.GetHistoryAsync(id, offset);
    }

    public Task<IEnumerable<ServicePart>> GetPartsAsync(long id, int offset)
    {
        Offset(offset);
        return repository.GetPartsAsync(id, offset);
    }

    public Task<IEnumerable<ServiceTechnician>> GetTechniciansAsync()
    {
        return repository.GetTechniciansAsync();
    }

    public Task<ServiceUnitContext?> GetUnitContextAsync(long unitId)
    {
        return repository.GetUnitContextAsync(unitId);
    }

    static void Offset(int offset)
    {
        if (offset < 0)
        {
            throw new ValidationException("Invalid page offset.");
        }
    }
}
