using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.Common;

public static class SaleRules
{
    // Apply only when creating a new invoice, after checking completed-request replay.
    // Historical CREDIT invoices and recovery continue to use the existing ledger.
    public static void RequireCollectedPayments(CreateSaleRequest request)
    {
        if (request.CustomerId is null) throw new ValidationException("Select a registered customer before completing the sale.");
        if (request.Payments.Count != 1) throw new ValidationException("Choose one payment method covering the complete bill.");
        var collection = request.Payments[0];
        if (collection.PaymentMode == "CARD" && collection.CardType is null)
            throw new ValidationException("Choose Debit Card or Credit Card.");
        if (collection.PaymentMode != "CASH" && collection.CollectionConfirmed != true)
            throw new ValidationException("Confirm receipt after checking your payment device or bank.");
        if (request.PaymentDueDate.HasValue || request.Payments.Any(p => p.PaymentMode == "CREDIT"))
            throw new ValidationException("New sales must be fully paid at checkout. Customer Credit / Pay Later is not available.");

        var allocations = new HashSet<(string Mode, string? Card, decimal Amount, string? Reference)>();
        var references = new HashSet<(string Mode, string Reference)>();
        foreach (var payment in request.Payments)
        {
            var reference = payment.ReferenceNumber?.ToUpperInvariant();
            if (!allocations.Add((payment.PaymentMode,payment.CardType,payment.Amount,reference)) ||
                (payment.PaymentMode != "CASH" && reference is not null && !references.Add((payment.PaymentMode,reference))))
                throw new ValidationException("Remove duplicate payment allocations or repeated transaction references.");
        }
    }
    public const decimal MaximumMoney = 9999999999.99m;
    public static decimal Money(decimal amount)
    {
        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public static void Validate(CreateSaleRequest request, bool completing)
    {
        if((request.SourceQuotationId.HasValue||request.SourceQuotationRevision.HasValue||request.SourceQuotationVersion.HasValue)
            && (request.SourceQuotationId is null || request.SourceQuotationId==Guid.Empty || request.SourceQuotationRevision is null or <=0 || request.SourceQuotationVersion is null or <=0))
            throw new ValidationException("Quotation checkout requires its complete source revision and version.");
        if (completing && request.RequestId == Guid.Empty)
        {
            throw new ValidationException("A request ID is required for safe retries.");
        }

        if (request.CustomerId <= 0)
        {
            throw new ValidationException("Choose a valid customer or walk-in sale.");
        }

        if (request.Items is null || request.Items.Count is < 1 or > 500 || request.Items.Any(i => i is null))
        {
            throw new ValidationException("Add between 1 and 500 sale lines.");
        }

        if (request.Payments is null || request.Payments.Count > 20 || request.Payments.Any(p => p is null))
        {
            throw new ValidationException("Use at most 20 payment entries.");
        }

        if (request.PaymentDueDate is {} due && (due.TimeOfDay != TimeSpan.Zero || request.CustomerId is null ||
            !request.Payments.Any(p => p.PaymentMode == "CREDIT" && p.Amount > 0)))
            throw new ValidationException("A payment due date requires a named customer and a credit payment.");

        if (request.Notes?.Length > 2000)
        {
            throw new ValidationException("Notes must be at most 2,000 characters.");
        }

        if (request.AdjustmentReason?.Length > 500)
        {
            throw new ValidationException("Adjustment reason must be at most 500 characters.");
        }

        if (request.TradeIn is { } trade)
        {
            if (string.IsNullOrWhiteSpace(trade.ItemType) || trade.ItemType.Length > 50 || trade.BrandModel?.Length > 200 || trade.SerialNumber?.Length > 150)
            {
                throw new ValidationException("Enter a trade-in item type and valid model/serial details.");
            }

            CheckMoney(trade.AllowanceAmount, "Trade-in allowance");
            if (trade.AllowanceAmount != Money(trade.AllowanceAmount))
            {
                throw new ValidationException("Trade-in allowance requires at most two decimal places.");
            }

            if (request.DiscountAmount < trade.AllowanceAmount)
            {
                throw new ValidationException("Invoice discount must include the trade-in allowance.");
            }
        }

        CheckMoney(request.DiscountAmount, "Invoice discount");
        request.DiscountAmount = Money(request.DiscountAmount);
        var unitIds = new HashSet<long>();
        foreach (var item in request.Items)
        {
            if(item.PriceAdjustmentReason?.Length>500 || item.TaxAdjustmentReason?.Length>500)
                throw new ValidationException("Adjustment reasons cannot exceed 500 characters.");
            if(item.AdjustmentReason?.Length>500)throw new ValidationException("Line adjustment reasons cannot exceed 500 characters.");
            if(item.TaxOverride is {} tax && (tax<0 || tax>100 || tax!=Money(tax) || item.ManualItem is not null))
                throw new ValidationException("Enter a tax percentage from 0 to 100 with at most two decimals. Manual items use their reviewed tax classification.");
            if (item.ManualItem is {} manual)
            {
                manual.Name = manual.Name.Trim();
                if (item.ProductId != 0 || item.VariantId is not null || item.ProductUnitId is not null || manual.Name.Length is < 1 or > 150 || manual.Description?.Length > 1000 || manual.TaxRuleId < 0)
                    throw new ValidationException("Manual items need a valid name and optional tax classification, without catalog or stock identifiers.");
                if (manual.Mrp is {} mrp && (mrp <= 0 || mrp > MaximumMoney || mrp != Money(mrp)))
                    throw new ValidationException("Enter a valid positive MRP or leave it empty.");
            }
            else if (item.ProductId <= 0)
            {
                throw new ValidationException($"Sale line {request.Items.IndexOf(item) + 1} has no valid product. Remove it and select the product again.");
            }

            if (item.Quantity is < 1 or > 10000)
            {
                throw new ValidationException($"Sale line {request.Items.IndexOf(item) + 1} requires a quantity between 1 and 10,000.");
            }

            if (item.ProductUnitId.HasValue && (item.ProductUnitId <= 0 || item.Quantity != 1 || !unitIds.Add(item.ProductUnitId.Value)))
            {
                throw new ValidationException("Each physical unit must appear once with quantity one.");
            }

            CheckMoney(item.SellingPrice, "Selling price");
            CheckMoney(item.DiscountAmount, "Line discount");
            item.SellingPrice = Money(item.SellingPrice);
            if (item.DiscountPercent is {} rate)
            {
                if (rate < 0 || rate > 100 || rate != Money(rate)) throw new ValidationException("Discount must be between 0 and 100 percent with up to two decimals.");
                item.DiscountAmount = Money(item.Quantity * item.SellingPrice * rate / 100);
            }
            item.DiscountAmount = Money(item.DiscountAmount);
            if (completing && item.SellingPrice == 0)
            {
                throw new ValidationException("Set a positive selling price before completing the sale. Zero prices are allowed during catalog setup only.");
            }

            if (item.DiscountAmount > item.Quantity * item.SellingPrice)
            {
                throw new ValidationException("Line discount cannot exceed its value.");
            }
        }

        foreach (var payment in request.Payments)
        {
            payment.PaymentMode = payment.PaymentMode?.Trim().ToUpperInvariant() ?? "";
            if (payment.PaymentMode is not ("CASH" or "UPI" or "CARD" or "BANK_TRANSFER" or "CREDIT"))
            {
                throw new ValidationException("Choose a supported payment mode.");
            }

            if (payment.PaymentMode == "CREDIT" && request.CustomerId is null)
            {
                throw new ValidationException("Credit requires a named customer.");
            }
            payment.CardType = string.IsNullOrWhiteSpace(payment.CardType) ? null : payment.CardType.Trim().ToUpperInvariant();
            if (payment.CardType is not null && (payment.PaymentMode != "CARD" || payment.CardType is not ("DEBIT" or "CREDIT")))
                throw new ValidationException("Choose Debit or Credit as the type of a Card collection.");

            CheckMoney(payment.Amount, "Payment");
            if (completing && payment.Amount != Money(payment.Amount))
                throw new ValidationException("Payment amounts require at most two decimal places.");
            payment.Amount = Money(payment.Amount);
            if (payment.Amount <= 0)
            {
                throw new ValidationException("Payment amounts must be positive.");
            }

            payment.ReferenceNumber = string.IsNullOrWhiteSpace(payment.ReferenceNumber) ? null : payment.ReferenceNumber.Trim();
            if (payment.ReferenceNumber?.Length > 100)
            {
                throw new ValidationException("Payment reference must be at most 100 characters.");
            }
        }

        if (request.CashTendered is { } tendered)
        {
            CheckMoney(tendered, "Cash received");
            if (tendered != Money(tendered))
            {
                throw new ValidationException("Cash received requires at most two decimal places.");
            }

            var cash = request.Payments.Where(p => p.PaymentMode == "CASH").Sum(p => p.Amount);
            if (completing && (cash <= 0 || tendered < cash))
            {
                throw new ValidationException("Cash received must cover the cash payment.");
            }
        }
    }

    public static SaleQuote Calculate(CreateSaleRequest request, IReadOnlyDictionary<int, decimal> taxes, bool completing)
    {
        Validate(request, completing);
        return CalculateLines(request, request.Items.Select(i => taxes.TryGetValue(i.ProductId, out var tax) ? tax : -1m).ToArray(), completing);
    }

    public static SaleQuote CalculateLines(CreateSaleRequest request, IReadOnlyList<decimal> taxes, bool completing)
    {
        Validate(request, completing);
        if (taxes.Count != request.Items.Count)
        {
            throw new ValidationException("A tax rate is required for every sale line.");
        }

        var quote = new SaleQuote
        {
            DiscountAmount = request.DiscountAmount
        };
        var allocate = request.AllocateInvoiceDiscount == true;
        var invoiceDiscount = allocate ? request.DiscountAmount - (request.TradeIn?.AllowanceAmount ?? 0) : 0;
        var remainingBase = request.Items.Sum(i => i.Quantity * i.SellingPrice - i.DiscountAmount);
        if (invoiceDiscount < 0 || invoiceDiscount > remainingBase)
            throw new ValidationException("Invoice discount cannot exceed the eligible item amount.");
        var remainingDiscount = invoiceDiscount;
        quote.InvoiceDiscountAmount = request.DiscountAmount - (request.TradeIn?.AllowanceAmount ?? 0);
        if (allocate) quote.DiscountAmount -= invoiceDiscount;
        foreach (var item in request.Items)
        {
            var tax = taxes[quote.Items.Count];
            if (tax is < 0 or > 100)
            {
                throw new ValidationException("Product tax configuration is invalid.");
            }

            var inclusive = request.PricesIncludeTax == true;
            var gross = item.Quantity * item.SellingPrice - item.DiscountAmount;
            var allocation = remainingBase == 0 ? 0 : Math.Min(gross, Money(remainingDiscount * gross / remainingBase));
            remainingBase -= gross;
            remainingDiscount -= allocation;
            quote.ItemSubtotal += item.Quantity * item.SellingPrice;
            quote.ItemDiscountAmount += item.DiscountAmount;
            gross -= allocation;
            var (subtotal, lineTax) = inclusive ? GstRules.InclusiveAmounts(gross, tax) : (gross, Money(gross * tax / 100));
            var netUnitPrice = inclusive ? decimal.Ceiling(item.SellingPrice / (1 + tax / 100) * 100) / 100 : item.SellingPrice;
            CheckMoney(subtotal + lineTax, "Line total");
            quote.Subtotal += subtotal;
            quote.TaxAmount += lineTax;
            if (tax > 0) quote.TaxableAmount += subtotal;
            quote.Items.Add(new()
            {
                NetUnitPrice = netUnitPrice,
                DiscountAmount = item.DiscountAmount,
                NetDiscountAmount = inclusive ? Money(item.Quantity * netUnitPrice - subtotal) : item.DiscountAmount + allocation,
                TaxPercentage = tax,
                TaxAmount = lineTax,
                LineTotal = subtotal + lineTax
            });
        }

        quote.TotalAmount = quote.Subtotal + quote.TaxAmount - quote.DiscountAmount;
        CheckMoney(quote.Subtotal, "Subtotal");
        CheckMoney(quote.TaxAmount, "Tax");
        CheckMoney(quote.TotalAmount, "Sale total");
        if (completing && request.Payments.Sum(p => p.Amount) != quote.TotalAmount)
        {
            throw new ValidationException("Payment allocations must equal the sale total.");
        }

        return quote;
    }

    private static void CheckMoney(decimal amount, string name)
    {
        if (amount < 0 || amount > MaximumMoney)
        {
            throw new ValidationException(name + " is outside the allowed range.");
        }
    }
}
