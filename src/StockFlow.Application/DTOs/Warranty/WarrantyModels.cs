namespace StockFlow.Application.DTOs.Warranty;

public class WarrantyLookupResponse
{
    public long? TotalCount { get; set; }
    public long WarrantyId
    {
        get; set;
    }
    public long SaleId
    {
        get; set;
    }
    public long SaleItemId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long? ProductUnitId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string? BrandName
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public string SerialSource { get; set; } = "LEGACY_UNKNOWN";
    public string? ManufacturerSerialNumber => SerialSource == "MANUFACTURER" ? SerialNumber : null;
    public string? InternalUnitIdentifier => SerialSource == "INTERNAL" ? SerialNumber : null;
    public string? SerialNumber
    {
        get; set;
    }
    public string InvoiceNumber { get; set; } = "";
    public DateTime SaleDate
    {
        get; set;
    }
    public string? CustomerName
    {
        get; set;
    }
    public string? CustomerMobile
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
    public string WarrantyStatus { get; set; } = "";
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
    public bool IsUnderWarranty
    {
        get; set;
    }
    public bool CanCreateClaim
    {
        get; set;
    }
    public int RemainingDays
    {
        get; set;
    }
    public long? ParentWarrantyId
    {
        get; set;
    }
    public long? ReplacementWarrantyId
    {
        get; set;
    }
    public string? ReplacementSerialNumber
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
}

public class CreateWarrantyClaimRequest
{
    public Guid RequestId
    {
        get; set;
    }
    public long WarrantyId
    {
        get; set;
    }
    public string Complaint { get; set; } = "";
}

public class ResolveWarrantyClaimRequest
{
    public Guid RequestId
    {
        get; set;
    }
    public string ExpectedStatus { get; set; } = "";
    public string ClaimStatus { get; set; } = "";
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
    public string? ReplacementWarrantyRule
    {
        get; set;
    }
    public DateTime? CustomWarrantyEndDate
    {
        get; set;
    }
}

public class WarrantyCoverageRequest
{
    public Guid RequestId
    {
        get; set;
    }
    public string? WarrantyType
    {
        get; set;
    }
    public string? WarrantyProvider
    {
        get; set;
    }
    public string Reason { get; set; } = "";
}

public class WarrantyClaimResponse
{
    public long? TotalCount { get; set; }
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
    public string ProductName { get; set; } = "";
    public string? SerialNumber
    {
        get; set;
    }
    public string? CustomerName
    {
        get; set;
    }
    public string Complaint { get; set; } = "";
    public string ClaimStatus { get; set; } = "";
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
}

public class WarrantyEventResponse
{
    public long WarrantyEventId
    {
        get; set;
    }
    public long? WarrantyClaimId
    {
        get; set;
    }
    public string ActionName { get; set; } = "";
    public string? OldStatus
    {
        get; set;
    }
    public string? NewStatus
    {
        get; set;
    }
    public string? Remarks
    {
        get; set;
    }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt
    {
        get; set;
    }
}
