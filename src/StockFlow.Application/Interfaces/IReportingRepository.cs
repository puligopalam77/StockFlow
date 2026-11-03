using StockFlow.Application.DTOs.Reports;

namespace StockFlow.Application.Interfaces;

public interface IReportingRepository
{
    Task<ActivityReport> ActivityAsync(DateTime from, DateTime to, bool stock, bool sales);
    async Task<List<FinancialPeriodRow>> FinancialAsync(DateTime from, DateTime to) => (await ActivityAsync(from, to, false, true)).Financial;
}
