using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Inventory;

public class InventoryBalance
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string AttributesJson { get; set; } = "{}";
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(AttributesJson);
    public string UnitOfMeasure { get; set; } = "PIECE";
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string VariantName { get; set; } = "";
    public string? ProductImageUrl { get; set; }
    public bool HasProductImage { get; set; }
    public string CategoryName { get; set; } = "";
    public string? BrandName
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
    public bool IsSerialTracked
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public int MinimumStockLevel
    {
        get; set;
    }
    public long CurrentStock
    {
        get; set;
    }
    public long AvailableUnits
    {
        get; set;
    }
    public bool IsLowStock => CurrentStock <= MinimumStockLevel;
    public bool HasDiscrepancy => IsSerialTracked && CurrentStock != AvailableUnits;
}

public class InventoryMovement
{
    public long? TotalCount { get; set; }
    public long InventoryTransactionId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string? SerialNumber
    {
        get; set;
    }
    public string TransactionType { get; set; } = "";
    public int Quantity
    {
        get; set;
    }
    public string? ReferenceType
    {
        get; set;
    }
    public long? ReferenceId
    {
        get; set;
    }
    public string? Remarks
    {
        get; set;
    }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt
    {
        get; set;
    }
}

public class InventoryUnit
{
    public long? TotalCount { get; set; }
    public string SerialSource { get; set; } = "LEGACY_UNKNOWN";
    public string? ManufacturerSerialNumber => SerialSource == "MANUFACTURER" ? SerialNumber : null;
    public string? InternalUnitIdentifier => SerialSource == "INTERNAL" ? SerialNumber : null;
    public long? PurchaseItemId { get; set; }
    public string?[] Barcodes { get; set; } = [];
    public long ProductUnitId
    {
        get; set;
    }
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public string? SerialNumber
    {
        get; set;
    }
    public string? Barcode
    {
        get; set;
    }
    public string UnitStatus { get; set; } = "";
    public long? PurchaseId
    {
        get; set;
    }
}

public class StockAdjustmentRequest
{
    public long? PurchaseId { get; set; }
    public long? PurchaseItemId { get; set; }
    public long? ExpectedVersion { get; set; }
    public long? LocationId
    {
        get; set;
    }
    public Guid RequestId
    {
        get; set;
    }

    [Range(1, int.MaxValue)]
    public int ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }

    [Required]
    public string TransactionType { get; set; } = "";

    [Range(1, 10000)]
    public int Quantity
    {
        get; set;
    }

    [Required, StringLength(1000)]
    public string Reason { get; set; } = "";
    public List<string> SerialNumbers { get; set; } = [];
    public List<long> ProductUnitIds { get; set; } = [];
}
