namespace StockFlow.Application.DTOs.Customers;

public class UpdateCustomerRequest : CreateCustomerRequest
{
    public bool IsActive { get; set; } = true;
}
