using StockFlow.Application.DTOs.Security;

namespace StockFlow.Application.Interfaces;

public interface IBusinessProfileRepository
{
    Task<BusinessProfileImpact> ImpactAsync();
    Task<long> ChangeAsync(BusinessProfileChange request);
}
