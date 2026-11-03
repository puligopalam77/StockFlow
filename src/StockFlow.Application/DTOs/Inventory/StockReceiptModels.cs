namespace StockFlow.Application.DTOs.Inventory;

public sealed class StockReceiptSearch
{
    public string Term { get; set; } = "";
    public string Type { get; set; } = "";
    public int? SupplierId
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
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int Offset => (Page - 1) * PageSize;
}

public sealed class StockReceiptHistoryResult
{
    public StockReceiptSummary Summary { get; set; } = new();
    public List<StockReceiptRow> Rows { get; set; } = [];
    public int TotalCount
    {
        get; set;
    }
    public int Page
    {
        get; set;
    }
    public int PageSize
    {
        get; set;
    }
}

public sealed class StockReceiptSummary
{
    public long Receipts
    {
        get; set;
    }
    public long UnitsReceived
    {
        get; set;
    }
    public decimal PurchaseValue
    {
        get; set;
    }
    public long Adjustments
    {
        get; set;
    }
}

public sealed class StockReceiptRow
{
    public string Source { get; set; } = "";
    public long SourceId
    {
        get; set;
    }
    public DateTime OccurredAt
    {
        get; set;
    }
    public string ReceiptNumber { get; set; } = "";
    public string MovementType { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public string? PartyName
    {
        get; set;
    }
    public int ItemCount
    {
        get; set;
    }
    public long Quantity
    {
        get; set;
    }
    public decimal? Value
    {
        get; set;
    }
    public string Status { get; set; } = "COMPLETED";
}

public sealed class StockReceiptItemHistoryResult
{
    public List<StockReceiptItemMovement> Rows { get; set; } = [];
    public int TotalCount
    {
        get; set;
    }
    public int Page
    {
        get; set;
    }
    public int PageSize
    {
        get; set;
    }
}

public sealed class StockReceiptItemMovement
{
    public DateTime OccurredAt
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public long Quantity
    {
        get; set;
    }
    public string MovementType { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public string ReceiptNumber { get; set; } = "";
    public string Source { get; set; } = "";
    public long SourceId
    {
        get; set;
    }
    public string? PartyName
    {
        get; set;
    }
}

public sealed class StockReceiptSupplier
{
    public int SupplierId
    {
        get; set;
    }
    public string SupplierName { get; set; } = "";
}

public sealed class StockReceiptDetail
{
    public string Source { get; set; } = "";
    public long SourceId
    {
        get; set;
    }
    public string ReceiptNumber { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public string? PartyName
    {
        get; set;
    }
    public string? SupplierInvoiceNumber
    {
        get; set;
    }
    public DateTime? SupplierInvoiceDate
    {
        get; set;
    }
    public DateTime ReceivedDate
    {
        get; set;
    }
    public string? ReferenceNumber
    {
        get; set;
    }
    public string ReceivedBy { get; set; } = "";
    public string Status { get; set; } = "COMPLETED";
    public string? Notes
    {
        get; set;
    }
    public decimal Subtotal
    {
        get; set;
    }
    public decimal Discount
    {
        get; set;
    }
    public decimal Tax
    {
        get; set;
    }
    public decimal Total
    {
        get; set;
    }
    public List<StockReceiptDetailItem> Items { get; set; } = [];
}

public sealed class StockReceiptDetailItem
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string AttributesJson { get; set; } = "{}";
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(AttributesJson);
    public string? Sku { get; set; }
    public string ProductName { get; set; } = "";
    public long Quantity
    {
        get; set;
    }
    public long DamagedQuantity
    {
        get; set;
    }
    public decimal? PurchasePrice
    {
        get; set;
    }
    public decimal Discount
    {
        get; set;
    }
    public decimal? TaxPercentage
    {
        get; set;
    }
    public decimal? Total
    {
        get; set;
    }
    public string? Disposition
    {
        get; set;
    }
    public string? BatchNumber
    {
        get; set;
    }
    public string[] SerialNumbers { get; set; } = [];
}
