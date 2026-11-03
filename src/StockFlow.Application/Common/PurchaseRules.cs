using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.Application.Common;

public static class PurchaseRules
{
    public const decimal MaxAmount = 9999999999.99m;
    public static decimal Money(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static (decimal Subtotal, decimal Tax, decimal Total) ValidateAndCalculate(CreatePurchaseRequest request)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (request.PurchaseDate == default)
        {
            throw new ValidationException("Purchase date is required.");
        }

        if (request.ReceivedDate?.Date < request.PurchaseDate.Date)
        {
            throw new ValidationException("Received date cannot precede purchase date.");
        }

        if (request.SupplierInvoiceDate?.Date > DateTime.Today.AddDays(1))
        {
            throw new ValidationException("Supplier invoice date cannot be in the future.");
        }

        request.PurchaseDate = DateTime.SpecifyKind(request.PurchaseDate.Date, DateTimeKind.Unspecified);
        request.SupplierInvoiceDate = request.SupplierInvoiceDate is { } invoice ? DateTime.SpecifyKind(invoice.Date, DateTimeKind.Unspecified) : null;
        request.ReceivedDate = request.ReceivedDate is { } receivedDate ? DateTime.SpecifyKind(receivedDate.Date, DateTimeKind.Unspecified) : null;
        request.SupplierInvoiceNumber = Clean(request.SupplierInvoiceNumber);
        request.Notes = Clean(request.Notes);
        request.ChangeReason = Clean(request.ChangeReason);
        request.InternalReference = Clean(request.InternalReference);
        if (request.ChangeReason is { Length: < 3 }) throw new ValidationException("Enter a reason of at least three characters.");
        request.DiscountAmount = Money(request.DiscountAmount);
        request.FreightAmount = Money(request.FreightAmount);
        request.OtherLandedCost = Money(request.OtherLandedCost);
        if (request.InvoiceAttachmentData is null)
        {
            request.InvoiceAttachmentName = request.InvoiceAttachmentContentType = null;
        }
        else
        {
            request.InvoiceAttachmentName = Path.GetFileName(Clean(request.InvoiceAttachmentName) ?? throw new ValidationException("Invoice attachment name is required."));
            request.InvoiceAttachmentContentType = Clean(request.InvoiceAttachmentContentType)?.ToLowerInvariant();
            if (request.InvoiceAttachmentData.Length is < 1 or > 5000000 || request.InvoiceAttachmentContentType is not ("application/pdf" or "image/jpeg" or "image/png"))
            {
                throw new ValidationException("Attach one PDF, JPG or PNG supplier invoice up to 5 MB.");
            }
        }

        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal subtotal = 0, tax = 0;
        foreach (var item in request.Items)
        {
            if (item is null)
            {
                throw new ValidationException("Purchase items cannot be null.");
            }

            Validator.ValidateObject(item, new ValidationContext(item), true);
            item.PurchasePrice = Money(item.PurchasePrice);
            item.TaxPercentage = Money(item.TaxPercentage);
            item.SerialNumbers = (item.SerialNumbers ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            item.DamagedSerialNumbers = (item.DamagedSerialNumbers ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            item.SupplierProductCode = Clean(item.SupplierProductCode);
            item.BatchNumber = Clean(item.BatchNumber);
            item.ManufacturingDate = item.ManufacturingDate is { } made ? DateTime.SpecifyKind(made.Date, DateTimeKind.Unspecified) : null;
            item.ExpiryDate = item.ExpiryDate is { } expiry ? DateTime.SpecifyKind(expiry.Date, DateTimeKind.Unspecified) : null;
            if (item.ExpiryDate < item.ManufacturingDate)
            {
                throw new ValidationException("Expiry date cannot precede manufacturing date.");
            }

            var received = item.ReceivedQuantity ?? (request.CompleteImmediately ? item.Quantity : 0);
            if (received + item.DamagedQuantity > item.Quantity)
            {
                throw new ValidationException("Received plus damaged quantity cannot exceed expected quantity.");
            }

            foreach (var serial in item.SerialNumbers.Concat(item.DamagedSerialNumbers))
            {
                if (serial.Length > 150)
                {
                    throw new ValidationException("Serial numbers must be at most 150 characters.");
                }

                if (!serials.Add(serial))
                {
                    throw new ValidationException($"Duplicate serial number: {serial}.");
                }
            }

            if (item.SerialNumbers.Count + item.DamagedSerialNumbers.Count > item.Quantity)
            {
                throw new ValidationException("Serial count cannot exceed expected quantity.");
            }

            item.DiscountAmount = Money(item.DiscountAmount);
            if (item.DiscountAmount < 0 || item.DiscountAmount > item.Quantity * item.PurchasePrice)
            {
                throw new ValidationException("Invalid purchase line discount.");
            }

            var lineSubtotal = Money(item.Quantity * item.PurchasePrice) - item.DiscountAmount;
            var lineTax = Money(lineSubtotal * item.TaxPercentage / 100m);
            if (lineSubtotal + lineTax > MaxAmount)
            {
                throw new ValidationException("An item total exceeds the supported amount.");
            }

            subtotal += lineSubtotal;
            tax += lineTax;
        }

        var total = subtotal + tax + request.FreightAmount + request.OtherLandedCost - request.DiscountAmount;
        if (total < 0)
        {
            throw new ValidationException("Discount cannot exceed subtotal plus tax.");
        }

        if (subtotal > MaxAmount || tax > MaxAmount || request.FreightAmount + request.OtherLandedCost > MaxAmount || total > MaxAmount)
        {
            throw new ValidationException("Purchase totals exceed the supported amount.");
        }

        return (subtotal, tax, total);
    }

    public static void ValidateSerials(CreatePurchaseItemRequest item, bool tracked, bool completing)
    {
        var received = item.ReceivedQuantity ?? (completing ? item.Quantity : 0);
        if (!tracked && (item.SerialNumbers.Count > 0 || item.DamagedSerialNumbers.Count > 0))
        {
            throw new ValidationException("Serial numbers are only allowed for serial-tracked products.");
        }

        if (tracked && completing && (item.SerialNumbers.Count != received || item.DamagedSerialNumbers.Count != item.DamagedQuantity))
        {
            throw new ValidationException($"Product {item.ProductId} requires one serial for every received or damaged unit.");
        }
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        else
        {
            return value.Trim();
        }
    }
}
