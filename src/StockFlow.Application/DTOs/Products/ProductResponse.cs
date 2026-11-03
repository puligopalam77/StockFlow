namespace StockFlow.Application.DTOs.Products;

public class ProductResponse
{
    public int SkuCount { get; set; }
    public int? ConfiguredVariantCount { get; set; }
    public List<ProductSkuPreview> SkuPreviews { get; set; } = [];
    public string? DefaultVariantSku { get; set; }
    public bool HasMatchingSku { get; set; }
    public string? EditVersion { get; set; }
    public bool HasInventoryHistory { get; set; }
    public bool CategoryChangeLocked { get; set; }
    public long[] HistoryVariantIds { get; set; } = [];
    public List<VehicleFitment>? VehicleFitments { get; set; }
    public ProductRegistrationDefaults? RegistrationDefaults { get; set; }
    public string? ParentProductCode { get; set; }
    public string? DetailedDescription { get; set; }

    public List<ProductGalleryImage> ProductImages { get; set; } = [];
    public string ProductStatus { get; set; } = "ACTIVE";
    public int ProductId
    {
        get; set;
    }
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId
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
    public string? Description
    {
        get; set;
    }
    public string? HsnCode
    {
        get; set;
    }
    public string UnitOfMeasure { get; set; } = "PIECE";
    public string? ProductImageUrl
    {
        get; set;
    }
    public string? ProductImageName
    {
        get; set;
    }
    public bool HasProductImage => ProductImageName is not null;
    public string ModelStatus => IsActive ? "ACTIVE" : "DISCONTINUED";
    public bool HasVariants
    {
        get; set;
    }
    public string TrackingType => IsSerialTracked ? "SERIAL" : "QUANTITY";
    public string? WarrantyProvider
    {
        get; set;
    }
    public string? Sku
    {
        get; set;
    }
    public string? Barcode
    {
        get; set;
    }
    public decimal PurchasePrice
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
    public int WarrantyMonths
    {
        get; set;
    }
    public int MinimumStockLevel
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
    public DateTime CreatedAt
    {
        get; set;
    }
    public string CategoryName { get; set; } = string.Empty;
    public string? BrandName
    {
        get; set;
    }
}

public class ProductSkuPreview
{
    public long VariantId { get; set; }
    public string Sku { get; set; } = "";
    public string? VariantName { get; set; }
}

public class ProductImage
{
    public string FileName { get; set; } = "product-image";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = [];
}
