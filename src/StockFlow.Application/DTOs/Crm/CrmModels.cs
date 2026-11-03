using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Crm;

public sealed class CrmQuery
{
    public string? Term { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? Due { get; set; }
    public string? Type { get; set; }
    public int? AssignedTo { get; set; }
    public int? CustomerId { get; set; }
    public bool? Active { get; set; }
    public int? LastPurchaseDays { get; set; }
    public int? ProductId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Sort { get; set; } = "updated";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public void Validate()
    {
        if(Page<1 || Page>100000 || !new[]{10,25,50,100}.Contains(PageSize) || Term?.Length>200)
            throw new ValidationException("Choose a valid page, rows-per-page and search value.");
        if(Status is not null && !CrmRules.Statuses.Contains(Status) || Priority is not null && !CrmRules.Priorities.Contains(Priority)
            || Due is not null && !new[]{"TODAY","OVERDUE","UPCOMING","COMPLETED"}.Contains(Due)
            || Type is not null && !CrmRules.Types.Concat(new[]{"INDIVIDUAL","BUSINESS"}).Contains(Type) || AssignedTo<=0 || CustomerId<=0)
            throw new ValidationException("Choose valid CRM filters.");
        Term=string.IsNullOrWhiteSpace(Term)?null:Term.Trim();
        if(ProductId<=0||LastPurchaseDays is <1 or >3650||From.HasValue!=To.HasValue||From>To||From.HasValue&&(To!.Value-From.Value).TotalDays>366
           ||!new[]{"updated","due","customer","priority"}.Contains(Sort))throw new ValidationException("Check the CRM date range, product and sorting filters.");
    }
}
public sealed class CrmEnquiryRequest
{
    public Guid RequestId { get; set; }
    public int Version { get; set; }
    public int CustomerId { get; set; }
    public string EnquiryType { get; set; } = "GENERAL";
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    public int? ProductId { get; set; }
    public long? VariantId { get; set; }
    public decimal? RequestedQuantity { get; set; }
    public string Priority { get; set; } = "NORMAL";
    public int AssignedTo { get; set; }
    public string Status { get; set; } = "NEW";
    public DateTimeOffset? NextFollowUp { get; set; }
    public string ContactMethod { get; set; } = "CALL";
}
public sealed class CrmFollowUpRequest
{
    public Guid RequestId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Method { get; set; } = "CALL";
    public string Outcome { get; set; } = "CONTACTED";
    public string Notes { get; set; } = "";
    public DateTimeOffset? NextFollowUp { get; set; }
    public string Status { get; set; } = "IN_PROGRESS";
}
public sealed record CrmNoteRequest(Guid RequestId,string Notes);
public sealed record CrmPreferencesRequest(int Version,string CustomerType,string? PreferredMethod,string? Preferences);
public static class CrmRules
{
    public static readonly string[] Statuses=["NEW","IN_PROGRESS","WAITING_CUSTOMER","WAITING_STOCK","RESOLVED","CLOSED","CANCELLED"];
    public static readonly string[] Priorities=["LOW","NORMAL","HIGH"];
    public static readonly string[] Types=["AVAILABILITY","PRICE","PRODUCT_REQUEST","ORDER_FOLLOW_UP","GENERAL","OTHER"];
    public static readonly string[] Methods=["CALL","WHATSAPP","EMAIL","IN_PERSON","OTHER"];
    public static bool Terminal(string status)=>status is "RESOLVED" or "CLOSED" or "CANCELLED";
    public static void Transition(string oldStatus,string status,bool canClose)
    {
        if(!Statuses.Contains(status))throw new ValidationException("Choose a valid enquiry status.");
        if(oldStatus==status)return;
        if((Terminal(oldStatus)||Terminal(status))&&!canClose)throw new ValidationException("Close/reopen permission is required for this status change.");
        if(Terminal(oldStatus)&&status!="IN_PROGRESS")throw new ValidationException("Reopen a completed enquiry as In Progress before changing its status.");
    }
    public static void Validate(CrmEnquiryRequest r)
    {
        r.Subject=r.Subject.Trim();
        if(r.RequestId==Guid.Empty||r.CustomerId<=0||r.AssignedTo<=0||r.Subject.Length<3||r.Subject.Length>200||r.Description?.Length>2000
           ||!Types.Contains(r.EnquiryType)||!Priorities.Contains(r.Priority)||!Statuses.Contains(r.Status)||!Methods.Contains(r.ContactMethod)
           ||r.RequestedQuantity is <=0 or >10000||r.ProductId<=0||r.VariantId<=0||r.VariantId.HasValue&&!r.ProductId.HasValue)
            throw new ValidationException("Check the customer, subject, enquiry details, staff and requested quantity.");
        if(Terminal(r.Status)&&r.NextFollowUp.HasValue)throw new ValidationException("Completed enquiries cannot have a pending follow-up.");
    }
}
