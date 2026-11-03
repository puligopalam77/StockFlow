using StockFlow.Application.DTOs.Inventory;

namespace StockFlow.Application.Interfaces;

public interface IStockReceiptHistoryRepository
{
    Task<StockReceiptHistoryResult> SearchAsync(StockReceiptSearch search);
    Task<StockReceiptItemHistoryResult> SearchItemsAsync(StockReceiptSearch search);
    Task<IReadOnlyList<StockReceiptSupplier>> GetSuppliersAsync();
    Task<StockReceiptDetail?> GetDetailAsync(string source, long id);
}
