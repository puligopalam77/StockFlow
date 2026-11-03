using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Gst;

public sealed class GstLine
{
    public long? SourceLineId { get; set; }
    public int? ProductId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsManual { get; set; }
    [Required,StringLength(300)] public string Description { get; set; } = "";
    [Required] public string HsnSac { get; set; } = "";
    [Required] public string Uqc { get; set; } = "NOS";
    public string Classification { get; set; } = "GOODS";
    public string Treatment { get; set; } = "TAXABLE";
    public decimal Quantity { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Utgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Cess { get; set; }
    public decimal Total { get; set; }
}
public sealed class GstRecordRequest
{
    public Guid RequestId { get; set; }
    public long RegistrationId { get; set; }
    public string Kind { get; set; } = "INVOICE";
    public string Direction { get; set; } = "PURCHASE";
    [Required,StringLength(100)] public string DocumentNumber { get; set; } = "";
    public DateTime DocumentDate { get; set; }
    public int? SupplierId { get; set; }
    [Required] public List<long> PurchaseIds { get; set; } = [];
    public long? ParentDocumentId { get; set; }
    [Required,StringLength(2,MinimumLength=2)] public string PlaceOfSupply { get; set; } = "";
    public string SupplierStateCode { get; set; } = "";
    [Required,StringLength(1000,MinimumLength=3)] public string EvidenceReference { get; set; } = "";
    [StringLength(1000)] public string? Reason { get; set; }
    [Required,MinLength(1),MaxLength(200)] public List<GstLine> Lines { get; set; } = [];
}
public sealed class GstDocument
{
    public long DocumentId { get; set; }
    public long RegistrationId { get; set; }
    public string BusinessProfileCode { get; set; } = "";
    public long StoreId { get; set; }
    public long LocationId { get; set; }
    public string StoreName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Kind { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public DateTime DocumentDate { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyGstin { get; set; }
    public string? PartyAddress { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? PartyStateCode { get; set; }
    public string PlaceOfSupply { get; set; } = "";
    public string RegistrationSnapshot { get; set; } = "{}";
    public string BusinessSnapshot { get; set; } = "{}";
    public long? SaleId { get; set; }
    public long? ParentDocumentId { get; set; }
    public long[] PurchaseIds { get; set; } = [];
    public string? EvidenceReference { get; set; }
    public string? Reason { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Utgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Cess { get; set; }
    public decimal InvoiceDiscount { get; set; }
    public decimal Total { get; set; }
    public string LinesJson { get; set; } = "[]";
    public string[] Warnings { get; set; } = [];
    public string ReviewStatus { get; set; } = "PENDING_REVIEW";
    public string SourceStatus { get; set; } = "RECORDED";
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalGst => Cgst+Sgst+Utgst+Igst+Cess;
    public int Sign => Kind=="CREDIT_NOTE"?-1:1;
}
public sealed class GstReportQuery
{
    public long RegistrationId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public bool RegistrationWide { get; set; }
    public string Report { get; set; } = "sales";
    public string? Term { get; set; }
    public string? PartyType { get; set; }
    public decimal? TaxRate { get; set; }
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 10;
    public string Sort { get; set; } = "date";
    public bool Ascending { get; set; }
}
public sealed class GstSummary
{
    public decimal TaxableSales { get; set; }
    public decimal OtherSupplyValue { get; set; }
    public decimal OutputGst { get; set; }
    public decimal RecordedPurchaseGst { get; set; }
    public decimal NoteAdjustments { get; set; }
    public long PendingReview { get; set; }
}
public sealed class GstHsnRow
{
    public string HsnSac { get; set; } = "";
    public string Uqc { get; set; } = "";
    public string PartyType { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Treatment { get; set; } = "";
    public decimal TaxRate { get; set; }
    public decimal Quantity { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Utgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Cess { get; set; }
    public decimal Total { get; set; }
}
public sealed class GstReport
{
    public string ScopeLabel { get; set; } = "";
    public string Gstin { get; set; } = "";
    public long Count { get; set; }
    public GstSummary Summary { get; set; } = new();
    public List<GstDocument> Documents { get; set; } = [];
    public List<GstHsnRow> Hsn { get; set; } = [];
    public long LegacySalesWithoutSnapshots { get; set; }
}
public sealed record GstReviewRequest(long DocumentId,string Status,string Notes);
public sealed record GstExportResult(byte[] Content,string FileName);
public sealed record GstSupplierOption(int SupplierId,string SupplierName,string? Gstin);
public sealed record GstPurchaseOption(long PurchaseId,int SupplierId,string PurchaseNumber);
public sealed record GstSourceOptions(IEnumerable<GstProduct> Products,IEnumerable<GstSupplierOption> Suppliers,IEnumerable<GstPurchaseOption> Purchases);
public sealed record GstExportHistory(long ExportId,long RegistrationId,string Report,DateTime FromDate,DateTime ToDate,string ScopeLabel,long RowCount,string Sha256,int CreatedBy,DateTime CreatedAt,int Version);
public sealed record GstReviewHistory(long ReviewId,string Status,string Notes,int CreatedBy,DateTime CreatedAt);
