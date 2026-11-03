using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.Interfaces;

public interface ISalesRepository
{
    Task<SalesListPage> GetListAsync(string? term, int? customerId, int offset, int limit, DateTime? from, DateTime? to, string? status, string sort = "date", bool ascending = false, string? paymentStatus = null, string? paymentMethod = null);
    Task<IEnumerable<SaleResponse>> GetAllAsync(string? term, int? customerId, int offset, DateTime? from = null, DateTime? to = null);
    Task<SaleResponse?> GetByIdAsync(long id);
    Task<SaleQuote> QuoteAsync(CreateSaleRequest request);
    Task<long> CreateSaleAsync(CreateSaleRequest request, int userId);
    Task<long?> RecoverCompletedAsync(CreateSaleRequest request, int userId);
}
