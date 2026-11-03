using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Products;

public class CreateProductRequest
{
    [Range(1,long.MaxValue),System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public long? GstTaxRuleId { get; set; }
    public string? RegistrationBusinessProfile { get; set; }
    public Guid? RegistrationRequestId { get; set; }
    [MaxLength(30)] public List<VehicleFitment>? VehicleFitments { get; set; }
    public ProductRegistrationDefaults? RegistrationDefaults { get; set; }
    public string? ParentProductCode { get; set; }
    public string? DetailedDescription { get; set; }
    public bool AllowUnassociatedBrand { get; set; }

    public bool RemoveProductImage { get; set; }
    [MaxLength(5)]
    public List<ProductGalleryImage>? ProductImages { get; set; }
    [RegularExpression("^(ACTIVE|INACTIVE|DISCONTINUED)$")]
    public string? ProductStatus { get; set; }
    public List<RegistrationVariantRequest>? Variants
    {
        get; set;
    }

    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string ProductName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int CategoryId
    {
        get; set;
    }

    [Range(1, int.MaxValue)]
    public int? BrandId
    {
        get; set;
    }

    [StringLength(100)]
    public string? Model
    {
        get; set;
    }

    [StringLength(2000)]
    public string? Description
    {
        get; set;
    }

    [StringLength(20), RegularExpression("^[0-9]{4,8}$")]
    public string? HsnCode
    {
        get; set;
    }

    [Required, RegularExpression("^(PIECE|BOX|PAIR|SET)$")]
    public string UnitOfMeasure { get; set; } = "PIECE";

    [StringLength(500), Url]
    public string? ProductImageUrl
    {
        get; set;
    }

    [StringLength(255)]
    public string? ProductImageName
    {
        get; set;
    }

    [StringLength(100)]
    public string? ProductImageContentType
    {
        get; set;
    }

    [MaxLength(3000000)]
    public byte[]? ProductImageData
    {
        get; set;
    }
    public bool HasVariants
    {
        get; set;
    }

    [RegularExpression("^(QUANTITY|SERIAL)$")]
    public string? TrackingType
    {
        get; set;
    }

    [RegularExpression("^(MANUFACTURER|DEALER|BOTH)$")]
    public string? WarrantyProvider
    {
        get; set;
    }

    [StringLength(100)]
    public string? Sku
    {
        get; set;
    }

    [StringLength(100)]
    public string? Barcode
    {
        get; set;
    }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal PurchasePrice
    {
        get; set;
    }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal SellingPrice
    {
        get; set;
    }

    [Range(0, 100)]
    public decimal TaxPercentage
    {
        get; set;
    }

    [Range(0, 240)]
    public int WarrantyMonths
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int MinimumStockLevel
    {
        get; set;
    }
    public bool IsSerialTracked
    {
        get; set;
    }
}

public class RegistrationVariantRequest : VariantRequest
{
    public RegistrationVariantRequest()
    {
        ProductId = 1;
    }

    public long? VariantId
    {
        get; set;
    }
}
