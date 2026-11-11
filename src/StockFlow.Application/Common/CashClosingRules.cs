using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Application.Common;

public static class CashClosingRules
{
    public static void Validate(CashClosingRequest request, DateTime today, bool allowNegativeOpening = false)
    {
        if (request.RequestId == Guid.Empty || request.Date == default || request.Date.Date > today.Date || request.ExpectedVersion < 0)
            throw new ValidationException("Choose today or an earlier date and refresh the cash totals before saving.");
        if (!Regex.IsMatch(request.Fingerprint ?? "", "^[A-F0-9]{64}$"))
            throw new ValidationException("Refresh the cash totals before saving.");
        var amounts = new[] { request.OpeningCash, request.CashAdded, request.CashRemoved, request.CountedCash };
        for (var index = 0; index < amounts.Length; index++)
        {
            var amount = amounts[index];
            if (amount < (index == 0 && allowNegativeOpening ? -9999999999.99m : 0) || amount > 9999999999.99m || decimal.Round(amount, 2) != amount)
                throw new ValidationException("Cash amounts must be between 0 and 9,999,999,999.99, with at most two decimal places.");
        }
        request.Date = request.Date.Date;
        request.Reason = request.Reason?.Trim() ?? "";
        if (request.Reason.Length > 1000)
            throw new ValidationException("The reason must contain no more than 1,000 characters.");
    }

    public static decimal Expected(CashClosingRequest request, CashTotals totals)
    {
        return request.OpeningCash + totals.NetCash + request.CashAdded - request.CashRemoved;
    }

    public static void CheckReason(CashClosingRequest request, decimal expected)
    {
        if ((request.CountedCash != expected || request.CashAdded != 0 || request.CashRemoved != 0 || request.ExpectedVersion > 0) && request.Reason.Length == 0)
            throw new ValidationException("Enter a reason for a cash difference, cash added/removed, or an updated closing.");
    }
}
