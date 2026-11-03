namespace StockFlow.Application.DTOs.Operations;

public abstract class OperationRequest
{
    public long? LocationId
    {
        get; set;
    }
    public Guid RequestId
    {
        get; set;
    }
}

public class MoneyReceiptRequest : OperationRequest
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CardType { get; set; }
    public decimal Amount
    {
        get; set;
    }
    public string PaymentMode { get; set; } = "CASH";
    public string? ReferenceNumber
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
}

public class SaleReturnRequest : OperationRequest
{
    public string RefundMode { get; set; } = "CREDIT_ADJUSTMENT";
    public decimal RefundAmount
    {
        get; set;
    }
    public string Notes { get; set; } = "";
    public List<SaleReturnLine> Items { get; set; } = [];
}

public class SaleReturnLine
{
    public long SaleItemId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public string Disposition { get; set; } = "RESTOCK";
}

public class PurchaseReturnRequest : OperationRequest
{
    public string? SupplierReference
    {
        get; set;
    }
    public string Notes { get; set; } = "";
    public List<PurchaseReturnLine> Items { get; set; } = [];
}

public class PurchaseReturnLine
{
    public long PurchaseItemId
    {
        get; set;
    }
    public long? ProductUnitId
    {
        get; set;
    }
    public int Quantity
    {
        get; set;
    }
    public string Reason { get; set; } = "";
}

public class StockCountRequest : OperationRequest
{
    public string Notes { get; set; } = "";
    public List<StockCountLine> Items { get; set; } = [];
}

public class StockCountLine
{
    public int ProductId
    {
        get; set;
    }
    public long VariantId
    {
        get; set;
    }
    public int CountedQuantity
    {
        get; set;
    }
    public string? Remarks
    {
        get; set;
    }
}

public class FulfilmentRequest : OperationRequest
{
    public string FulfilmentType { get; set; } = "DELIVERY";
    public string Status { get; set; } = "SCHEDULED";
    public DateTime ScheduledAt
    {
        get; set;
    }
    public string Address { get; set; } = "";
    public string? ContactNumber
    {
        get; set;
    }
    public string? TechnicianName
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
}

public class TradeInRequest : OperationRequest
{
    public string ItemType { get; set; } = "BATTERY";
    public string? BrandModel
    {
        get; set;
    }
    public string? SerialNumber
    {
        get; set;
    }
    public decimal AllowanceAmount
    {
        get; set;
    }
    public string Status { get; set; } = "RECEIVED";
    public string? Notes
    {
        get; set;
    }
}

public class WarrantyRmaRequest : OperationRequest
{
    public string? ProviderReference
    {
        get; set;
    }
    public string Status { get; set; } = "READY_TO_SEND";
    public DateTime? DispatchedAt
    {
        get; set;
    }
    public DateTime? ReturnedAt
    {
        get; set;
    }
    public decimal ChargeAmount
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
}

public class OperationRow
{
    public string? CardType { get; set; }
    public decimal? RefundedAmount { get; set; }
    public long Id
    {
        get; set;
    }
    public DateTime Date
    {
        get; set;
    }
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount
    {
        get; set;
    }
    public string? Reference
    {
        get; set;
    }
    public string? Notes
    {
        get; set;
    }
}

public class DocumentOperations
{
    public List<SaleReturnHistoryLine> ReturnedLines { get; set; } = [];
    public decimal DocumentTotal
    {
        get; set;
    }
    public decimal ReturnedTotal
    {
        get; set;
    }
    public decimal PaidTotal
    {
        get; set;
    }
    public decimal RefundedTotal
    {
        get; set;
    }
    public decimal Outstanding => Math.Max(0, DocumentTotal - ReturnedTotal - PaidTotal + RefundedTotal);
    public List<OperationRow> Payments { get; set; } = [];
    public List<OperationRow> Returns { get; set; } = [];
    public List<OperationRow> Fulfilments { get; set; } = [];
    public List<OperationRow> TradeIns { get; set; } = [];
}

public class SaleReturnHistoryLine
{
    public long ReturnId { get; set; }
    public string ProductName { get; set; } = "";
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public string Disposition { get; set; } = "";
}

public class ProductSalesRow
{
    public string? Sku { get; set; }
    public int? ProductId
    {
        get; set;
    }
    public long? VariantId
    {
        get; set;
    }
    public string ProductName { get; set; } = "";
    public long QuantitySold
    {
        get; set;
    }
    public long QuantityReturned
    {
        get; set;
    }
    public decimal NetSales
    {
        get; set;
    }
    public decimal? EstimatedCost
    {
        get; set;
    }
    public decimal? EstimatedMargin => NetSales - EstimatedCost;
}
