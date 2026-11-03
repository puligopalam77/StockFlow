using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Customers;

public class CreateCustomerRequest
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string CustomerName { get; set; } = string.Empty;

    [StringLength(10)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone, ErrorMessage = StockFlow.Application.Common.FieldPatterns.PhoneMessage)]
    public string? MobileNumber
    {
        get; set;
    }

    [StringLength(150)]
    [EmailAddress]
    public string? Email
    {
        get; set;
    }
    [StringLength(1000)]
    public string? Address
    {
        get; set;
    }

    [StringLength(15)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Gst, ErrorMessage = StockFlow.Application.Common.FieldPatterns.GstMessage)]
    public string? GstNumber
    {
        get; set;
    }
}
