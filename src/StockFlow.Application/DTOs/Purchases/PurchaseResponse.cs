namespace StockFlow.Application.DTOs.Purchases;

public class PurchaseResponse
{
    public long EditVersion { get; set; }
    public long StockCorrectionQuantity { get; set; }
    public string? InternalReference { get; set; }
    public long PurchaseId
    {
        get; set;
    }
    public string PurchaseNumber { get; set; } = string.Empty;
    public int SupplierId
    {
        get; set;
    }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierCode
    {
        get; set;
    }
    public string? SupplierGstNumber
    {
        get; set;
    }
    public string? SupplierAddress
    {
        get; set;
    }
    public bool HasSupplierSnapshot
    {
        get; set;
    }
    public int? PaymentTermsDays
    {
        get; set;
    }
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
    public decimal FreightAmount
    {
        get; set;
    }
    public decimal OtherLandedCost
    {
        get; set;
    }
    public string? InvoiceAttachmentName
    {
        get; set;
    }
    public decimal PaidAmount
    {
        get; set;
    }
    public decimal ReturnedAmount
    {
        get; set;
    }
    public decimal SupplierOutstanding => Math.Max(0, TotalAmount - PaidAmount - ReturnedAmount);
    public decimal Subtotal
    {
        get; set;
    }
    public decimal TaxAmount
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public decimal TotalAmount
    {
        get; set;
    }
    public string Status { get; set; } = string.Empty;
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
    public List<PurchaseItemResponse> Items { get; set; } = [];
}

public class PurchaseAttachment
{
    public string FileName { get; set; } = "invoice";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = [];
}

public class PurchaseItemResponse
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string AttributesJson { get; set; } = "{}";
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(AttributesJson);
    public string? Sku { get; set; }
    public int ReceivedQuantity
    {
        get; set;
    }
    public int DamagedQuantity
    {
        get; set;
    }
    public int PendingReceivedQuantity
    {
        get; set;
    }
    public int PendingDamagedQuantity
    {
        get; set;
    }
    public long PurchaseItemId
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
    public string ProductName { get; set; } = string.Empty;
    public int Quantity
    {
        get; set;
    }
    public decimal PurchasePrice
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public decimal TaxPercentage
    {
        get; set;
    }
    public decimal LineTotal
    {
        get; set;
    }
    public bool IsSerialTracked
    {
        get; set;
    }
    public string[] SerialNumbers { get; set; } = [];
    public string[] DamagedSerialNumbers { get; set; } = [];
    public string[] PendingSerialNumbers { get; set; } = [];
    public string[] PendingDamagedSerialNumbers { get; set; } = [];
    public string UnitOfMeasure { get; set; } = "PIECE";
    public int PackQuantity { get; set; } = 1;
    public decimal Mrp
    {
        get; set;
    }
    public string? SupplierProductCode
    {
        get; set;
    }
    public string? BatchNumber
    {
        get; set;
    }
    public DateTime? ManufacturingDate
    {
        get; set;
    }
    public DateTime? ExpiryDate
    {
        get; set;
    }
}
