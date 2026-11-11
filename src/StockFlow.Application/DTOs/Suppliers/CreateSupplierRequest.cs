using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Suppliers;

public class CreateSupplierRequest
{
    // Null keeps the legacy contract; true explicitly replaces the combined address.
    public bool? UseStructuredAddress { get; set; }
    [StringLength(250)]
    public string? AddressLine1 { get; set; }
    [StringLength(250)]
    public string? AddressLine2 { get; set; }
    [StringLength(100)]
    public string? District { get; set; }
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string SupplierName { get; set; } = string.Empty;

    [StringLength(100)]
    public string? ContactPerson
    {
        get; set;
    }

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

    [StringLength(500)]
    public string? Address
    {
        get; set;
    }

    [StringLength(15)]
    public string? GstNumber
    {
        get; set;
    }

    [StringLength(30)]
    public string? SupplierType
    {
        get; set;
    }

    [StringLength(10)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone, ErrorMessage = StockFlow.Application.Common.FieldPatterns.PhoneMessage)]
    public string? AlternateMobile
    {
        get; set;
    }

    [Required, StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; } = StockFlow.Application.Common.CountryPolicy.Code;

    [StringLength(100)]
    public string? State
    {
        get; set;
    }

    [StringLength(100)]
    public string? City
    {
        get; set;
    }

    [StringLength(20)]
    public string? PostalCode
    {
        get; set;
    }

    [Range(0, 365)]
    public int? PaymentTermsDays
    {
        get; set;
    }

    [StringLength(1000)]
    public string? Notes
    {
        get; set;
    }
}
