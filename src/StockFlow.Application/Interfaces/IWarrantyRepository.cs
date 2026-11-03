using StockFlow.Application.DTOs.Warranty;

namespace StockFlow.Application.Interfaces;

public interface IWarrantyRepository
{
    Task<WarrantyLookupResponse?> GetByIdAsync(long id);
    Task<WarrantyLookupResponse?> GetBySerialAsync(string serial);
    Task<WarrantyLookupResponse?> GetByUnitAsync(long unitId);
    Task<IEnumerable<WarrantyLookupResponse>> GetByInvoiceAsync(string invoice);
    Task<IEnumerable<WarrantyLookupResponse>> GetExpiringAsync(int days, int offset);
    Task<IEnumerable<WarrantyClaimResponse>> GetClaimsAsync(long? warrantyId, string? status, int offset);
    Task<IEnumerable<WarrantyEventResponse>> GetHistoryAsync(long warrantyId, int offset);
    Task<long> RegisterAsync(long saleItemId, int userId);
    Task<long> CreateClaimAsync(CreateWarrantyClaimRequest request, int userId);
    Task<bool> ResolveClaimAsync(long claimId, ResolveWarrantyClaimRequest request, int userId);
    Task<bool> SetCoverageAsync(long warrantyId, WarrantyCoverageRequest request, int userId);
}
