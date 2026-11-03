namespace StockFlow.Domain.Entities;

public class ServicePart
{
    public long ServicePartId
    {
        get; set;
    }
    public long ServiceRequestId
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
    public string ProductName { get; set; } = "";
    public long? ProductUnitId
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
    public decimal UnitPrice
    {
        get; set;
    }
    public decimal TotalAmount
    {
        get; set;
    }
    public int CreatedBy
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
