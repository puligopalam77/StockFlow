namespace StockFlow.Application.DTOs.Dashboard;

public class BusinessDashboardSummaryResponse
{
    public decimal? CustomerOutstandingAmount { get; set; }
    public decimal? OverduePaymentAmount { get; set; }
    public long? OverdueInvoiceCount { get; set; }
    public DateTime Today
    {
        get; set;
    }
    public decimal? TodaySalesAmount
    {
        get; set;
    }
    public decimal? MonthSalesAmount
    {
        get; set;
    }
    public long? TodayInvoiceCount
    {
        get; set;
    }
    public long LowStockCount
    {
        get; set;
    }
    public long PendingServiceCount
    {
        get; set;
    }
    public long ReadyServiceCount
    {
        get; set;
    }
    public long ExpiringWarrantyCount
    {
        get; set;
    }
}

public class RecentSaleResponse
{
    public long SaleId
    {
        get; set;
    }
    public string InvoiceNumber { get; set; } = "";
    public string? CustomerName
    {
        get; set;
    }
    public DateTime SaleDate
    {
        get; set;
    }
    public decimal TotalAmount
    {
        get; set;
    }
    public string Payment { get; set; } = "";
}

public class RecentServiceResponse
{
    public long ServiceRequestId
    {
        get; set;
    }
    public string ServiceNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? ExpectedDeliveryDate
    {
        get; set;
    }
}

public class LowStockResponse
{
    public long? TotalCount { get; set; }
    public int ProductId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public long CurrentStock
    {
        get; set;
    }
    public int MinimumStockLevel
    {
        get; set;
    }
    public long Required
    {
        get; set;
    }
}

public class DashboardMovementSearch
{
    public bool Incoming
    {
        get; set;
    }
    public string? Type
    {
        get; set;
    }
    public string? Term
    {
        get; set;
    }
    public DateTime? From
    {
        get; set;
    }
    public DateTime? To
    {
        get; set;
    }
    public int Offset
    {
        get; set;
    }
}

public class GlobalSearchResult
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Detail
    {
        get; set;
    }
    public string Url { get; set; } = "";
}

public class DashboardUser
{
    public int UserId
    {
        get; set;
    }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool IsActive
    {
        get; set;
    }
}
