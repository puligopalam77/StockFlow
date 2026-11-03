using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.DTOs.Invoices;

public class InvoiceResponse
{
    public StockFlow.Application.DTOs.Operations.DocumentOperations? Settlement { get; set; }
    public bool PricesIncludeTax { get; set; }
    public decimal? ItemSubtotal { get; set; }
    public decimal TaxableAmount => Items.Where(i => i.TaxPercentage > 0).Sum(i => i.Quantity * i.SellingPrice - i.DiscountAmount);
    public decimal? ItemDiscountAmount { get; set; }
    public decimal? InvoiceDiscountAmount { get; set; }
    public GstInvoiceDetails? Gst { get; set; }
    public long SaleId
    {
        get; set;
    }
    public string InvoiceNumber { get; set; } = "";
    public DateTime SaleDate
    {
        get; set;
    }
    public string? CustomerName
    {
        get; set;
    }
    public string? CustomerMobile
    {
        get; set;
    }
    public string? CustomerAddress
    {
        get; set;
    }
    public string? CustomerGstNumber
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
    public decimal TradeInAllowance
    {
        get; set;
    }
    public decimal? CashTendered
    {
        get; set;
    }
    public decimal? CashChange
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
    public bool HasHistoricalDetails
    {
        get; set;
    }
    public CompanySettings Company { get; set; } = new();
    public List<InvoiceItemResponse> Items { get; set; } = [];
    public List<InvoicePaymentResponse> Payments { get; set; } = [];
    public decimal AmountReceived => Payments.Where(p => p.PaymentMode != "CREDIT").Sum(p => p.Amount);
    public decimal CreditAmount => Payments.Where(p => p.PaymentMode == "CREDIT").Sum(p => p.Amount);
}
