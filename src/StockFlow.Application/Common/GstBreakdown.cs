using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.Common;

public static class GstBreakdown
{
    // Match sf_gst_capture_sale: round each line's central component, then use
    // the remainder for the state/UT component. Never split a rounded group total.
    public static SaleTaxBreakdown ForQuote(SaleQuote quote, bool mapped, string? registrationType,
        string? supplierState, string? placeOfSupply)
    {
        var status = quote.TaxAmount == 0 ? "NO_GST" : "UNAVAILABLE";
        if (quote.TaxAmount > 0 && mapped && registrationType == "REGULAR" && ValidState(supplierState))
            status = !ValidState(placeOfSupply) ? "PLACE_OF_SUPPLY_REQUIRED"
                : supplierState != placeOfSupply ? "IGST"
                : supplierState is "04" or "26" or "31" or "35" or "38" ? "CGST_UTGST" : "CGST_SGST";
        var lines = quote.Items.Select(line =>
        {
            var central = status is "CGST_SGST" or "CGST_UTGST" ? SaleRules.Money(line.TaxAmount / 2) : 0;
            return new GstRateBreakdown(line.TaxPercentage, line.LineTotal - line.TaxAmount, line.TaxAmount,
                central, status == "CGST_SGST" ? line.TaxAmount - central : 0,
                status == "CGST_UTGST" ? line.TaxAmount - central : 0,
                status == "IGST" ? line.TaxAmount : 0, 0);
        });
        return new() { Status = status, SupplierStateCode = supplierState, PlaceOfSupply = placeOfSupply, Rates = Group(lines) };
    }

    // Historical invoices use recorded components, never today's registration or rates.
    public static List<GstRateBreakdown> ForInvoice(IEnumerable<GstLine> lines) => Group(lines.Select(line =>
        new GstRateBreakdown(line.TaxRate, line.TaxableValue, line.Cgst + line.Sgst + line.Utgst + line.Igst + line.Cess,
            line.Cgst, line.Sgst, line.Utgst, line.Igst, line.Cess)));

    private static bool ValidState(string? state) => state is not null && state is not "25" and not "28" && GstRules.States.ContainsKey(state);

    private static List<GstRateBreakdown> Group(IEnumerable<GstRateBreakdown> lines) => lines.GroupBy(line => line.Rate)
        .OrderBy(group => group.Key).Select(group => new GstRateBreakdown(group.Key,
            group.Sum(line => line.SupplyValue), group.Sum(line => line.TaxAmount), group.Sum(line => line.Cgst),
            group.Sum(line => line.Sgst), group.Sum(line => line.Utgst), group.Sum(line => line.Igst), group.Sum(line => line.Cess))).ToList();
}
