namespace StockFlow.Domain.Entities;

public class ProductUnit
{
    public long ProductUnitId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long VariantId
    {
        get; set;
    }
    public long PurchaseId
    {
        get; set;
    }
    public string? SerialNumber
    {
        get; set;
    }
    public string? Barcode
    {
        get; set;
    }
    public string UnitStatus { get; set; } = "IN_STOCK";
    public DateTime CreatedAt
    {
        get; set;
    }
}
