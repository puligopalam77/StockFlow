using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Purchases;

namespace StockFlow.Application.Common;

public static class StockIntakeRules
{
    public static CreatePurchaseRequest Validate(StockIntakeRequest request, bool opening)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        if (request.RequestId == Guid.Empty || request.ReceivedDate == default)
        {
            throw new ValidationException("A receipt date and retry identifier are required.");
        }

        if (request.Items is null || request.Items.Count is < 1 or > 200 || request.Items.Any(i => i is null || i.Quantity is < 1 or > 10000 || i.VariantId <= 0 || i.SerialNumbers is null))
        {
            throw new ValidationException("Add 1 to 200 valid items with quantities from 1 to 10,000.");
        }

        if (opening && (request.SupplierId.HasValue || !string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber)))
        {
            throw new ValidationException("Opening stock must not have a supplier or purchase invoice.");
        }

        if (!opening && !(request.SupplierId > 0))
        {
            throw new ValidationException("Choose a supplier for this receipt.");
        }

        var purchase = new CreatePurchaseRequest
        {
            LocationId = request.LocationId,
            SupplierId = opening ? 1 : request.SupplierId!.Value,
            PurchaseDate = request.ReceivedDate,
            ReceivedDate = request.ReceivedDate,
            SupplierInvoiceNumber = request.SupplierInvoiceNumber,
            Notes = request.Notes,
            CompleteImmediately = true,
            Items = request.Items
        };
        PurchaseRules.ValidateAndCalculate(purchase);
        if (request.Items.Any(i => i.DiscountAmount != 0))
        {
            throw new ValidationException("Use the advanced purchase form for discounts.");
        }

        request.ReceivedDate = purchase.PurchaseDate;
        request.Notes = purchase.Notes;
        request.SupplierInvoiceNumber = purchase.SupplierInvoiceNumber;
        return purchase;
    }
}
