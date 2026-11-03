using System.ComponentModel.DataAnnotations;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class SalesService : ISalesService
{
    private readonly ISalesRepository repository;
    private readonly RetailVariantSelection? selection;
    // ASP.NET supplies these services when it creates this class.
    public SalesService(ISalesRepository repository, RetailVariantSelection? selection = null)
    {
        this.repository = repository;
        this.selection = selection;
    }

    public Task<IEnumerable<SaleResponse>> GetAllAsync(string? term, int? customerId, int offset, DateTime? from = null, DateTime? to = null)
    {
        if (offset < 0 || customerId <= 0 || from?.Date > to?.Date)
        {
            throw new ValidationException("Invalid sales filters or date range.");
        }

        return repository.GetAllAsync(term, customerId, offset, from, to);
    }

    public Task<SaleResponse?> GetByIdAsync(long id)
    {
        return repository.GetByIdAsync(id);
    }

    public Task<SalesListPage> GetListAsync(string? term, int? customerId, int offset, int limit, DateTime? from, DateTime? to, string? status, string sort = "date", bool ascending = false, string? paymentStatus = null, string? paymentMethod = null)
    {
        paymentStatus = string.IsNullOrWhiteSpace(paymentStatus) ? null : paymentStatus.Trim().ToUpperInvariant();
        paymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? null : paymentMethod.Trim().ToUpperInvariant();
        if (term?.Trim().Length > 200 || paymentStatus is not (null or "PAID" or "PARTIAL" or "UNPAID" or "ADJUSTED") || paymentMethod is not (null or "CASH" or "UPI" or "CARD" or "DEBIT_CARD" or "CREDIT_CARD" or "BANK_TRANSFER" or "MIXED" or "CREDIT" or "CHEQUE"))
            throw new ValidationException("Invalid sales search or payment filters.");
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if(offset < 0 || limit is < 1 or > 100 || customerId <= 0 || from?.Date > to?.Date || status is not (null or "DRAFT" or "COMPLETED" or "CANCELLED" or "OUTSTANDING" or "OVERDUE" or "PARTIALLY_RETURNED" or "FULLY_RETURNED"))
            throw new ValidationException("Invalid sales filters or date range.");
        if(sort is not ("date" or "invoice" or "customer" or "items" or "total" or "payment" or "status" or "paid" or "due" or "paymentStatus")) throw new ValidationException("Invalid invoice sort.");
        return repository.GetListAsync(term,customerId,offset,limit,from,to,status,sort,ascending,paymentStatus,paymentMethod);
    }

    public async Task<SaleQuote> QuoteAsync(CreateSaleRequest request)
    {
        SaleRules.Validate(request, false);
        if(selection is not null)await selection.ValidateAsync(request.Items.Where(i=>i.ManualItem is null).Select(i=>(i.ProductId,i.VariantId,i.ProductUnitId)));
        return await repository.QuoteAsync(request);
    }

    public async Task<long> CreateSaleAsync(CreateSaleRequest request, int userId)
    {
        SaleRules.Validate(request, true);
        if(selection is not null)await selection.ValidateAsync(request.Items.Where(i=>i.ManualItem is null).Select(i=>(i.ProductId,i.VariantId,i.ProductUnitId)));
        return await repository.CreateSaleAsync(request, userId);
    }

    public Task<long?> RecoverCompletedAsync(CreateSaleRequest request, int userId)
    {
        SaleRules.Validate(request, true);
        return repository.RecoverCompletedAsync(request, userId);
    }
}
