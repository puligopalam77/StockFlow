using System.ComponentModel.DataAnnotations;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Warranty;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class WarrantyService : IWarrantyService
{
    private readonly IWarrantyRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public WarrantyService(IWarrantyRepository repository)
    {
        this.repository = repository;
    }

    public Task<WarrantyLookupResponse?> GetByIdAsync(long id)
    {
        return repository.GetByIdAsync(id);
    }

    public Task<WarrantyLookupResponse?> GetBySerialAsync(string serial)
    {
        return repository.GetBySerialAsync(WarrantyRules.Code(serial));
    }

    public Task<WarrantyLookupResponse?> GetByUnitAsync(long unitId)
    {
        return repository.GetByUnitAsync(unitId);
    }

    public Task<IEnumerable<WarrantyLookupResponse>> GetByInvoiceAsync(string invoice)
    {
        return repository.GetByInvoiceAsync(WarrantyRules.Code(invoice));
    }

    public Task<IEnumerable<WarrantyLookupResponse>> GetExpiringAsync(int days, int offset)
    {
        if (days is < 1 or > 365 || offset < 0)
        {
            throw new ValidationException("Choose 1–365 days and a valid offset.");
        }

        return repository.GetExpiringAsync(days, offset);
    }

    public Task<IEnumerable<WarrantyClaimResponse>> GetClaimsAsync(long? warrantyId, string? status, int offset)
    {
        if (offset < 0 || warrantyId <= 0 || status is not (null or "OPEN" or "UNDER_REVIEW" or "APPROVED" or "REJECTED" or "REPLACED" or "CLOSED"))
        {
            throw new ValidationException("Invalid claim filters.");
        }

        return repository.GetClaimsAsync(warrantyId, status, offset);
    }

    public Task<IEnumerable<WarrantyEventResponse>> GetHistoryAsync(long id, int offset)
    {
        if (offset < 0)
        {
            throw new ValidationException("Invalid offset.");
        }

        return repository.GetHistoryAsync(id, offset);
    }

    public Task<long> RegisterAsync(long id, int userId)
    {
        return repository.RegisterAsync(id, userId);
    }

    public Task<long> CreateClaimAsync(CreateWarrantyClaimRequest request, int userId)
    {
        WarrantyRules.Validate(request);
        return repository.CreateClaimAsync(request, userId);
    }

    public Task<bool> ResolveClaimAsync(long id, ResolveWarrantyClaimRequest request, int userId)
    {
        WarrantyRules.Validate(request);
        return repository.ResolveClaimAsync(id, request, userId);
    }

    public Task<bool> SetCoverageAsync(long id, WarrantyCoverageRequest request, int userId)
    {
        WarrantyRules.Validate(request);
        return repository.SetCoverageAsync(id, request, userId);
    }
}
