using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Purchases;

public class ReceivePurchaseRequest
{
    public long? LocationId
    {
        get; set;
    }
    public Guid RequestId
    {
        get; set;
    }
    public DateTime ReceivedDate
    {
        get; set;
    }

    [StringLength(100)]
    public string? SupplierInvoiceNumber
    {
        get; set;
    }
    public DateTime? SupplierInvoiceDate
    {
        get; set;
    }

    [StringLength(2000)]
    public string? Notes
    {
        get; set;
    }
    public List<ReceivePurchaseItem> Items { get; set; } = [];
}

public class ReceivePurchaseItem
{
    public long PurchaseItemId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public int DamagedQuantity
    {
        get; set;
    }
    public List<string> SerialNumbers { get; set; } = [];
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

public class PurchaseReceiptResponse
{
    public long ReceiptId
    {
        get; set;
    }
    public string ReceiptNumber { get; set; } = "";
    public string Status { get; set; } = "COMPLETED";
    public DateTime ReceivedDate
    {
        get; set;
    }
    public string? SupplierInvoiceNumber
    {
        get; set;
    }
    public DateTime? SupplierInvoiceDate
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public int DamagedQuantity
    {
        get; set;
    }
}
