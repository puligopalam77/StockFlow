using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Invoices;

public record CompanySalesperson(int UserId, string FullName);

public class CompanySettings
{
    [StringLength(150)] public string? LegalBusinessName { get; set; }
    [Required, StringLength(100)] public string City { get; set; } = "";
    [Required, StringLength(100)] public string State { get; set; } = "";
    [Required, RegularExpression("^[1-9][0-9]{5}$", ErrorMessage = "Enter a valid six-digit PIN code.")]
    public string PinCode { get; set; } = "";
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone), StringLength(10)] public string? AlternatePhone { get; set; }
    [Url, StringLength(250)] public string? Website { get; set; }
    [RegularExpression("^[A-Z]{5}[0-9]{4}[A-Z]$", ErrorMessage = "Enter a valid PAN number."), StringLength(10)] public string? PanNumber { get; set; }
    [Required, StringLength(30), RegularExpression("^[A-Za-z0-9{}_-]+$", ErrorMessage = "Use letters, numbers, dashes and the {{YYYY}} year placeholder.")]
    public string InvoicePrefix { get; set; } = "INV-{YYYY}-";
    [Required, StringLength(30), RegularExpression("^[A-Za-z0-9{}_-]+$", ErrorMessage = "Use letters, numbers, dashes and the {{YYYY}} year placeholder.")]
    public string ReceiptPrefix { get; set; } = "GRN-{YYYY}-";
    [Required, RegularExpression("^INR$")] public string Currency { get; set; } = "INR";
    [Required, RegularExpression("^(EXCLUSIVE|INCLUSIVE)$")] public string DefaultTaxMode { get; set; } = "EXCLUSIVE";
    [Range(0, 365)] public int? DefaultPaymentTermsDays { get; set; }
    [Range(1, int.MaxValue)] public int? DefaultSalespersonId { get; set; }
    [StringLength(500)] public string? TermsAndConditions { get; set; }
    public string? LogoDataUrl { get; set; }
    public string? SalespersonName { get; set; }
    [Required, StringLength(150)]
    public string CompanyName { get; set; } = "";

    [Required, StringLength(2000)]
    public string? Address
    {
        get; set;
    }

    [Required, StringLength(10)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Phone, ErrorMessage = StockFlow.Application.Common.FieldPatterns.PhoneMessage)]
    public string? MobileNumber
    {
        get; set;
    }

    [Required, EmailAddress, StringLength(150)]
    public string? Email
    {
        get; set;
    }

    [StringLength(15)]
    [RegularExpression(StockFlow.Application.Common.FieldPatterns.Gst, ErrorMessage = StockFlow.Application.Common.FieldPatterns.GstMessage)]
    public string? GstNumber
    {
        get; set;
    }

    [StringLength(250)]
    public string? InvoiceFooter
    {
        get; set;
    }
}
