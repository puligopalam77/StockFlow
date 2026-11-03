namespace StockFlow.Application.Interfaces;

public record TodaySoldQuantity(DateTime Date, long Quantity, long InvoiceCount);
public record SalesPeriod(string Period, DateTime? From = null, DateTime? To = null);
public record SalesPeriodSummary(DateTime From, DateTime To, long Quantity, long InvoiceCount, decimal Amount, decimal Collected = 0);

public interface ISalesEnquiryRepository
{
    Task<TodaySoldQuantity> GetTodaySoldQuantityAsync();
    Task<SalesPeriodSummary> GetSummaryAsync(SalesPeriod period, CancellationToken cancellation);
}
