using System.Text.Json;

namespace StockFlow.Application.DTOs.Gst;

public sealed class EWayParty
{
    public string Name { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string Address { get; set; } = "";
    public string State { get; set; } = "";
    public string Pin { get; set; } = "";
}
public sealed class EWayPreparation
{
    public string DocumentType { get; set; } = "INV";
    public string MovementDocumentNumber { get; set; } = "";
    public string MovementDocumentDate { get; set; } = "";
    public string MovementReason { get; set; } = "";
    public int TransactionType { get; set; } = 1;
    public EWayParty BillFrom { get; set; } = new();
    public EWayParty DispatchFrom { get; set; } = new();
    public EWayParty BillTo { get; set; } = new();
    public EWayParty ShipTo { get; set; } = new();
    public string Mode { get; set; } = "ROAD";
    public string TransporterName { get; set; } = "";
    public string TransporterId { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string TransportDocument { get; set; } = "";
    public string TransportDocumentDate { get; set; } = "";
    public int? DistanceKm { get; set; }
    public string ApplicabilityReview { get; set; } = "";
    public string GoodsEvidence { get; set; } = "";
    public List<GstLine> TransferGoods { get; set; } = [];
}
public sealed class EWaySave
{
    public Guid DocumentId { get; set; }
    public Guid OperationId { get; set; }
    public long ExpectedVersion { get; set; }
    public string SourceType { get; set; } = "GST";
    public long SourceId { get; set; }
    public long RegistrationId { get; set; }
    public EWayPreparation Preparation { get; set; } = new();
}
public sealed record EWayAction(Guid OperationId, long ExpectedVersion, string? Ebn = null, string? Evidence = null);
public sealed class EWayDocument
{
    public Guid DocumentId { get; set; }
    public long Version { get; set; }
    public string SourceType { get; set; } = "";
    public long SourceId { get; set; }
    public long RegistrationId { get; set; }
    public string SourceJson { get; set; } = "{}";
    public string BodyJson { get; set; } = "{}";
    public string Status { get; set; } = "";
    public string? ExternalEbn { get; set; }
    public string? ExternalEvidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed record EWayValidation(string RulesVersion, string Applicability, bool ProviderReady, string[] Issues, string[] Warnings);
public sealed record EWayPage(long Total, int Page, int PageSize, IReadOnlyList<EWayDocument> Rows);
public sealed record EWayRegistration(long RegistrationId, string Label);
