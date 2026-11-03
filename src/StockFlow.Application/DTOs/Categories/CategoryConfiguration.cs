using StockFlow.Application.DTOs.Products;

namespace StockFlow.Application.DTOs.Categories;

public sealed class CategoryInventoryDefaults
{
    public string StockUnit { get; set; } = "PIECE";
    public string TrackingMethod { get; set; } = "QUANTITY";
    public bool AllowProductOverride { get; set; } = true;
    public bool GenerateSkuAutomatically { get; set; } = true;
    public bool SuggestBarcodeGeneration { get; set; } = true;
    public int ReorderLevel { get; set; }
    public bool EnableVehicleCompatibility { get; set; }
    public bool EnableWarranty { get; set; }
    public bool RequireManufacturerPartNumber { get; set; }
    public bool EnableExpiryTracking { get; set; }
}

public sealed class CategoryConfiguration
{
    public long SchemaVersion { get; set; }
    public List<CatalogAttribute> Attributes { get; set; } = [];
    public List<CategoryAttributeRule> Fields { get; set; } = [];
    public CategoryInventoryDefaults Inventory { get; set; } = new();
}
