using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Purchases;

public class StockIntakeRequest
{
    public Guid RequestId
    {
        get; set;
    }
    public long? LocationId
    {
        get; set;
    }
    public DateTime ReceivedDate
    {
        get; set;
    }
    public int? SupplierId
    {
        get; set;
    }

    [StringLength(100)]
    public string? SupplierInvoiceNumber
    {
        get; set;
    }

    [StringLength(2000)]
    public string? Notes
    {
        get; set;
    }
    public List<CreatePurchaseItemRequest> Items { get; set; } = [];
}

public class StockIntakeResult
{
    public long IntakeId
    {
        get; set;
    }
    public long? PurchaseId
    {
        get; set;
    }
    public string Kind { get; set; } = "";
}
