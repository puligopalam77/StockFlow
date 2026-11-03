using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Application.Interfaces;

public interface ICashClosingRepository
{
    Task<CashDayReport> GetAsync(DateTime date);
    Task<CashClosingRecord> SaveAsync(CashClosingRequest request);
}
