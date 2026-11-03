using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Purchases;

public class CreatePurchaseItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }

    [Range(1, int.MaxValue)]
    public int Quantity
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int? ReceivedQuantity
    {
        get; set;
    }

    [Range(0, int.MaxValue)]
    public int DamagedQuantity
    {
        get; set;
    }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal PurchasePrice
    {
        get; set;
    }
    public decimal DiscountAmount
    {
        get; set;
    }

    [Range(0, 100)]
    public decimal TaxPercentage
    {
        get; set;
    }

    [MaxLength(10000)]
    public List<string> SerialNumbers { get; set; } = [];

    [MaxLength(10000)]
    public List<string> DamagedSerialNumbers { get; set; } = [];

    [RegularExpression("^(PIECE|BOX|PAIR|SET)$")]
    public string UnitOfMeasure { get; set; } = "PIECE";

    [Range(1, 10000)]
    public int PackQuantity { get; set; } = 1;

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal Mrp
    {
        get; set;
    }

    [StringLength(100)]
    public string? SupplierProductCode
    {
        get; set;
    }

    [StringLength(100)]
    public string? BatchNumber
    {
        get; set;
    }
    public DateTime? ManufacturingDate
    {
        get; set;
    }
    public DateTime? ExpiryDate
    {
        get; set;
    }
}
