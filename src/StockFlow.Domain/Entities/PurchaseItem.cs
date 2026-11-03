namespace StockFlow.Domain.Entities;

public class PurchaseItem
{
    public long PurchaseItemId
    {
        get; set;
    }
    public long PurchaseId
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
    public string ProductName { get; set; } = string.Empty;
    public int Quantity
    {
        get; set;
    }
    public decimal PurchasePrice
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
    public bool IsSerialTracked
    {
        get; set;
    }
}
