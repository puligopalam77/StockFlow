using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Suppliers;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Application.Common;

namespace StockFlow.Application.Services;

public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public SupplierService(ISupplierRepository repository)
    {
        this.repository = repository;
    }

    public async Task<IEnumerable<SupplierResponse>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        return (await repository.GetAllAsync(activeOnly, term)).Select(Map);
    }

    public async Task<SupplierResponse?> GetByIdAsync(int id)
    {
        if (await repository.GetByIdAsync(id) is { } entity)
        {
            return Map(entity);
        }
        else
        {
            return null;
        }
    }

    public async Task<int> CreateAsync(CreateSupplierRequest request)
    {
        await ValidateAsync(request, null);
        return await repository.CreateAsync(ToEntity(request));
    }

    public async Task<bool> UpdateAsync(int id, UpdateSupplierRequest request)
    {
        if (await repository.GetByIdAsync(id) is not { } existing)
        {
            return false;
        }

        await ValidateAsync(request, id);
        var entity = ToEntity(request);
        entity.SupplierId = id;
        entity.SupplierCode = existing.SupplierCode;
        entity.IsActive = request.IsActive;
        return await repository.UpdateAsync(entity);
    }

    public Task<bool> DeleteAsync(int id)
    {
        return repository.DeleteAsync(id);
    }

    private Task ValidateAsync(CreateSupplierRequest request, int? id)
    {
        SupplierRules.Validate(request);
        return Task.CompletedTask;
    }

    private static Supplier ToEntity(CreateSupplierRequest request)
    {
        return new()
        {
            SupplierName = request.SupplierName.Trim(),
            SupplierType = request.SupplierType,
            AlternateMobile = request.AlternateMobile,
            CountryCode = request.CountryCode,
            State = request.State,
            City = request.City,
            PostalCode = request.PostalCode,
            PaymentTermsDays = request.PaymentTermsDays,
            Notes = request.Notes,
            ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim(),
            MobileNumber = string.IsNullOrWhiteSpace(request.MobileNumber) ? null : request.MobileNumber.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            GstNumber = string.IsNullOrWhiteSpace(request.GstNumber) ? null : request.GstNumber.Trim(),
            IsActive = true
        };
    }

    private static SupplierResponse Map(Supplier entity)
    {
        return new()
        {
            SupplierId = entity.SupplierId,
            SupplierCode = entity.SupplierCode,
            SupplierType = entity.SupplierType,
            AlternateMobile = entity.AlternateMobile,
            CountryCode = entity.CountryCode,
            State = entity.State,
            City = entity.City,
            PostalCode = entity.PostalCode,
            PaymentTermsDays = entity.PaymentTermsDays,
            Notes = entity.Notes,
            SupplierName = entity.SupplierName,
            ContactPerson = entity.ContactPerson,
            MobileNumber = entity.MobileNumber,
            Email = entity.Email,
            Address = entity.Address,
            GstNumber = entity.GstNumber,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
}
