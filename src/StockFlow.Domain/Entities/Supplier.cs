namespace StockFlow.Domain.Entities;

public class Supplier
{
    public bool UseStructuredAddress { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? District { get; set; }
    public int SupplierId
    {
        get; set;
    }
    public string SupplierCode { get; set; } = "";
    public string? SupplierType
    {
        get; set;
    }
    public string? AlternateMobile
    {
        get; set;
    }
    public string CountryCode { get; set; } = "IN";
    public string? State
    {
        get; set;
    }
    public string? City
    {
        get; set;
    }
    public string? PostalCode
    {
        get; set;
    }
    public int? PaymentTermsDays
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
    public string SupplierName { get; set; } = string.Empty;
    public string? ContactPerson
    {
        get; set;
    }
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
    public DateTime? UpdatedAt
    {
        get; set;
    }
}
