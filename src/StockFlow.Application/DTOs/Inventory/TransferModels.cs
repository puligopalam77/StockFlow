using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Inventory;

public class CreateTransferRequest
{
    public Guid RequestId
    {
        get; set;
    }
    public long FromLocationId
    {
        get; set;
    }
    public long ToLocationId
    {
        get; set;
    }

    [StringLength(2000)]
    public string? Notes
    {
        get; set;
    }
    public List<TransferRequestLine> Items { get; set; } = [];
}

public class TransferRequestLine
{
    public long VariantId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
}

public class TransferActionRequest
{
    public Guid RequestId
    {
        get; set;
    }

    [StringLength(2000)]
    public string? Notes
    {
        get; set;
    }
    public List<TransferActionLine> Items { get; set; } = [];
}

public class TransferActionLine
{
    public long TransferItemId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public string Resolution { get; set; } = "RECEIVED";
    public List<long> UnitIds { get; set; } = [];
}

public class TransferResponse
{
    public long TransferId
    {
        get; set;
    }
    public string TransferNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public long FromLocationId
    {
        get; set;
    }
    public long ToLocationId
    {
        get; set;
    }
    public string FromLocation { get; set; } = "";
    public string ToLocation { get; set; } = "";
    public DateTime RequestedAt
    {
        get; set;
    }
    public string? Remarks
    {
        get; set;
    }
    public List<TransferLine> Items { get; set; } = [];
    public object? Operations
    {
        get; set;
    }
}

public class TransferLine
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string AttributesJson { get; set; } = "{}";
    public string AttributeSummary => StockFlow.Application.Common.VariantIdentity.Summary(AttributesJson);
    public long TransferItemId
    {
        get; set;
    }
    public long VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public bool IsSerialTracked
    {
        get; set;
    }
    public int RequestedQuantity
    {
        get; set;
    }
    public int DispatchedQuantity
    {
        get; set;
    }
    public int ReceivedQuantity
    {
        get; set;
    }
    public int DamagedQuantity
    {
        get; set;
    }
    public int LostQuantity
    {
        get; set;
    }
    public List<TransferUnit> Units { get; set; } = [];
}

public class TransferUnit
{
    public long TransferItemId
    {
        get; set;
    }
    public long ProductUnitId
    {
        get; set;
    }
    public string? SerialNumber
    {
        get; set;
    }
    public string Resolution { get; set; } = "";
    public string?[] Barcodes { get; set; } = [];
}
