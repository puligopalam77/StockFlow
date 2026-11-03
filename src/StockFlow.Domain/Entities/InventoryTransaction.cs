namespace StockFlow.Domain.Entities;

public class InventoryTransaction
{
    public long InventoryTransactionId
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
    public long? ProductUnitId
    {
        get; set;
    }
    public string TransactionType { get; set; } = string.Empty;
    public int Quantity
    {
        get; set;
    }
    public string? ReferenceType
    {
        get; set;
    }
    public long? ReferenceId
    {
        get; set;
    }
    public string? Remarks
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
