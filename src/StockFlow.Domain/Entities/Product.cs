namespace StockFlow.Domain.Entities;

public class Product
{
    public long? GstTaxRuleId { get; set; }
    public int SkuCount { get; set; }
    public int? ConfiguredVariantCount { get; set; }
    public string? SkuPreviewsJson { get; set; }
    public string? DefaultVariantSku { get; set; }
    public bool HasMatchingSku { get; set; }
    public string? EditVersion { get; set; }
    public string? ExpectedEditVersion { get; set; }
    public bool HasInventoryHistory { get; set; }
    public bool CategoryChangeLocked { get; set; }
    public long[] HistoryVariantIds { get; set; } = [];
    public Guid? RegistrationRequestId { get; set; }
    public string? RegistrationRequestHash { get; set; }
    public string? RegistrationDefaultsJson { get; set; }
    public string? VehicleFitmentsJson { get; set; }
    public string? ParentProductCode { get; set; }
    public string? DetailedDescription { get; set; }

    public bool RemoveProductImage { get; set; }
    public string? ProductImagesJson { get; set; }
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
    public string? ProductImageContentType
    {
        get; set;
    }
    public byte[]? ProductImageData
    {
        get; set;
    }
    public bool HasVariants
    {
        get; set;
    }
    public string? WarrantyProvider
    {
        get; set;
    }
    public bool IsRegistrationEdit
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
    public DateTime? UpdatedAt
    {
        get; set;
    }
    public string CategoryName { get; set; } = string.Empty;
    public string? BrandName
    {
        get; set;
    }
}
