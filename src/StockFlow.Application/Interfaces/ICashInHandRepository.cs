using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Application.Interfaces;

public interface ICashInHandRepository
{
    Task<CashInHandReport> GetAsync(DateTime date, string? search = null, int page = 1, string? type = null, string? direction = null);
    Task<Guid> AdjustAsync(CashAdjustmentRequest request);
    Task ReviewOpeningExpenseAsync(CashOpeningReviewRequest request);
}
