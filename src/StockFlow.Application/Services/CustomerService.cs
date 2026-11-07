using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Customers;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Services;

public class CustomerService : ICustomerService
{
    private static readonly System.Text.Json.JsonSerializerOptions AddressJson = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly ICustomerRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public CustomerService(ICustomerRepository repository)
    {
        this.repository = repository;
    }

    public async Task<IEnumerable<CustomerResponse>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        return (await repository.GetAllAsync(activeOnly, term)).Select(Map);
    }

    public async Task<CustomerResponse?> GetByIdAsync(int id)
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

    public async Task<int> CreateAsync(CreateCustomerRequest request, bool preventIdentifierDuplicates = false)
    {
        await ValidateAsync(request, null);
        return await repository.CreateAsync(ToEntity(request), preventIdentifierDuplicates);
    }

    public async Task<bool> UpdateAsync(int id, UpdateCustomerRequest request)
    {
        if (await repository.GetByIdAsync(id) is not {} existing)
        {
            return false;
        }

        await ValidateAsync(request, id);
        var entity = ToEntity(request);
        if(request.UseStructuredAddress != true && request.AddressDetails is null && (request.Address is null || request.Address==existing.Address))
        {
            entity.Address=existing.Address;entity.AddressDetailsJson=existing.AddressDetailsJson;
        }
        entity.CustomerId = id;
        entity.IsActive = request.IsActive;
        return await repository.UpdateAsync(entity);
    }

    public Task<bool> DeleteAsync(int id)
    {
        return repository.DeleteAsync(id);
    }

    private Task ValidateAsync(CreateCustomerRequest request, int? id)
    {
        request.CustomerName = request.CustomerName?.Trim() ?? "";
        if(request.AddressDetails is not null || request.UseStructuredAddress==true) request.Address=request.AddressDetails?.NormalizeAndFormat();
        if (!request.CustomerName.Any(char.IsLetter))
        {
            throw new ValidationException("Customer name must contain letters.");
        }
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        return Task.CompletedTask;
    }

    private static Customer ToEntity(CreateCustomerRequest request)
    {
        return new()
        {
            CustomerName = request.CustomerName.Trim(),
            MobileNumber = string.IsNullOrWhiteSpace(request.MobileNumber) ? null : request.MobileNumber.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            AddressDetailsJson = request.AddressDetails is null || request.Address is null ? null : System.Text.Json.JsonSerializer.Serialize(request.AddressDetails,AddressJson),
            GstNumber = string.IsNullOrWhiteSpace(request.GstNumber) ? null : request.GstNumber.Trim(),
            IsActive = true
        };
    }

    private static CustomerResponse Map(Customer entity)
    {
        return new()
        {
            CustomerId = entity.CustomerId,
            CustomerName = entity.CustomerName,
            MobileNumber = entity.MobileNumber,
            Email = entity.Email,
            Address = entity.Address,
            AddressDetails = string.IsNullOrWhiteSpace(entity.AddressDetailsJson) ? null : System.Text.Json.JsonSerializer.Deserialize<StockFlow.Application.Common.AddressComponents>(entity.AddressDetailsJson,AddressJson),
            GstNumber = entity.GstNumber,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
}
