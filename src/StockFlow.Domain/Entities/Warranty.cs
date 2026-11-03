namespace StockFlow.Domain.Entities;

public class Warranty
{
    public long WarrantyId
    {
        get; set;
    }
    public long SaleItemId
    {
        get; set;
    }
    public long? ProductUnitId
    {
        get; set;
    }
    public DateTime WarrantyStartDate
    {
        get; set;
    }
    public DateTime WarrantyEndDate
    {
        get; set;
    }
    public string WarrantyStatus { get; set; } = "ACTIVE";
    public string? WarrantyType
    {
        get; set;
    }
    public string? WarrantyProvider
    {
        get; set;
    }
    public int ClaimCount
    {
        get; set;
    }
    public long? ReplacementProductUnitId
    {
        get; set;
    }
    public long? ParentWarrantyId
    {
        get; set;
    }
    public string? ReplacementWarrantyRule
    {
        get; set;
    }
    public string? Notes
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
