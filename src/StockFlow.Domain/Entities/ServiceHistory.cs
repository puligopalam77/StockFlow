namespace StockFlow.Domain.Entities;

public class ServiceHistory
{
    public long ServiceHistoryId
    {
        get; set;
    }
    public long ServiceRequestId
    {
        get; set;
    }
    public string? OldStatus
    {
        get; set;
    }
    public string NewStatus { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string? Remarks
    {
        get; set;
    }
    public string? Details
    {
        get; set;
    }
    public int ChangedBy
    {
        get; set;
    }
    public string ChangedByName { get; set; } = "";
    public DateTime ChangedAt
    {
        get; set;
    }
}
