namespace StockFlow.Application.Interfaces;

public record ShopAssistantTotal(long StoreId, string StoreName, long InvoiceCount, decimal SalesAmount, decimal Collected);

public interface IHelpRetailRepository
{
    Task<IReadOnlyList<ShopAssistantTotal>> CompareShopsAsync(SalesPeriod period, CancellationToken cancellation);
}
