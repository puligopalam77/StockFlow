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

    public async Task<SupplierPage<SupplierResponse>> GetPageAsync(string? term, bool? active, int offset, int limit)
    {
        if (offset < 0 || !new[] { 10, 25, 50, 100 }.Contains(limit) || term?.Length > 200)
            throw new ValidationException("Choose a valid supplier search and page size.");
        var page = await repository.GetPageAsync(term, active, offset, limit);
        return new(page.Items.Select(Map).ToList(), page.Total);
    }

    public Task<bool> SetActiveAsync(int id, bool active) => repository.SetActiveAsync(id, active);

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
        if (request.UseStructuredAddress is null && request.District is null) entity.District = existing.District;
        if (request.UseStructuredAddress != true && !existing.UseStructuredAddress &&
            request.Address == SupplierRules.Clean(existing.Address)) entity.Address = existing.Address;
        // Older clients do not send structured fields. Preserve them on an ordinary edit.
        if (request.UseStructuredAddress is null && existing.UseStructuredAddress &&
            (request.Address is null || request.Address == existing.Address))
        {
            entity.UseStructuredAddress = true;
            entity.AddressLine1 = existing.AddressLine1;
            entity.AddressLine2 = existing.AddressLine2;
            entity.District = existing.District;
            entity.Address = existing.Address;
        }
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
            UseStructuredAddress = request.UseStructuredAddress == true,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            District = request.District,
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
            UseStructuredAddress = entity.UseStructuredAddress,
            AddressLine1 = entity.AddressLine1,
            AddressLine2 = entity.AddressLine2,
            District = entity.District,
            GstNumber = entity.GstNumber,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
}
