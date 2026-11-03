using StockFlow.Application.DTOs.Security;

namespace StockFlow.Application.Interfaces;

public interface IBranchRepository
{
    Task<BranchUserState?> UserStateAsync();
    Task<IEnumerable<BranchLocation>> LocationsAsync();
    Task<object> StoresAsync();
    Task<long> CreateStoreAsync(StoreRequest request);
    Task<long> CreateLocationAsync(LocationRequest request);
    Task<StoreAccessRequest> AccessAsync(int userId);
    Task SetAccessAsync(int userId, StoreAccessRequest request);
    Task<IEnumerable<StockComparison>> ComparisonAsync(string? term);
    Task<object> SummaryAsync(bool includeSales);
}
