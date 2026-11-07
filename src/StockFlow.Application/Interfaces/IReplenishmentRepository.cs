using StockFlow.Application.DTOs.Inventory;
namespace StockFlow.Application.Interfaces;
public interface IReplenishmentRepository
{
 Task<ReplenishmentPage> PlanAsync(string? term,string? status,int? supplierId,int days,int page,int pageSize);
 Task<IReadOnlyList<ReplenishmentSupplier>> SuppliersAsync();
 Task<long> SavePolicyAsync(ReplenishmentPolicy policy);
 Task<long> PrepareAsync(ReplenishmentDraftRequest request);
}
