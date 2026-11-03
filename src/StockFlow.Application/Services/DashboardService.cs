using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Dashboard;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public DashboardService(IDashboardRepository repository)
    {
        this.repository = repository;
    }

    public static readonly string[] Periods = ["7days", "30days", "6months", "12months"];
    public Task<DashboardResponse> GetAsync(bool isAdmin, bool canSeeSales = true)
    {
        return repository.GetAsync(isAdmin, canSeeSales);
    }

    public Task<DashboardInventorySummaryResponse> GetInventorySummaryAsync(bool isAdmin)
    {
        return repository.GetInventorySummaryAsync(isAdmin);
    }

    public Task<BusinessDashboardSummaryResponse> GetBusinessSummaryAsync(bool canSeeSales)
    {
        return repository.GetBusinessSummaryAsync(canSeeSales);
    }

    public Task<IEnumerable<MonthlyStock>> GetTrendAsync(string period)
    {
        if (!Periods.Contains(period))
        {
            throw new ValidationException("Choose 7days, 30days, 6months or 12months.");
        }

        return repository.GetTrendAsync(period);
    }

    public Task<IEnumerable<CategoryStock>> GetCategoryStockAsync()
    {
        return repository.GetCategoryStockAsync();
    }

    public Task<IEnumerable<RecentStock>> GetMovementsAsync(DashboardMovementSearch search, bool isAdmin, int limit = 100)
    {
        Page(search.Offset, limit);
        search.Term = Code(search.Term);
        var allowed = search.Incoming ? new[]
        {
            "PURCHASE",
            "CUSTOMER_RETURN",
            "ADJUSTMENT_IN",
            "SERVICE_IN"
        }

        : new[]
        {
            "SALE",
            "SUPPLIER_RETURN",
            "DAMAGED",
            "ADJUSTMENT_OUT",
            "SERVICE_OUT",
            "WARRANTY_REPLACEMENT"
        };
        if (search.Type is not null && !allowed.Contains(search.Type))
        {
            throw new ValidationException("Choose a movement type for this direction.");
        }

        if (search.From > search.To)
        {
            throw new ValidationException("Choose a valid date range.");
        }

        search.From = search.From?.Date;
        search.To = search.To?.Date;
        return repository.GetMovementsAsync(search, isAdmin, limit);
    }

    public Task<IEnumerable<RecentSaleResponse>> GetRecentSalesAsync()
    {
        return repository.GetRecentSalesAsync();
    }

    public Task<IEnumerable<RecentServiceResponse>> GetRecentServicesAsync()
    {
        return repository.GetRecentServicesAsync();
    }

    public Task<IEnumerable<LowStockResponse>> GetLowStockAsync(int offset, int limit = 100)
    {
        Page(offset, limit);
        return repository.GetLowStockAsync(offset, limit);
    }

    public Task<IEnumerable<GlobalSearchResult>> SearchAsync(string term)
    {
        term = Code(term);
        if (term.Length < 2)
        {
            throw new ValidationException("Enter at least two characters to search.");
        }

        return repository.SearchAsync(term);
    }

    public Task<IEnumerable<DashboardUser>> GetUsersAsync()
    {
        return repository.GetUsersAsync();
    }

    static string Code(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length > 200 || value.Any(char.IsControl))
        {
            throw new ValidationException("Search text must be at most 200 characters without control characters.");
        }

        return value;
    }

    static void Page(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100)
        {
            throw new ValidationException("Invalid page size or offset.");
        }
    }
}
