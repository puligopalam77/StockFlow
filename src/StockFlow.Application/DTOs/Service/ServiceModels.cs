using StockFlow.Domain.Entities;
using StockFlow.Application.DTOs.Warranty;

namespace StockFlow.Application.DTOs.Service;

public class CreateServiceRequest
{
    public long? LocationId
    {
        get; set;
    }
    public Guid RequestId
    {
        get; set;
    }
    public int CustomerId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
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
    public int? AssignedTo
    {
        get; set;
    }
    public DateTime? ExpectedDeliveryDate
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
}

public class ServiceMutation
{
    public long? LocationId
    {
        get; set;
    }
    public Guid RequestId
    {
        get; set;
    }
    public long ExpectedVersion
    {
        get; set;
    }
}

public class UpdateServiceRequest : ServiceMutation
{
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
    public int? AssignedTo
    {
        get; set;
    }
    public DateTime? ExpectedDeliveryDate
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
}

public class UpdateServiceStatusRequest : ServiceMutation
{
    public string Status { get; set; } = "";
    public string Remarks { get; set; } = "";
}

public class AddServicePartRequest : ServiceMutation
{
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public long? ProductUnitId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public decimal UnitPrice
    {
        get; set;
    }
}

public class ServiceRequestResponse : ServiceRequest
{
    public long? TotalCount { get; set; }
    public string CustomerName { get; set; } = "";
    public string? CustomerMobile
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string? SerialNumber
    {
        get; set;
    }
    public string? AssignedToName
    {
        get; set;
    }
    public bool IsUnderWarranty
    {
        get; set;
    }
    public string? WarrantyStatus
    {
        get; set;
    }
    public decimal PartsTotal
    {
        get; set;
    }
    public decimal TotalCharge => ServiceCharge + PartsTotal;
    public string[] AllowedTransitions { get; set; } = [];
}

public class ServiceSearch
{
    public string? Term
    {
        get; set;
    }
    public string? Status
    {
        get; set;
    }
    public bool PendingOnly
    {
        get; set;
    }
    public DateTime? From
    {
        get; set;
    }
    public DateTime? To
    {
        get; set;
    }
    public int Offset
    {
        get; set;
    }
}

public class ServiceTechnician
{
    public int UserId
    {
        get; set;
    }
    public string FullName { get; set; } = "";
}

public class ServiceUnitContext
{
    public long ProductUnitId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string? SerialNumber
    {
        get; set;
    }
    public string UnitStatus { get; set; } = "";
    public long? SaleId
    {
        get; set;
    }
    public string? SaleStatus
    {
        get; set;
    }
    public string? InvoiceNumber
    {
        get; set;
    }
    public int? CustomerId
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
    public WarrantyLookupResponse? Warranty
    {
        get; set;
    }
}
