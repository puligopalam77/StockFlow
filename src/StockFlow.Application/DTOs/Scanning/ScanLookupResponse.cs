namespace StockFlow.Application.DTOs.Scanning;

public class ScanLookupResponse
{
    public decimal? Mrp { get; set; }
    public string? ProductCode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string AttributesJson { get; set; } = "{}";
    public Dictionary<string,string> Attributes => StockFlow.Application.Common.VariantIdentity.Read(AttributesJson);
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(Attributes);
    public string? Sku { get; set; }
    public string UnitOfMeasure { get; set; } = "PIECE";
    public string MatchType { get; set; } = "";
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
    public long? SaleId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string? BrandName
    {
        get; set;
    }
    public string? CategoryName
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public string? ProductBarcode
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
    public string? UnitBarcode
    {
        get; set;
    }
    public string? UnitStatus
    {
        get; set;
    }
    public bool IsSerialTracked
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public decimal SellingPrice
    {
        get; set;
    }
    public decimal TaxPercentage
    {
        get; set;
    }
    public long CurrentStock
    {
        get; set;
    }
    public bool IsAvailable
    {
        get; set;
    }
}
