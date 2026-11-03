namespace StockFlow.Domain.Entities;

public class WarrantyClaim
{
    public long WarrantyClaimId
    {
        get; set;
    }
    public long WarrantyId
    {
        get; set;
    }
    public string ClaimNumber { get; set; } = "";
    public DateTime ClaimDate
    {
        get; set;
    }
    public string Complaint { get; set; } = "";
    public string ClaimStatus { get; set; } = "OPEN";
    public string? ResolutionType
    {
        get; set;
    }
    public string? ResolutionNotes
    {
        get; set;
    }
    public long? ReplacementProductUnitId
    {
        get; set;
    }
    public long? ReplacementWarrantyId
    {
        get; set;
    }
    public int CreatedBy
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
