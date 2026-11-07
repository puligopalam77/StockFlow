using StockFlow.Application.DTOs.Inventory;

namespace StockFlow.Application.Interfaces;

public interface IInventoryRepository
{
    Task<IEnumerable<InventoryBalance>> GetBalancesAsync(string? term, bool lowStockOnly, bool includeInactive);
    Task<LowStockPage> GetLowStockPageAsync(string? term,string? stockStatus,int page,int pageSize);
    Task<IEnumerable<InventoryMovement>> GetHistoryAsync(int? productId, DateTime? from, DateTime? to, int offset, long? variantId = null);
    Task<IEnumerable<InventoryUnit>> GetUnitsAsync(int? productId, string? term, bool availableOnly, int offset, long? variantId = null);
    Task<long> AdjustAsync(StockAdjustmentRequest request, int userId);
}
