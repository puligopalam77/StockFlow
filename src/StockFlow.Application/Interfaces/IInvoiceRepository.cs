using StockFlow.Application.DTOs.Invoices;

namespace StockFlow.Application.Interfaces;

public interface IInvoiceRepository
{
    Task<InvoiceResponse?> GetInvoiceAsync(long saleId);
}
