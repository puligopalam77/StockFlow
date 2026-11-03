using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Products;

public sealed class SupplierItemIdentifier
{
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal? ExpectedPurchaseCost { get; set; }
    [RegularExpression("^(PIECE|BOX|PAIR|SET)$")] public string? PurchaseUnit { get; set; }
    [Range(1,10000)] public int? PackConversion { get; set; }
    [Range(0,3650)] public int? LeadTimeDays { get; set; }
    public bool IsPreferred { get; set; }
    [Range(1,int.MaxValue)] public int SupplierId { get; set; }
    [Required,StringLength(100,MinimumLength=1)] public string SupplierSku { get; set; } = "";
    [StringLength(150)] public string? SupplierName { get; set; }
}

public class VariantRequest
{
    [MaxLength(30)] public List<VehicleFitment>? VehicleFitments { get; set; }
    public bool UseDefaultPurchasePrice { get; set; }
    public bool UseDefaultSellingPrice { get; set; }
    [Range(1,long.MaxValue)] public long? LocationId { get; set; }
    [MaxLength(20)] public List<SupplierItemIdentifier>? SupplierIdentifiers { get; set; }
    [Range(1, int.MaxValue)]
    public int ProductId
    {
        get; set;
    }

    [Required, StringLength(150)]
    public string VariantName { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 3)]
    public string Sku { get; set; } = "";

    [RegularExpression("^(INTERNAL|MANUFACTURER)$")]
    public string? BarcodeSource
    {
        get; set;
    }

    [StringLength(150)]
    public string? PrimaryBarcode
    {
        get; set;
    }

    [Range(0, 999999999)]
    public int? LocationMinimumStockLevel
    {
        get; set;
    }
    public bool ApplyLocationMinimumStock
    {
        get; set;
    }
    public bool IsSerialTracked
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

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal Mrp
    {
        get; set;
    }

    [Range(1, 10000)]
    public int PackQuantity { get; set; } = 1;

    [StringLength(100)]
    public string? SupplierProductCode
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

    [Range(0, 999999999)]
    public int MinimumStockLevel
    {
        get; set;
    }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string> Attributes { get; set; } = [];
    public List<string> Barcodes { get; set; } = [];
}

public class VariantResponse : VariantRequest
{
    public string? ParentProductCode { get; set; }
    public string? CategoryName { get; set; }
    public string? ParentCategoryName { get; set; }
    public string? BrandName { get; set; }
    public int? PreferredSupplierId { get; set; }
    public string? ManufacturerBarcode { get; set; }
    public bool SuggestBarcodeGeneration { get; set; } = true;
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(Attributes);
    public string? ProductImageUrl { get; set; }
    public bool HasProductImage { get; set; }
    public string UnitOfMeasure { get; set; } = "PIECE";
    public string? Model
    {
        get; set;
    }
    public long VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public bool IsDefault
    {
        get; set;
    }
    public string AttributesJson { get; set; } = "{}";
    public string? Barcode
    {
        get; set;
    }
    public long CurrentStock
    {
        get; set;
    }
}

public class UnitBarcodeRequest
{
    [StringLength(150)]
    public string? ManufacturerBarcode
    {
        get; set;
    }
    public bool GenerateInternal
    {
        get; set;
    }
}
