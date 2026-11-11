namespace StockFlow.Application.DTOs.Operations;

public sealed class CashAdjustmentRequest
{
    public Guid RequestId { get; set; }
    public long? LocationId { get; set; }
    public string? BusinessProfileCode { get; set; }
    public string Kind { get; set; } = "";
    [System.Text.Json.Serialization.JsonRequired]
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }
    public DateTime? StartDate { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? OpeningMode { get; set; }
    public Guid? ReversesId { get; set; }
}

public sealed class CashMovement
{
    public string Id { get; set; } = "";
    public DateTimeOffset Occurred { get; set; }
    public string Reference { get; set; } = "";
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal? Balance { get; set; }
    public Guid? AdjustmentId { get; set; }
    public bool CanReverse { get; set; }
    public string? SourceModule { get; set; }
    public string? SourceUrl { get; set; }
    public string? ReversesReference { get; set; }
    public string? ReversedByReference { get; set; }
    public bool DateOnly { get; set; }
}

public sealed class CashInHandReport
{
    public bool Available { get; set; }
    public DateTime Date { get; set; }
    public DateTime Today { get; set; }
    public string TimeZone { get; set; } = "";
    public DateTime? TrackingStart { get; set; }
    public DateTimeOffset? TrackingStartedAt { get; set; }
    public long PendingReviewCount { get; set; }
    public List<CashOpeningExpense> OpeningExpenses { get; set; } = [];
    public decimal OpeningCash { get; set; }
    public decimal? Balance { get; set; }
    public decimal? Received { get; set; }
    public decimal? Paid { get; set; }
    public decimal? Net => Received - Paid;
    public int Page { get; set; }
    public long Total { get; set; }
    public List<CashMovement> Movements { get; set; } = [];
    public CashClosingRecord? Closing { get; set; }
}

public sealed class CashOpeningExpense
{
    public long MovementId { get; set; }
    public string Reference { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Timing { get; set; }
    public long Version { get; set; }
}

public sealed class CashOpeningReviewRequest
{
    public Guid RequestId { get; set; }
    public long? LocationId { get; set; }
    public long MovementId { get; set; }
    public long ExpectedVersion { get; set; }
    public string Timing { get; set; } = "";
    public string Reason { get; set; } = "";
}
