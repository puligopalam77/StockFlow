namespace StockFlow.Application.DTOs.Gst;

public sealed record GstRateBreakdown(decimal Rate, decimal SupplyValue, decimal TaxAmount,
    decimal Cgst, decimal Sgst, decimal Utgst, decimal Igst, decimal Cess);

public sealed class SaleTaxBreakdown
{
    public string Status { get; init; } = "UNAVAILABLE";
    public string? SupplierStateCode { get; init; }
    public string? PlaceOfSupply { get; init; }
    public List<GstRateBreakdown> Rates { get; init; } = [];
}
