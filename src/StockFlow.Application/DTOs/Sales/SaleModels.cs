namespace StockFlow.Application.DTOs.Sales;

public class CreateSaleRequest
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceQuotationId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceQuotationRevision { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceQuotationVersion { get; set; }
    // Null preserves the financial interpretation and hashes of legacy requests.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllocateInvoiceDiscount { get; set; }
    // Transport-only editor authority; excluded from financial idempotency payloads.
    [System.Text.Json.Serialization.JsonIgnore] public Guid? DraftEditorId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long? DraftRevision { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanOverridePrice
    {
        get; set;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanDiscount
    {
        get; set;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanApproveDiscount
    {
        get; set;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanTradeIn
    {
        get; set;
    }
    public string? AdjustmentReason
    {
        get; set;
    }
    public decimal? CashTendered
    {
        get; set;
    }
    public SaleTradeIn? TradeIn
    {
        get; set;
    }
    public long? LocationId
    {
        get; set;
    }
    public Guid RequestId
    {
        get; set;
    }
    public int? CustomerId
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
    public DateTime? PaymentDueDate { get; set; }
    public bool? PricesIncludeTax { get; set; }
    // Omitted on unmapped shops so historical request hashes/replays remain compatible.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? GstPlaceOfSupplyStateCode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    [System.ComponentModel.DataAnnotations.StringLength(1000)]
    public string? GstDeliveryAddress { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? GstRecipientDetailsRequested { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? CrmEnquiryId { get; set; }
    public string? Notes
    {
        get; set;
    }
    public List<CreateSaleItemRequest> Items { get; set; } = [];
    public List<CreatePaymentRequest> Payments { get; set; } = [];
}

public class SaleTradeIn
{
    public string ItemType { get; set; } = "";
    public string? BrandModel
    {
        get; set;
    }
    public string? SerialNumber
    {
        get; set;
    }
    public decimal AllowanceAmount
    {
        get; set;
    }
}

public class CreateSaleItemRequest
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PriceAdjustmentReason { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TaxAdjustmentReason { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AdjustmentReason { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? TaxOverride { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ManualSaleItem? ManualItem { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DiscountPercent { get; set; }
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
    public decimal SellingPrice
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }
}

public class ManualSaleItem
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long TaxRuleId { get; set; }
    public decimal? Mrp { get; set; }
}

public class CreatePaymentRequest
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? CollectionConfirmed { get; set; }
    public string PaymentMode { get; set; } = "CASH";
    // Optional classification of a CARD collection, never customer CREDIT.
    // Omit null to preserve historical idempotency payloads from older clients.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CardType { get; set; }
    public decimal Amount
    {
        get; set;
    }
    public string? ReferenceNumber
    {
        get; set;
    }
}

public class SaleQuote
{
    public StockFlow.Application.DTOs.Gst.SaleTaxBreakdown? TaxBreakdown { get; set; }
    public decimal ItemSubtotal { get; set; }
    public decimal ItemDiscountAmount { get; set; }
    public decimal InvoiceDiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal AmountWithoutGst => Subtotal - TaxableAmount;
    public SaleAdjustmentReview? AdjustmentReview { get; set; }
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
    public List<SaleLineQuote> Items { get; set; } = [];
}

public sealed record SaleAdjustmentReview(decimal ThresholdPercent, bool RequiresApproval, string ApprovalStatus)
{
    public long PolicyVersion { get; init; }
    public bool Exceptional { get; init; }
    public bool InvoiceReasonRequired { get; init; }
    public string? ApprovingUser { get; init; }
    public List<SaleLineReasonRequirement> LineReasons { get; init; } = [];
}
public sealed record SaleLineReasonRequirement(bool Discount, bool Price, bool Tax);

public class SaleLineQuote
{
    public decimal? Mrp { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetUnitPrice { get; set; }
    public decimal NetDiscountAmount { get; set; }
    public long CurrentStock
    {
        get; set;
    }
    public decimal CatalogPrice
    {
        get; set;
    }
    public int WarrantyMonths
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public decimal TaxPercentage
    {
        get; set;
    }
    public decimal TaxAmount
    {
        get; set;
    }
    public decimal LineTotal
    {
        get; set;
    }
}

public class SaleResponse
{
    public decimal? TaxableAmount { get; set; }
    public decimal? ItemSubtotal { get; set; }
    public decimal? ItemDiscountAmount { get; set; }
    public decimal? InvoiceDiscountAmount { get; set; }

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
    public long SaleId
    {
        get; set;
    }
    public string InvoiceNumber { get; set; } = "";
    public int? CustomerId
    {
        get; set;
    }
    public string? CustomerName
    {
        get; set;
    }
    public DateTime SaleDate
    {
        get; set;
    }
    public string Status { get; set; } = "";
    public string? Notes
    {
        get; set;
    }
    public string CreatedByName { get; set; } = "";
    public List<SaleItemResponse> Lines { get; set; } = [];
    public List<PaymentResponse> Payments { get; set; } = [];
}

public class SaleItemResponse
{
    public string? HsnSac { get; set; }
    public string? GstTreatment { get; set; }
    public long? TaxRuleId { get; set; }
    public bool IsManual { get; set; }
    public decimal? Mrp { get; set; }
    public string DiscountType { get; set; } = "FIXED";
    public decimal? DiscountPercent { get; set; }
    public string? Description { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string AttributesJson { get; set; } = "{}";
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(AttributesJson);
    public string? Sku { get; set; }
    public string UnitOfMeasure { get; set; } = "PIECE";
    public long SaleItemId
    {
        get; set;
    }
    public int? ProductId
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
    public string ProductName { get; set; } = "";
    public string SerialSource { get; set; } = "LEGACY_UNKNOWN";
    public string? ManufacturerSerialNumber => SerialSource == "MANUFACTURER" ? SerialNumber : null;
    public string? InternalUnitIdentifier => SerialSource == "INTERNAL" ? SerialNumber : null;
    public string? SerialNumber
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public decimal SellingPrice
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
    public DateTime? WarrantyStartDate
    {
        get; set;
    }
    public DateTime? WarrantyEndDate
    {
        get; set;
    }
    public string? WarrantyStatus
    {
        get; set;
    }
}

public class PaymentResponse
{
    public DateTime? PaymentDate { get; set; }
    public string PaymentMode { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CardType { get; set; }
    public decimal Amount
    {
        get; set;
    }
    public string? ReferenceNumber
    {
        get; set;
    }
}
