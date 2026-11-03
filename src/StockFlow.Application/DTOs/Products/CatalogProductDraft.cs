namespace StockFlow.Application.DTOs.Products;

public class CatalogProductDraft
{
    public string Barcode { get; set; } = "";
    public string SourceName { get; set; } = "Supplier / manufacturer catalogue";
    public int? SupplierId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public int? CategoryId
    {
        get; set;
    }
    public int? BrandId
    {
        get; set;
    }
    public string? Model
    {
        get; set;
    }
    public string? HsnCode
    {
        get; set;
    }
    public string UnitOfMeasure { get; set; } = "PIECE";
    public string TrackingType { get; set; } = "SERIAL";
    public decimal PurchasePrice
    {
        get; set;
    }
    public decimal SellingPrice
    {
        get; set;
    }
    public decimal Mrp
    {
        get; set;
    }
    public decimal TaxPercentage
    {
        get; set;
    }
    public int WarrantyMonths
    {
        get; set;
    }
    public string? WarrantyProvider
    {
        get; set;
    }
    public string? SupplierProductCode
    {
        get; set;
    }
    public Dictionary<string, string> Attributes { get; set; } = [];
    public string AttributesJson { get; set; } = "{}";
}
