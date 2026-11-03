namespace StockFlow.Application.DTOs.Purchases;

public class StockLabelResponse
{
    public long? LabelId { get; set; }
    public long? IntakeId { get; set; }
    public int? ItemNumber { get; set; }
    public long VariantId
    {
        get; set;
    }
    public long? UnitId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? VariantDetails
    {
        get; set;
    }
    public string? SerialNumber
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public string? ExistingBarcode
    {
        get; set;
    }
    public string? InternalBarcode
    {
        get; set;
    }
    public decimal SellingPrice
    {
        get; set;
    }
}
