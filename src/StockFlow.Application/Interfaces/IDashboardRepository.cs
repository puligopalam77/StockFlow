using StockFlow.Application.DTOs.Dashboard;

namespace StockFlow.Application.Interfaces;

public interface IDashboardRepository
{
    Task<DashboardResponse> GetAsync(bool isAdmin, bool canSeeSales = true);
    Task<DashboardInventorySummaryResponse> GetInventorySummaryAsync(bool isAdmin);
    Task<BusinessDashboardSummaryResponse> GetBusinessSummaryAsync(bool canSeeSales);
    Task<IEnumerable<MonthlyStock>> GetTrendAsync(string period);
    Task<IEnumerable<CategoryStock>> GetCategoryStockAsync();
    Task<IEnumerable<RecentStock>> GetMovementsAsync(DashboardMovementSearch search, bool isAdmin, int limit = 100);
    Task<IEnumerable<RecentSaleResponse>> GetRecentSalesAsync();
    Task<IEnumerable<RecentServiceResponse>> GetRecentServicesAsync();
    Task<IEnumerable<LowStockResponse>> GetLowStockAsync(int offset, int limit = 100);
    Task<IEnumerable<GlobalSearchResult>> SearchAsync(string term);
    Task<IEnumerable<DashboardUser>> GetUsersAsync();
}
