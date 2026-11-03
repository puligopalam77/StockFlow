using StockFlow.Application.DTOs.Invoices;

namespace StockFlow.Application.Interfaces;

public interface IInvoiceService
{
    Task<InvoiceResponse?> GetInvoiceAsync(long saleId);
}
