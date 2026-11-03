using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.Common;

// One policy decision shared by quotes and transactional finalization.
public sealed class BillingDiscountPolicy
{
    public decimal DiscountLimitPercent { get; set; } = 10;
    public long StoreId { get; set; }
    public long Version { get; set; }
    public bool FastBillingEnabled { get; set; }
    public bool AllowRoutineDiscounts { get; set; } = true;
    public bool RequireRoutineReason { get; set; } = true;
    public bool RequireExceptionalReason { get; set; } = true;
    public bool RequireApprovalAboveLimit { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Stamp => Version == 0 ? null : System.Text.Json.JsonSerializer.Serialize(this);

    public SaleAdjustmentReview Evaluate(CreateSaleRequest request, SaleQuote quote, IReadOnlyList<decimal> catalogPrices)
    {
        var catalogTotal = request.Items.Select((item, n) => item.Quantity * catalogPrices[n] *
            (request.PricesIncludeTax == true ? 1 : 1 + quote.Items[n].TaxPercentage / 100)).Sum();
        var reduction = Math.Max(0, catalogTotal - quote.TotalAmount - (request.TradeIn?.AllowanceAmount ?? 0));
        var exceptional = reduction > catalogTotal * DiscountLimitPercent / 100;
        var reason = !FastBillingEnabled || (exceptional ? RequireExceptionalReason : RequireRoutineReason);
        var review = new SaleAdjustmentReview(DiscountLimitPercent, exceptional && RequireApprovalAboveLimit, "NOT_REQUIRED")
        {
            PolicyVersion = Version, Exceptional = exceptional,
            InvoiceReasonRequired = request.DiscountAmount > (request.TradeIn?.AllowanceAmount ?? 0) && reason
        };
        foreach (var (item, n) in request.Items.Select((item, n) => (item, n)))
        {
            var priceChanged = item.ManualItem is null && item.SellingPrice != catalogPrices[n];
            review.LineReasons.Add(new(item.DiscountAmount > 0 && reason, priceChanged, item.TaxOverride.HasValue));
        }
        return review;
    }

    public void ValidateAdjustments(CreateSaleRequest request, SaleAdjustmentReview review, bool completing)
    {
        if (!AllowRoutineDiscounts && !review.Exceptional && (request.DiscountAmount > (request.TradeIn?.AllowanceAmount ?? 0) || request.Items.Any(i => i.DiscountAmount > 0)))
            throw new ValidationException("Routine discounts are disabled by this shop's billing policy.");
        if (!completing) return;
        if (review.InvoiceReasonRequired && string.IsNullOrWhiteSpace(request.AdjustmentReason))
            throw new ValidationException("Enter a reason for the invoice discount.");
        foreach (var (requirement, n) in review.LineReasons.Select((requirement, n) => (requirement, n)))
        {
            var item = request.Items[n];
            // Legacy drafts retain the old bill-level discount/price reason; tax is separate.
            var legacy = request.Items.All(i => i.AdjustmentReason is null) ? request.AdjustmentReason : null;
            if (requirement.Discount && string.IsNullOrWhiteSpace(item.AdjustmentReason ?? legacy))
                throw new ValidationException($"Enter a discount exception reason for line {n + 1}.");
            if (requirement.Price && string.IsNullOrWhiteSpace(item.PriceAdjustmentReason ?? item.AdjustmentReason ?? legacy))
                throw new ValidationException($"Enter a price change reason for line {n + 1}.");
        }
    }
}
