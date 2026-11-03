using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Application.Interfaces;

public interface IOperationsRepository
{
    Task<DocumentOperations> SaleAsync(long saleId);
    Task<long> AddCustomerPaymentAsync(long saleId, MoneyReceiptRequest request, int userId);
    Task<long> ReturnSaleAsync(long saleId, SaleReturnRequest request, int userId);
    Task<DocumentOperations> PurchaseAsync(long purchaseId);
    Task<long> AddSupplierPaymentAsync(long purchaseId, MoneyReceiptRequest request, int userId);
    Task<long> ReturnPurchaseAsync(long purchaseId, PurchaseReturnRequest request, int userId);
    Task<long> PostStockCountAsync(StockCountRequest request, int userId);
    Task<DocumentOperations> ServiceAsync(long serviceId);
    Task<long> AddServicePaymentAsync(long serviceId, MoneyReceiptRequest request, int userId);
    Task<long> AddFulfilmentAsync(long saleId, FulfilmentRequest request, int userId);
    Task<long> AddTradeInAsync(long saleId, TradeInRequest request, int userId);
    Task<IEnumerable<OperationRow>> WarrantyRmasAsync(long claimId);
    Task<long> AddWarrantyRmaAsync(long claimId, WarrantyRmaRequest request, int userId);
    Task<IEnumerable<ProductSalesRow>> ProductSalesAsync(DateTime from, DateTime to);
}
