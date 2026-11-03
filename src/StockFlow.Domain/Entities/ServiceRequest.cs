namespace StockFlow.Domain.Entities;

public class ServiceRequest
{
    public long ServiceRequestId
    {
        get; set;
    }
    public string ServiceNumber { get; set; } = "";
    public int CustomerId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long VariantId
    {
        get; set;
    }
    public long? ProductUnitId
    {
        get; set;
    }
    public long? WarrantyId
    {
        get; set;
    }
    public string Complaint { get; set; } = "";
    public string? Diagnosis
    {
        get; set;
    }
    public string? RepairDetails
    {
        get; set;
    }
    public decimal ServiceCharge
    {
        get; set;
    }
    public string Status { get; set; } = "RECEIVED";
    public DateTime ReceivedDate
    {
        get; set;
    }
    public DateTime? ExpectedDeliveryDate
    {
        get; set;
    }
    public DateTime? DeliveredDate
    {
        get; set;
    }
    public int? AssignedTo
    {
        get; set;
    }
    public string? Notes
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
    public long Version
    {
        get; set;
    }
    public bool? UnderWarrantyAtReceipt
    {
        get; set;
    }
}
