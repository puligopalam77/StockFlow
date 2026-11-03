using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.Application.Interfaces;

public interface IStockIntakeRepository
{
    Task GenerateItemLabelsAsync(long intakeId, int userId) => throw new System.ComponentModel.DataAnnotations.ValidationException("Unique item labels are unavailable.");
    Task<StockIntakeResult> PostAsync(StockIntakeRequest request, bool opening, int userId);
    Task<IReadOnlyList<StockLabelResponse>?> LabelsAsync(long intakeId, string? printAction = null, int userId = 0);
}
