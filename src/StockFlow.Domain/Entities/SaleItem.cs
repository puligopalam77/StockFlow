namespace StockFlow.Domain.Entities;

public class SaleItem
{
    public bool IsManual { get; set; }
    public string? DescriptionSnapshot { get; set; }
    public decimal? MrpSnapshot { get; set; }
    public string DiscountType { get; set; } = "FIXED";
    public decimal? DiscountPercent { get; set; }
    public string? ManualTaxSnapshot { get; set; }
    public long SaleItemId
    {
        get; set;
    }
    public long SaleId
    {
        get; set;
    }
    public int? ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public long? ProductUnitId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public decimal SellingPrice
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public decimal TaxPercentage
    {
        get; set;
    }
    public decimal LineTotal
    {
        get; set;
    }
}
