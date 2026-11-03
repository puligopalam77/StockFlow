namespace StockFlow.Domain.Entities;

public class Sale
{
    public long SaleId
    {
        get; set;
    }
    public int? CustomerId
    {
        get; set;
    }
    public string InvoiceNumber { get; set; } = "";
    public DateTime SaleDate
    {
        get; set;
    }
    public decimal Subtotal
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public decimal TaxAmount
    {
        get; set;
    }
    public decimal TotalAmount
    {
        get; set;
    }
    public string Status { get; set; } = "";
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
