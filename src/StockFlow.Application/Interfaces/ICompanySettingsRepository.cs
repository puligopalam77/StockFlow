using StockFlow.Application.DTOs.Invoices;

namespace StockFlow.Application.Interfaces;

public interface ICompanySettingsRepository
{
    Task<CompanyGstIdentity?> GetGstIdentityAsync() => Task.FromResult<CompanyGstIdentity?>(null);
    Task SaveTaxModeAsync(string mode,int userId) => throw new NotSupportedException();
    Task<CompanySettings> GetAsync();
    Task<IReadOnlyList<CompanySalesperson>> GetSalespeopleAsync() => Task.FromResult<IReadOnlyList<CompanySalesperson>>([]);
    Task SaveAsync(CompanySettings settings, int userId);
    Task SaveSectionAsync(CompanySettings settings,string section,string expectedVersion,int userId)=>throw new NotSupportedException();
}
