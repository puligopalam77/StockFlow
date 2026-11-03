namespace StockFlow.Domain.Entities;

public class Payment
{
    public long PaymentId
    {
        get; set;
    }
    public long SaleId
    {
        get; set;
    }
    public string PaymentMode { get; set; } = "";
    public decimal Amount
    {
        get; set;
    }
    public string? ReferenceNumber
    {
        get; set;
    }
    public DateTime PaymentDate
    {
        get; set;
    }
    public int CreatedBy
    {
        get; set;
    }
}
