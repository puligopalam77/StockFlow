namespace StockFlow.Domain.Entities;

public class Purchase
{
    public long PurchaseId
    {
        get; set;
    }
    public int SupplierId
    {
        get; set;
    }
    public string SupplierName { get; set; } = string.Empty;
    public string PurchaseNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber
    {
        get; set;
    }
    public DateTime PurchaseDate
    {
        get; set;
    }
    public DateTime? ReceivedDate
    {
        get; set;
    }
    public decimal Subtotal
    {
        get; set;
    }
    public decimal TaxAmount
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public decimal TotalAmount
    {
        get; set;
    }
    public string Status { get; set; } = string.Empty;
    public string? Notes
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
