namespace StockFlow.Application.DTOs.Sales;

public class SalesListRow : SaleResponse
{
    public decimal OutstandingAmount { get; set; }
    public DateTime? PaymentDueDate { get; set; }
    public string? CustomerMobile { get; set; }
    public long ItemCount { get; set; }
    public string PaymentMode { get; set; } = "";
    public decimal ReturnedAmount { get; set; }
    public decimal CollectedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public long LineCount { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string InvoiceStatus { get; set; } = "";
}
public class SalesListSummary
{
    public long Count { get; set; }
    public decimal TotalSales { get; set; }
    public decimal AverageValue { get; set; }
    public decimal ReturnedAmount { get; set; }
    public long InvoiceCount { get; set; }
    public decimal NetSales { get; set; }
    public decimal AmountCollected { get; set; }
    public decimal OutstandingDues { get; set; }
}
public class SalesListPage
{
    public List<SalesListRow> Rows { get; set; } = [];
    public SalesListSummary Summary { get; set; } = new();
    public SalesListSummary? Previous { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Kolkata";
}
