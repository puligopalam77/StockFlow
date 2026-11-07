namespace StockFlow.Application.DTOs.Inventory;
public sealed class ReplenishmentPolicy
{
 public long VariantId {get;set;} public int SupplierId {get;set;} public long Version {get;set;}
 public int LeadDays {get;set;} public int SafetyStock {get;set;} public int CoverageDays {get;set;}=14;
 public int Moq {get;set;}=1; public int OrderMultiple {get;set;}=1;
}
public sealed class ReplenishmentRow
{
 public int ProductId {get;set;} public long VariantId {get;set;} public string ProductName {get;set;}=""; public string VariantName {get;set;}=""; public string Sku {get;set;}="";
 public string UnitOfMeasure {get;set;}="PIECE"; public bool IsSerialTracked {get;set;} public long SerialUnits {get;set;} public long Stock {get;set;} public int Minimum {get;set;}
 public long Sold {get;set;} public long RestockedReturns {get;set;} public long NonSaleableReturns {get;set;} public int SaleDays {get;set;} public int HistoryDays {get;set;}
 public long OpenPo {get;set;} public long DraftPo {get;set;} public long IncomingTransfer {get;set;} public DateTime? LastReceipt {get;set;}
 public int? SupplierId {get;set;} public string? SupplierName {get;set;} public string? SupplierCode {get;set;} public ReplenishmentPolicy? Policy {get;set;}
 public decimal DailyDemand {get;set;} public decimal? CoverDays {get;set;} public decimal? ReorderPoint {get;set;} public decimal Target {get;set;}
 public long Position {get;set;} public long RawSuggestion {get;set;} public int Suggested {get;set;}
 public string Evidence {get;set;}=""; public string Status {get;set;}=""; public List<string> Warnings {get;set;}=[]; public string Fingerprint {get;set;}="";
}
public sealed record ReplenishmentSupplier(long VariantId,int SupplierId,string SupplierName,string SupplierCode);
public sealed record ReplenishmentPage(IReadOnlyList<ReplenishmentRow> Rows,int Total,int Page,int PageSize,DateTime Cutoff,int Days,object Summary);
public sealed record ReplenishmentDraftLine(long VariantId,string Fingerprint,int Quantity,decimal PurchasePrice,decimal TaxPercentage);
public sealed record ReplenishmentDraftRequest(Guid OperationId,int SupplierId,int Days,List<ReplenishmentDraftLine> Lines);
