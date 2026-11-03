namespace StockFlow.Application.DTOs.Dashboard;

public class DashboardInventorySummaryResponse
{
    public long TotalProducts
    {
        get; set;
    }
    public long StockInflow
    {
        get; set;
    }
    public long StockOutflow
    {
        get; set;
    }
    public long CurrentStock
    {
        get; set;
    }
    public long? TotalSuppliers
    {
        get; set;
    }
    public long TotalCustomers
    {
        get; set;
    }
    public long TodayInflow
    {
        get; set;
    }
    public long TodayOutflow
    {
        get; set;
    }
}

public class DashboardResponse : DashboardInventorySummaryResponse
{
    public BusinessDashboardSummaryResponse Business { get; set; } = new();
    public List<RecentSaleResponse> RecentSales { get; set; } = [];
    public List<RecentServiceResponse> RecentServices { get; set; } = [];
    public List<LowStockResponse> LowStock { get; set; } = [];
    public List<MonthlyStock> Months { get; set; } = [];
    public List<CategoryStock> Categories { get; set; } = [];
    public List<RecentStock> RecentInflow { get; set; } = [];
    public List<RecentStock> RecentOutflow { get; set; } = [];
}

public class MonthlyStock
{
    public string Period { get; set; } = "";
    public DateTime Month
    {
        get; set;
    }
    public long Inflow
    {
        get; set;
    }
    public long Outflow
    {
        get; set;
    }
}

public class CategoryStock
{
    public string CategoryName { get; set; } = string.Empty;
    public long Quantity
    {
        get; set;
    }
}

public class RecentStock
{
    public long? TotalCount { get; set; }
    public long InventoryTransactionId
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
    public string ProductName { get; set; } = string.Empty;
    public long Quantity
    {
        get; set;
    }
    public string? PartyName
    {
        get; set;
    }
    public string TransactionType { get; set; } = string.Empty;
    public string? ReferenceNumber
    {
        get; set;
    }
    public string? ReferenceUrl
    {
        get; set;
    }
    public string MovementLabel => TransactionType.Replace('_', ' ') + (ReferenceNumber is null ? "" : " " + ReferenceNumber);
}
