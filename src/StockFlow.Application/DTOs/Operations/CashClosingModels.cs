namespace StockFlow.Application.DTOs.Operations;

public class CashClosingRequest
{
    public Guid RequestId
    {
        get; set;
    }
    public long? LocationId
    {
        get; set;
    }
    public DateTime Date
    {
        get; set;
    }
    public int ExpectedVersion
    {
        get; set;
    }
    public string Fingerprint { get; set; } = "";
    public decimal OpeningCash
    {
        get; set;
    }
    public decimal CashAdded
    {
        get; set;
    }
    public decimal CashRemoved
    {
        get; set;
    }
    public decimal CountedCash
    {
        get; set;
    }
    public string Reason { get; set; } = "";
}

public class CashTotals
{
    public long SalesCount { get; set; }
    public long ServiceCount { get; set; }
    public long RefundCount { get; set; }
    public long SupplierCount { get; set; }
    public long ExpenseCount { get; set; }
    public decimal ExpenseCash { get; set; }
    public decimal SalesCash
    {
        get; set;
    }
    public decimal ServiceCash
    {
        get; set;
    }
    public decimal CashRefunds
    {
        get; set;
    }
    public decimal SupplierCash
    {
        get; set;
    }
    public long MovementCount
    {
        get; set;
    }
    public string Fingerprint { get; set; } = "";
    public decimal NetCash => SalesCash + ServiceCash - CashRefunds - SupplierCash - ExpenseCash;
}

public class CashClosingRecord : CashClosingRequest
{
    public long ClosingId
    {
        get; set;
    }
    public int Version
    {
        get; set;
    }
    public CashTotals Totals { get; set; } = new();
    public decimal ExpectedCash
    {
        get; set;
    }
    public decimal Difference
    {
        get; set;
    }
    public int ClosedBy
    {
        get; set;
    }
    public DateTimeOffset ClosedAt
    {
        get; set;
    }
}

public class CashDayReport
{
    public DateTime Date
    {
        get; set;
    }
    public string Location { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public CashTotals Totals { get; set; } = new();
    public List<CashClosingRecord> Closings { get; set; } = [];
    public bool ChangedSinceClosing => Closings.Count > 0 && Closings[0].Fingerprint != Totals.Fingerprint;
}
