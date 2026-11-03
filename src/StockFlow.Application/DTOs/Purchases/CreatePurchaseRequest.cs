using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Purchases;

public class CreatePurchaseRequest
{
    public long? ExpectedVersion { get; set; }
    [StringLength(100)] public string? InternalReference { get; set; }
    [StringLength(1000)] public string? ChangeReason { get; set; }
    // Optional for legacy order callers. Physical receipts supply a stable ID so
    // an interrupted response can be retried without posting stock twice.
    public Guid? RequestId { get; set; }

    public long? LocationId
    {
        get; set;
    }

    [Range(0, 365)]
    public int? PaymentTermsDays
    {
        get; set;
    }

    [Range(1, int.MaxValue)]
    public int SupplierId
    {
        get; set;
    }

    [StringLength(100)]
    public string? SupplierInvoiceNumber
    {
        get; set;
    }
    public DateTime PurchaseDate
    {
        get; set;
    }
    public DateTime? SupplierInvoiceDate
    {
        get; set;
    }
    public DateTime? ReceivedDate
    {
        get; set;
    }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal FreightAmount
    {
        get; set;
    }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal OtherLandedCost
    {
        get; set;
    }

    [StringLength(255)]
    public string? InvoiceAttachmentName
    {
        get; set;
    }

    [StringLength(100)]
    public string? InvoiceAttachmentContentType
    {
        get; set;
    }

    [MaxLength(5000000)]
    public byte[]? InvoiceAttachmentData
    {
        get; set;
    }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal DiscountAmount
    {
        get; set;
    }

    [StringLength(4000)]
    public string? Notes
    {
        get; set;
    }

    [Required, MinLength(1), MaxLength(200)]
    public List<CreatePurchaseItemRequest> Items { get; set; } = [];
    public bool CompleteImmediately
    {
        get; set;
    }
}
