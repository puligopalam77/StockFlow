using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Crm;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class CrmRepository(DbConnectionFactory factory,ISalesRepository sales,IOptions<ShopTimeOptions> time,TimeProvider? clock=null) : ICrmRepository
{
    private readonly TimeProvider clock=clock??TimeProvider.System;
    private static JsonElement Json(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    private async Task Require(NpgsqlConnection c,string action,NpgsqlTransaction? tx=null)
    {
        factory.RequireLocation();
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_crm_allowed(@action)",new{action},tx))throw new UnauthorizedAccessException("CRM permission required.");
    }
    private static async Task<bool> Allowed(NpgsqlConnection c,string action,NpgsqlTransaction? tx=null)=>await c.ExecuteScalarAsync<bool>("SELECT public.sf_crm_allowed(@action)",new{action},tx);
    private static string Literal(string? term)=>"%"+(term??"").Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
    private const string CustomerRows="""
        SELECT c.customer_id AS "customerId",c.customer_name AS "customerName",c.mobile_number AS "mobileNumber",c.email AS "email",c.address AS "address",c.gst_number AS "gstNumber",c.is_active AS "isActive",
          cc.customer_type AS "customerType",cc.preferred_method AS "preferredMethod",cc.preferences,cc.version,
          cc.created_at AS "linkedAt",c.created_at AS "createdAt",
          (SELECT count(*) FROM sales s WHERE s.customer_id=c.customer_id AND s.store_id=sf_store() AND s.business_profile_code=sf_visible_business_profile() AND s.status='COMPLETED') AS "purchaseCount",
          (SELECT max(s.sale_date AT TIME ZONE current_setting('TimeZone')) FROM sales s WHERE s.customer_id=c.customer_id AND s.store_id=sf_store() AND s.business_profile_code=sf_visible_business_profile() AND s.status='COMPLETED') AS "lastPurchase",
          CASE WHEN sf_crm_allowed('VIEW_FINANCIAL') THEN (SELECT coalesce(sum(s.total_amount-coalesce((SELECT sum(r.return_total) FROM sale_returns r WHERE r.sale_id=s.sale_id),0)),0) FROM sales s WHERE s.customer_id=c.customer_id AND s.store_id=sf_store() AND s.business_profile_code=sf_visible_business_profile() AND s.status='COMPLETED') END AS "netPurchases",
          (SELECT count(*) FROM crm_enquiries e WHERE e.customer_id=c.customer_id AND e.status NOT IN('RESOLVED','CLOSED','CANCELLED')) AS "openEnquiries",
          (SELECT min(next_follow_up) FROM crm_enquiries e WHERE e.customer_id=c.customer_id AND e.status NOT IN('RESOLVED','CLOSED','CANCELLED')) AS "nextFollowUp"
        FROM crm_customers cc JOIN customers c USING(tenant_id,customer_id)
        """;
    public async Task<JsonElement> CustomersAsync(CrmQuery q)
    {
        q.Validate();await using var c=factory.CreateConnection();await Require(c,"VIEW");
        if(q.Status is not null||q.Priority is not null||q.Type is not null&&!new[]{"INDIVIDUAL","BUSINESS"}.Contains(q.Type))throw new ValidationException("Choose valid customer filters.");
        var result=await c.ExecuteScalarAsync<string>("""
            WITH selected AS (
            """+CustomerRows+"""
             WHERE (@term IS NULL OR c.customer_name ILIKE @pattern OR c.mobile_number ILIKE @pattern OR c.email ILIKE @pattern OR c.customer_id::text=@term)
             AND (@type IS NULL OR cc.customer_type=@type) AND (@active IS NULL OR c.is_active=@active)
            ), filtered AS (SELECT * FROM selected WHERE (@days IS NULL OR "lastPurchase" IS NULL OR "lastPurchase"<@now-make_interval(days=>@days))
             AND (@due IS NULL OR (@due='OVERDUE' AND "nextFollowUp"<@now) OR (@due='UPCOMING' AND "nextFollowUp">=@end) OR (@due='TODAY' AND "nextFollowUp">=@start AND "nextFollowUp"<@end)))
            , page AS (SELECT * FROM filtered ORDER BY "customerName","customerId" LIMIT @limit OFFSET @offset)
            SELECT jsonb_build_object('rows',coalesce((SELECT jsonb_agg(to_jsonb(page)) FROM page),'[]'),'total',(SELECT count(*) FROM filtered))::text
            """,new{term=q.Term,pattern=Literal(q.Term),type=q.Type,active=q.Active,days=q.LastPurchaseDays,due=q.Due,now=clock.GetUtcNow(),start=Today().Start,end=Today().End,limit=q.PageSize,offset=(q.Page-1)*q.PageSize});
        return Json(result!);
    }
    public async Task<JsonElement?> CustomerAsync(int id,int page,int pageSize)
    {
        new CrmQuery{Page=page,PageSize=pageSize}.Validate();await using var c=factory.CreateConnection();await Require(c,"VIEW");
        var customer=await c.ExecuteScalarAsync<string>("SELECT to_jsonb(x)::text FROM ("+CustomerRows+" WHERE c.customer_id=@id) x",new{id});
        if(customer is null)return null;
        var notes=await c.ExecuteScalarAsync<string>("SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n.note_id DESC),'[]')::text FROM (SELECT note_id,notes,created_at,created_by FROM crm_customer_notes WHERE customer_id=@id ORDER BY note_id DESC LIMIT 100) n",new{id});
        var enquiries=await EnquiriesAsync(new(){CustomerId=id,Page=1,PageSize=100});
        var activities=await c.ExecuteScalarAsync<string>("SELECT coalesce(jsonb_agg(to_jsonb(a) ORDER BY a.activity_id DESC),'[]')::text FROM (SELECT a.* FROM crm_activities a JOIN crm_enquiries e USING(tenant_id,enquiry_id) WHERE e.customer_id=@id ORDER BY a.activity_id DESC LIMIT 100) a",new{id});
        object? history=null;
        var financial=await Allowed(c,"VIEW_FINANCIAL");
        var historyAllowed=await Allowed(c,"VIEW_HISTORY");object? financialSummary=null;
        if(historyAllowed||financial)
        {
            var h=await sales.GetListAsync(null,id,(page-1)*pageSize,pageSize,null,null,"COMPLETED");
            // Existing sales service supplies current settlement/return facts; never recompute dues from invoice minus one payment column.
            financialSummary=financial?h.Summary:null;
            if(historyAllowed)history=financial?(object)h:new{rows=h.Rows.Select(r=>new{r.SaleId,r.InvoiceNumber,r.SaleDate,r.InvoiceStatus,r.ItemCount}),total=h.Summary.Count};
        }
        return Json(JsonSerializer.Serialize(new{customer=Json(customer),notes=Json(notes!),enquiries,activities=Json(activities!),history,financial,financialSummary,timeZoneId=time.Value.TimeZoneId},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
    }
    public async Task LinkCustomerAsync(int id)
    {
        await using var c=factory.CreateConnection();await Require(c,"CREATE");
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM customers WHERE customer_id=@id)",new{id}))throw new ValidationException("Select an existing customer authorized for this shop setup.");
        await c.ExecuteAsync("INSERT INTO crm_customers(customer_id) VALUES(@id) ON CONFLICT DO NOTHING",new{id});
    }
    public async Task NoteAsync(int id,CrmNoteRequest r)
    {
        if(r.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(r.Notes)||r.Notes.Trim().Length<3||r.Notes.Length>2000)throw new ValidationException("Enter a note between 3 and 2,000 characters.");
        await using var c=factory.CreateConnection();await Require(c,"EDIT");
        await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('crm-note:'||sf_tenant()::text||@request::text,0))",new{request=r.RequestId},tx);
        var old=await c.QuerySingleOrDefaultAsync<NoteReplay>("SELECT customer_id AS CustomerId,notes FROM crm_customer_notes WHERE request_id=@RequestId",r,tx);
        if(old is not null){if(old.CustomerId!=id||old.Notes!=r.Notes.Trim())throw new ValidationException("This request already belongs to a different note.");return;}
        await c.ExecuteAsync("INSERT INTO crm_customer_notes(customer_id,notes,request_id) VALUES(@id,@notes,@request)",new{id,notes=r.Notes.Trim(),request=r.RequestId},tx);await tx.CommitAsync();
    }
    public async Task PreferencesAsync(int id,CrmPreferencesRequest r)
    {
        if(!new[]{"INDIVIDUAL","BUSINESS"}.Contains(r.CustomerType)||r.PreferredMethod is not null&&!CrmRules.Methods.Contains(r.PreferredMethod)||r.Preferences?.Length>2000)throw new ValidationException("Check customer type and contact preferences.");
        await using var c=factory.CreateConnection();await Require(c,"EDIT");
        if(await c.ExecuteAsync("UPDATE crm_customers SET customer_type=@CustomerType,preferred_method=@PreferredMethod,preferences=@Preferences,version=version+1 WHERE customer_id=@id AND version=@Version",new{id,r.CustomerType,r.PreferredMethod,r.Preferences,r.Version})!=1)throw new ValidationException("Customer preferences changed. Reload before editing.");
    }
    private sealed record NoteReplay(int CustomerId,string Notes);
    private const string EnquiryRows="""
        SELECT e.enquiry_id AS "enquiryId",'ENQ-'||e.enquiry_id AS "enquiryNumber",e.customer_id AS "customerId",c.customer_name AS "customerName",c.mobile_number AS "mobileNumber",
        e.enquiry_type AS "enquiryType",e.subject,e.description,e.product_id AS "productId",e.variant_id AS "variantId",e.requested_quantity AS "requestedQuantity",e.priority,e.assigned_to AS "assignedTo",u.full_name AS "assignedName",e.status,e.next_follow_up AS "nextFollowUp",e.contact_method AS "contactMethod",e.updated_at AS "updatedAt",e.version,
        p.product_name AS "productName",v.variant_name AS "variantName",v.sku,
        l.sale_id AS "saleId",l.converted_at AS "convertedAt",
        (SELECT coalesce(sum(cs.current_stock),0) FROM vw_location_current_stock cs WHERE cs.store_id=sf_store() AND cs.location_id=sf_location() AND cs.variant_id=e.variant_id) AS "availableStock"
        FROM crm_enquiries e JOIN customers c USING(tenant_id,customer_id) JOIN users u ON u.tenant_id=e.tenant_id AND u.user_id=e.assigned_to
        LEFT JOIN products p ON p.tenant_id=e.tenant_id AND p.product_id=e.product_id LEFT JOIN product_variants v ON v.tenant_id=e.tenant_id AND v.variant_id=e.variant_id
        LEFT JOIN crm_sale_links l ON l.tenant_id=e.tenant_id AND l.enquiry_id=e.enquiry_id
        """;
    private (DateTimeOffset Start,DateTimeOffset End) Today()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(time.Value.TimeZoneId);var day=time.Value.Today(clock);
        return (new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day,DateTimeKind.Unspecified),zone)),new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.AddDays(1),DateTimeKind.Unspecified),zone)));
    }
    public async Task<JsonElement> EnquiriesAsync(CrmQuery q)
    {
        q.Validate();if(q.Type is not null&&!CrmRules.Types.Contains(q.Type))throw new ValidationException("Choose a valid enquiry type.");await using var c=factory.CreateConnection();await Require(c,"VIEW");var day=Today();
        var range=q.From.HasValue?ShopDateRange.Resolve(new("range",q.From,q.To),time.Value,clock):null;
        var order=q.Sort switch{"due"=>"\"nextFollowUp\" ASC NULLS LAST","customer"=>"\"customerName\" ASC","priority"=>"CASE priority WHEN 'HIGH' THEN 0 WHEN 'NORMAL' THEN 1 ELSE 2 END",_=>"\"updatedAt\" DESC"};
        var result=await c.ExecuteScalarAsync<string>("WITH selected AS ("+EnquiryRows+"""
         WHERE (@term IS NULL OR e.subject ILIKE @pattern OR c.customer_name ILIKE @pattern OR c.mobile_number ILIKE @pattern OR p.product_name ILIKE @pattern OR ('ENQ-'||e.enquiry_id) ILIKE @pattern)
         AND (@status IS NULL OR e.status=@status) AND (@priority IS NULL OR e.priority=@priority) AND (@assigned IS NULL OR e.assigned_to=@assigned) AND (@customer IS NULL OR e.customer_id=@customer) AND (@type IS NULL OR e.enquiry_type=@type)
         AND (@product IS NULL OR e.product_id=@product) AND (@from::timestamptz IS NULL OR e.created_at>=@from AND e.created_at<@to)
         AND (@due IS NULL OR (@due='COMPLETED' AND e.status IN('RESOLVED','CLOSED','CANCELLED')) OR (e.status NOT IN('RESOLVED','CLOSED','CANCELLED') AND
          ((@due='TODAY' AND e.next_follow_up>=@start AND e.next_follow_up<@end) OR (@due='OVERDUE' AND e.next_follow_up<@now) OR (@due='UPCOMING' AND e.next_follow_up>=@end))))
        ), page AS (SELECT * FROM selected ORDER BY
        """+order+"""
         ,"enquiryId" DESC LIMIT @limit OFFSET @offset)
        SELECT jsonb_build_object('rows',coalesce((SELECT jsonb_agg(to_jsonb(page)) FROM page),'[]'),'total',(SELECT count(*) FROM selected),'timeZoneId',@zone)::text
        """,new{term=q.Term,pattern=Literal(q.Term),status=q.Status,priority=q.Priority,assigned=q.AssignedTo,customer=q.CustomerId,type=q.Type,product=q.ProductId,from=range?.StartUtc,to=range?.EndUtc,due=q.Due,start=day.Start,end=day.End,now=clock.GetUtcNow(),zone=time.Value.TimeZoneId,limit=q.PageSize,offset=(q.Page-1)*q.PageSize});return Json(result!);
    }
    public async Task<JsonElement?> EnquiryAsync(long id)
    {
        await using var c=factory.CreateConnection();await Require(c,"VIEW");
        var e=await c.ExecuteScalarAsync<string>("SELECT to_jsonb(x)::text FROM ("+EnquiryRows+" WHERE e.enquiry_id=@id) x",new{id});if(e is null)return null;
        var a=await c.ExecuteScalarAsync<string>("SELECT coalesce(jsonb_agg(to_jsonb(a) ORDER BY a.activity_id),'[]')::text FROM (SELECT a.*,u.full_name AS actor_name FROM crm_activities a JOIN users u ON u.tenant_id=a.tenant_id AND u.user_id=a.actor_id WHERE enquiry_id=@id) a",new{id});
        return Json(JsonSerializer.Serialize(new{enquiry=Json(e),activities=Json(a!),timeZoneId=time.Value.TimeZoneId}));
    }
    private static async Task Lock(NpgsqlConnection c,NpgsqlTransaction tx,Guid request)=>await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('crm:'||sf_tenant()::text,0))",transaction:tx);
    private static async Task<long?> Replay(NpgsqlConnection c,NpgsqlTransaction tx,Guid request,string payload,long? id=null)
    {
        var row=await c.QuerySingleOrDefaultAsync<ReplayRow>("SELECT enquiry_id AS Id,payload::text AS Payload FROM crm_activities WHERE request_id=@request",new{request},tx);
        if(row is null)return null;
        if(id.HasValue&&row.Id!=id||!JsonElement.DeepEquals(Json(row.Payload),Json(payload)))throw new ValidationException("This request was already used with different enquiry details.");
        return row.Id;
    }
    private sealed record ReplayRow(long Id,string Payload);
    public async Task<long> SaveAsync(long? id,CrmEnquiryRequest r)
    {
        CrmRules.Validate(r);await using var c=factory.CreateConnection();await Require(c,id.HasValue?"EDIT":"CREATE");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c,tx);await Lock(c,tx,r.RequestId);var payload=JsonSerializer.Serialize(r);
        if(await Replay(c,tx,r.RequestId,payload,id) is {} replay)return replay;
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM crm_customers cc JOIN customers c USING(tenant_id,customer_id) WHERE cc.customer_id=@CustomerId AND (@editing OR c.is_active))",new{r.CustomerId,editing=id.HasValue},tx))throw new ValidationException("Link an active existing customer to this CRM shop first.");
        if(r.NextFollowUp.HasValue)await CheckContact(c,tx,r.CustomerId,r.ContactMethod);
        if(id.HasValue)
        {
            var old=await c.QuerySingleOrDefaultAsync<EnquiryState>("SELECT version,status,customer_id AS CustomerId FROM crm_enquiries WHERE enquiry_id=@id FOR UPDATE",new{id},tx)??throw new ValidationException("Enquiry is unavailable.");
            if(old.Version!=r.Version||old.CustomerId!=r.CustomerId)throw new ValidationException("Enquiry changed. Reload before editing; its customer cannot be replaced.");
            CrmRules.Transition(old.Status,r.Status,await Allowed(c,"CLOSE",tx));
            await c.ExecuteAsync("""
                UPDATE crm_enquiries SET subject=@Subject,description=@Description,product_id=@ProductId,variant_id=@VariantId,requested_quantity=@RequestedQuantity,priority=@Priority,assigned_to=@AssignedTo,status=@Status,next_follow_up=@NextFollowUp,contact_method=@ContactMethod WHERE enquiry_id=@id
                """,new{id,r.Subject,r.Description,r.ProductId,r.VariantId,r.RequestedQuantity,r.Priority,r.AssignedTo,r.Status,r.NextFollowUp,r.ContactMethod},tx);
        }
        else
        {
            if(r.Status!="NEW")throw new ValidationException("New enquiries start with New status.");
            id=await c.ExecuteScalarAsync<long>("""
                INSERT INTO crm_enquiries(customer_id,enquiry_type,subject,description,product_id,variant_id,requested_quantity,priority,assigned_to,status,next_follow_up,contact_method,request_id,request_payload)
                VALUES(@CustomerId,@EnquiryType,@Subject,@Description,@ProductId,@VariantId,@RequestedQuantity,@Priority,@AssignedTo,@Status,@NextFollowUp,@ContactMethod,@RequestId,CAST(@payload AS jsonb)) RETURNING enquiry_id
                """,new{r.CustomerId,r.EnquiryType,r.Subject,r.Description,r.ProductId,r.VariantId,r.RequestedQuantity,r.Priority,r.AssignedTo,r.Status,r.NextFollowUp,r.ContactMethod,r.RequestId,payload},tx);
        }
        await c.ExecuteAsync("INSERT INTO crm_activities(enquiry_id,kind,status,request_id,payload,notes,next_follow_up) VALUES(@id,@kind,@Status,@RequestId,CAST(@payload AS jsonb),@Description,@NextFollowUp)",new{id,kind=r.Version==0?"CREATED":"EDITED",r.Status,r.RequestId,payload,r.Description,r.NextFollowUp},tx);
        await tx.CommitAsync();return id.Value;
    }
    private sealed record EnquiryState(int Version,string Status,int CustomerId);
    private static async Task CheckContact(NpgsqlConnection c,NpgsqlTransaction tx,int customer,string method)
    {
        var contact=await c.QuerySingleOrDefaultAsync<Contact>("SELECT mobile_number AS Mobile,email FROM customers WHERE customer_id=@customer",new{customer},tx);
        var valid=contact is not null&&(method is "IN_PERSON" or "OTHER" || method is "CALL" or "WHATSAPP"&&System.Text.RegularExpressions.Regex.IsMatch(contact.Mobile?.Trim()??"",FieldPatterns.Phone)
            ||method=="EMAIL"&&System.Net.Mail.MailAddress.TryCreate(contact.Email,out var email)&&email.Address==contact.Email);
        if(!valid)throw new ValidationException("Add a usable contact for this follow-up method, or choose In Person/Other.");
    }
    private sealed record Contact(string? Mobile,string? Email);
    public async Task FollowUpAsync(long id,CrmFollowUpRequest r)
    {
        if(r.RequestId==Guid.Empty||!CrmRules.Methods.Contains(r.Method)||!new[]{"CONTACTED","NO_ANSWER","INTERESTED","NOT_INTERESTED","CALLBACK","AWAITING_STOCK","RESOLVED"}.Contains(r.Outcome)||r.Notes.Length>2000||r.OccurredAt>clock.GetUtcNow().AddMinutes(5)||r.OccurredAt<new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero))throw new ValidationException("Check follow-up date, method, outcome and notes.");
        if(r.NextFollowUp.HasValue&&r.NextFollowUp<=r.OccurredAt)throw new ValidationException("Next follow-up must be after this activity.");
        if(CrmRules.Terminal(r.Status)&&r.NextFollowUp.HasValue)throw new ValidationException("Clear the next follow-up when completing an enquiry.");
        await using var c=factory.CreateConnection();await Require(c,"FOLLOW_UP");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,tx);await Lock(c,tx,r.RequestId);
        var payload=JsonSerializer.Serialize(r);if(await Replay(c,tx,r.RequestId,payload,id) is not null)return;
        var old=await c.QuerySingleOrDefaultAsync<EnquiryState>("SELECT version,status,customer_id AS CustomerId FROM crm_enquiries WHERE enquiry_id=@id FOR UPDATE",new{id},tx)??throw new ValidationException("Enquiry is unavailable.");
        if(old.Version!=r.Version)throw new ValidationException("Enquiry changed. Reload before adding a follow-up.");
        if(CrmRules.Terminal(old.Status))throw new ValidationException("Reopen this enquiry before adding a follow-up.");
        CrmRules.Transition(old.Status,r.Status,await Allowed(c,"CLOSE",tx));await CheckContact(c,tx,old.CustomerId,r.Method);
        await c.ExecuteAsync("UPDATE crm_enquiries SET status=@Status,next_follow_up=@NextFollowUp,contact_method=@Method WHERE enquiry_id=@id",new{id,r.Status,r.NextFollowUp,r.Method},tx);
        await c.ExecuteAsync("INSERT INTO crm_activities(enquiry_id,kind,occurred_at,method,outcome,notes,next_follow_up,status,request_id,payload) VALUES(@id,'FOLLOW_UP',@OccurredAt,@Method,@Outcome,@Notes,@NextFollowUp,@Status,@RequestId,CAST(@payload AS jsonb))",new{id,r.OccurredAt,r.Method,r.Outcome,r.Notes,r.NextFollowUp,r.Status,r.RequestId,payload},tx);await tx.CommitAsync();
    }
    public async Task<JsonElement> OptionsAsync(string? term)
    {
        if(term?.Length>200)throw new ValidationException("Search is limited to 200 characters.");await using var c=factory.CreateConnection();await Require(c,"VIEW");
        var result=await c.ExecuteScalarAsync<string>("""
            SELECT jsonb_build_object('currentUserId',sf_actor(),'staff',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',user_id,'name',full_name)),'[]') FROM users WHERE sf_crm_staff(user_id)),
            'customers',(SELECT coalesce(jsonb_agg(to_jsonb(x)),'[]') FROM (SELECT c.customer_id AS id,c.customer_name AS name,c.mobile_number AS mobile FROM crm_customers cc JOIN customers c USING(tenant_id,customer_id) WHERE c.is_active AND (@term IS NULL OR c.customer_name ILIKE @pattern OR c.mobile_number ILIKE @pattern) ORDER BY c.customer_name LIMIT 30) x),
            'products',(SELECT coalesce(jsonb_agg(to_jsonb(x)),'[]') FROM (SELECT p.product_id AS "productId",v.variant_id AS "variantId",p.product_name AS name,v.variant_name AS variant,v.sku,
              (SELECT coalesce(sum(cs.current_stock),0) FROM vw_location_current_stock cs WHERE cs.variant_id=v.variant_id AND cs.store_id=sf_store() AND cs.location_id=sf_location()) AS stock
              ,CASE WHEN sf_gst_allowed('VIEW','SALES') THEN v.selling_price END AS price
              FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.is_active AND v.is_active AND p.identifier_profile=sf_visible_business_profile() AND @term IS NOT NULL AND (p.product_name ILIKE @pattern OR v.sku ILIKE @pattern) ORDER BY p.product_name,v.variant_id LIMIT 30) x))::text
            """,new{term=string.IsNullOrWhiteSpace(term)?null:term.Trim(),pattern=Literal(term?.Trim())});return Json(result!);
    }
    public async Task<JsonElement> DashboardAsync()
    {
        await using var c=factory.CreateConnection();await Require(c,"VIEW");var day=Today();var zone=TimeZoneInfo.FindSystemTimeZoneById(time.Value.TimeZoneId);var today=time.Value.Today(clock);var month=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(new(today.Year,today.Month,1),DateTimeKind.Unspecified),zone);
        var result=await c.ExecuteScalarAsync<string>("""
            SELECT jsonb_build_object('totalCustomers',(SELECT count(*) FROM crm_customers),
            'newCustomersThisMonth',(SELECT count(*) FROM crm_customers cc JOIN customers c USING(tenant_id,customer_id) WHERE c.created_at>=@month AND c.created_at<@end),
            'repeatCustomers',(SELECT count(*) FROM (SELECT s.customer_id FROM sales s JOIN crm_customers cc USING(tenant_id,customer_id,business_profile_code,store_id) WHERE s.status='COMPLETED' AND s.store_id=sf_store() GROUP BY s.customer_id HAVING count(*)>1) x),
            'openEnquiries',(SELECT count(*) FROM crm_enquiries WHERE status NOT IN('RESOLVED','CLOSED','CANCELLED')),
            'dueToday',(SELECT count(*) FROM crm_enquiries WHERE status NOT IN('RESOLVED','CLOSED','CANCELLED') AND next_follow_up>=@start AND next_follow_up<@end),
            'overdue',(SELECT count(*) FROM crm_enquiries WHERE status NOT IN('RESOLVED','CLOSED','CANCELLED') AND next_follow_up<@now),
            'closedEnquiries',(SELECT count(*) FROM crm_enquiries WHERE status IN('RESOLVED','CLOSED','CANCELLED')),
            'converted',(SELECT count(*) FROM crm_sale_links l JOIN sales s USING(tenant_id,sale_id) WHERE s.status='COMPLETED'),
            'timeZoneId',@zone,'today',@today)::text
            """,new{month,start=day.Start,end=day.End,now=clock.GetUtcNow(),zone=time.Value.TimeZoneId,today=today.ToString("yyyy-MM-dd")});return Json(result!);
    }
}
