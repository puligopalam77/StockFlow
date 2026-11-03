using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.Common;

public static class BillingApproval
{
    public static string Hash(CreateSaleRequest request, string? policyStamp = null, SaleQuote? quote = null)
    {
        var financial = JsonSerializer.Serialize(new
        {
            request.CustomerId,
            request.Items,
            request.DiscountAmount,
            request.TradeIn,
            request.AdjustmentReason
        });
        // Preserve legacy hashes, but never reuse an after-tax approval for
        // a request that allocates the discount before calculating tax.
        if (request.AllocateInvoiceDiscount == true) financial += "|allocated-v1";
        if (request.SourceQuotationId.HasValue) financial += $"|quotation:{request.SourceQuotationId}:{request.SourceQuotationRevision}:{request.SourceQuotationVersion}";
        if (policyStamp is not null) financial += "|policy:" + policyStamp;
        if (quote is not null) financial += "|quote:" + JsonSerializer.Serialize(new
        {
            request.PricesIncludeTax, request.GstPlaceOfSupplyStateCode,
            quote.ItemSubtotal, quote.ItemDiscountAmount, quote.InvoiceDiscountAmount,
            quote.Subtotal, quote.TaxAmount, quote.TotalAmount,
            Lines = quote.Items.Select(i => new { i.CatalogPrice, i.TaxPercentage, i.TaxAmount, i.LineTotal })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(financial)));
    }
}
