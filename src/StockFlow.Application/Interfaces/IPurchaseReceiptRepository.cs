using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.Application.Interfaces;

public interface IPurchaseReceiptRepository
{
    Task<long> ReceiveAsync(long purchaseId, ReceivePurchaseRequest request, int userId);
    Task<IEnumerable<PurchaseReceiptResponse>> GetAsync(long purchaseId);
}
