using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
sealed class FakeBranchRepository:IBranchRepository
{
 public BranchUserState State {get;set;}=new(){IsActive=true,IsOwner=true};
 public Exception? Failure {get;set;}
 public List<BranchLocation> Locations {get;set;}=[new(){LocationId=1,StoreId=1,StoreName="Main store",LocationName="Main stock",LocationType="SALES_FLOOR",IsActive=true,IsDefault=true}];
 public Task<BranchUserState?> UserStateAsync()=>Failure is null ? Task.FromResult<BranchUserState?>(State) : Task.FromException<BranchUserState?>(Failure);
 public Task<IEnumerable<BranchLocation>> LocationsAsync()=>Task.FromResult<IEnumerable<BranchLocation>>(Locations);
 public Task<object> StoresAsync()=>Task.FromResult<object>(new[]{new{storeId=1,storeName="Main store"}});
 public Task<long> CreateStoreAsync(StoreRequest r)=>throw new NotSupportedException();
 public Task<long> CreateLocationAsync(LocationRequest r)=>throw new NotSupportedException();
 public Task<StoreAccessRequest> AccessAsync(int id)=>throw new NotSupportedException();
 public Task SetAccessAsync(int id,StoreAccessRequest r)=>throw new NotSupportedException();
 public Task<IEnumerable<StockComparison>> ComparisonAsync(string? term)=>Task.FromResult<IEnumerable<StockComparison>>([]);
 public Task<object> SummaryAsync(bool includeSales)=>Task.FromResult<object>(Array.Empty<object>());
}
