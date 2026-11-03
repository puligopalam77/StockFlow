using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.DTOs.Invoices;

public class InvoiceItemResponse : SaleItemResponse
{
    // Original entered values retained in the posted request, before invoice allocation.
    public decimal? EnteredRate { get; set; }
    public decimal? EnteredDiscount { get; set; }
    public string? BrandName
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public int WarrantyMonths
    {
        get; set;
    }
}
