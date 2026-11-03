using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.Application.Interfaces;

public interface IPurchaseService
{
    Task<long> CreateAsync(CreatePurchaseRequest request, int userId);
    Task<bool> UpdateDraftAsync(long id, CreatePurchaseRequest request, int userId);
    Task<bool> CompleteAsync(long id, int userId);
    Task<IEnumerable<PurchaseResponse>> GetAllAsync();
    Task<PurchaseResponse?> GetByIdAsync(long id);
    Task<PurchaseAttachment?> GetAttachmentAsync(long id);
}
