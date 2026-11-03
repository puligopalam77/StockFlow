using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Products;

public sealed class ProductRegistrationDefaults
{
    [MaxLength(3)] public List<string> VariantAxes { get; set; } = [];
    [Range(1,int.MaxValue)] public int? PreferredSupplierId { get; set; }
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal PurchasePrice { get; set; }
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal SellingPrice { get; set; }
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal Mrp { get; set; }
    [Range(1,10000)] public int PackQuantity { get; set; } = 1;
    [Range(0,999999999)] public int ReorderLevel { get; set; }
    public bool PricesIncludeTax { get; set; }
    public bool? CatalogPricesIncludeTax { get; set; }
}
