namespace StockFlow.Application.Interfaces;

public record BusinessValueQuery(string Metric, SalesPeriod Period, string? Term = null);
public record BusinessValueResult(string Status, string Value);

public interface IHelpBusinessRepository
{
    Task<BusinessValueResult> GetAsync(BusinessValueQuery query, CancellationToken cancellation);
}
