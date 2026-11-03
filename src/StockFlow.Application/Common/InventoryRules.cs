using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Inventory;

namespace StockFlow.Application.Common;

public static class InventoryRules
{
    public static void Validate(StockAdjustmentRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new ValidationException("A request ID is required for safe retries.");
        }

        if (request.ProductId <= 0 || request.Quantity is < 1 or > 10000)
        {
            throw new ValidationException("Choose a product and a quantity between 1 and 10,000.");
        }

        if (request.TransactionType is not ("ADJUSTMENT_IN" or "ADJUSTMENT_OUT" or "DAMAGED"))
        {
            throw new ValidationException("Choose adjustment in, adjustment out, or damaged.");
        }

        request.Reason = request.Reason?.Trim() ?? "";
        if (request.Reason.Length is < 1 or > 1000)
        {
            throw new ValidationException("A reason of up to 1,000 characters is required.");
        }

        if (request.SerialNumbers is null || request.ProductUnitIds is null)
        {
            throw new ValidationException("Serial numbers and unit IDs must be lists.");
        }

        request.SerialNumbers = request.SerialNumbers.Select(s => s?.Trim() ?? "").ToList();
        if (request.SerialNumbers.Count > 10000 || request.SerialNumbers.Any(s => s.Length is < 1 or > 150) || request.SerialNumbers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.SerialNumbers.Count)
        {
            throw new ValidationException("Serial numbers must be unique and between 1 and 150 characters.");
        }

        if (request.ProductUnitIds.Count > 10000 || request.ProductUnitIds.Any(id => id <= 0) || request.ProductUnitIds.Distinct().Count() != request.ProductUnitIds.Count)
        {
            throw new ValidationException("Select unique, valid physical units.");
        }
    }
}
