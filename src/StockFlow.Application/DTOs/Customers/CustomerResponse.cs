namespace StockFlow.Application.DTOs.Customers;

public class CustomerResponse
{
    public int CustomerId
    {
        get; set;
    }
    public string CustomerName { get; set; } = string.Empty;
    public string? MobileNumber
    {
        get; set;
    }
    public string? Email
    {
        get; set;
    }
    public string? Address
    {
        get; set;
    }
    public string? GstNumber
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
