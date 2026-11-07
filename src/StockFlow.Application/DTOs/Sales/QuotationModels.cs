using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace StockFlow.Application.DTOs.Sales;

public sealed class SaveQuotationRequest
{
    public Guid QuotationId { get; set; }
    public Guid OperationId { get; set; }
    public long ExpectedVersion { get; set; }
    public long? StoreId { get; set; }
    public DateTime DocumentDate { get; set; }
    public DateTime ValidUntil { get; set; }
    [StringLength(150)] public string? Reference { get; set; }
    [StringLength(4000)] public string? Terms { get; set; }
    public CreateSaleRequest Request { get; set; } = new();
}

public sealed record QuotationStateRequest(Guid OperationId, long ExpectedVersion, string Status, string Notes);
public sealed record QuotationCheckoutRequest(long ExpectedVersion, int Revision);
public sealed class QuotationRow
{
    public Guid QuotationId { get; set; }
    public string QuotationNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerMobile { get; set; }
    public string Status { get; set; } = "";
    public DateTime DocumentDate { get; set; }
    public DateTime ValidUntil { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public int CurrentRevision { get; set; }
    public long Version { get; set; }
    public long? SaleId { get; set; }
}
public sealed record QuotationPage(IReadOnlyList<QuotationRow> Rows, long Total, int Page, int PageSize);
public sealed record QuotationRevision(int Revision, JsonElement Body, string PreparedBy, DateTimeOffset CreatedAt);
public sealed class QuotationEvent
{
    public long EventId {get;set;}
    public int Revision {get;set;}
    public string Action {get;set;}="";
    public string Notes {get;set;}="";
    public string Actor {get;set;}="";
    public DateTime CreatedAt {get;set;}
}
public sealed record QuotationDetail(QuotationRow Header, QuotationRevision Selected, IReadOnlyList<int> Revisions, IReadOnlyList<QuotationEvent> History);
