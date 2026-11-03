namespace StockFlow.Application.DTOs.Reports;

public class ActivityReport
{
    public DateTime From
    {
        get; set;
    }
    public DateTime To
    {
        get; set;
    }
    public string Scope { get; set; } = "";
    public bool CanSeeStock
    {
        get; set;
    }
    public bool CanSeeSales
    {
        get; set;
    }
    public List<StockPeriodRow> Stock { get; set; } = [];
    public List<FinancialPeriodRow> Financial { get; set; } = [];
    public List<CreditPeriodRow> Credit { get; set; } = [];
    public List<FinancialPeriodRow> PreviousFinancial { get; set; } = [];
    public List<ShopTrendRow> Trend { get; set; } = [];
    public List<ShopProductRow> TopProducts { get; set; } = [];
    public List<ShopStockRow> CurrentStock { get; set; } = [];
    public List<ShopActivityRow> Recent { get; set; } = [];
}

public sealed record ShopTrendRow(DateTime Day, decimal Sales, decimal Collections);
public sealed record ShopProductRow(string ProductName, long Quantity, decimal Amount);
public sealed record ShopStockRow(string ProductName, long CurrentStock, int MinimumStock);
public sealed record ShopActivityRow(string Type, string Reference, long Quantity, decimal? Amount, DateTime Date, long Id);

public class StockPeriodRow
{
    public long LocationId
    {
        get; set;
    }
    public long VariantId
    {
        get; set;
    }
    public string StoreName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long Opening
    {
        get; set;
    }
    public long OpeningAdded
    {
        get; set;
    }
    public long Receipts
    {
        get; set;
    }
    public long Sales
    {
        get; set;
    }
    public long AdjustmentIn
    {
        get; set;
    }
    public long AdjustmentOut
    {
        get; set;
    }
    public long TransferIn
    {
        get; set;
    }
    public long TransferOut
    {
        get; set;
    }
    public long TransferLoss
    {
        get; set;
    }
    public long OtherIn
    {
        get; set;
    }
    public long OtherOut
    {
        get; set;
    }
    public long Closing
    {
        get; set;
    }
    public long InvalidMovements
    {
        get; set;
    }
    public long CalculatedClosing => Opening + OpeningAdded + Receipts + AdjustmentIn + TransferIn + OtherIn - Sales - AdjustmentOut - TransferOut - TransferLoss - OtherOut;
    public bool Reconciles => CalculatedClosing == Closing && InvalidMovements == 0 && Closing >= 0;
}

public class FinancialPeriodRow
{
    public long LocationId
    {
        get; set;
    }
    public string StoreName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public long InvoiceCount
    {
        get; set;
    }
    public long InvoiceQuantity
    {
        get; set;
    }
    public long LedgerSalesQuantity
    {
        get; set;
    }
    public long NonStockQuantity { get; set; }
    public decimal SalesValue
    {
        get; set;
    }
    public decimal Collections
    {
        get; set;
    }
    public decimal CreditOutstanding
    {
        get; set;
    }
    public decimal InvoiceCollected
    {
        get; set;
    }
    public decimal InvoiceCredit
    {
        get; set;
    }
    public decimal InvoiceItemValue
    {
        get; set;
    }
    public bool Reconciles => SalesValue == InvoiceItemValue && InvoiceQuantity - NonStockQuantity == LedgerSalesQuantity;
}

public class CreditPeriodRow
{
    public long SaleId
    {
        get; set;
    }
    public string InvoiceNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public DateTime SaleDate
    {
        get; set;
    }
    public decimal Outstanding
    {
        get; set;
    }
}
