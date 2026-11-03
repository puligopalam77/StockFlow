using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Application.Interfaces;

public interface IQuotationRepository
{
    Task<QuotationPage> ListAsync(string? term, string? status, DateTime? from, DateTime? to, int page, int pageSize);
    Task<QuotationDetail> GetAsync(Guid id, int? revision = null);
    Task<SaleQuote> PreviewAsync(CreateSaleRequest request);
    Task<long> SaveAsync(SaveQuotationRequest request);
    Task<long> StateAsync(Guid id, QuotationStateRequest request);
    Task<object> CheckoutAsync(Guid id, QuotationCheckoutRequest request);
    Task RequireAsync(string action);
}
