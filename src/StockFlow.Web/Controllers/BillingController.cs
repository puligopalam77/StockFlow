using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Infrastructure.Data;
using StockFlow.Web.Security;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/sales/billing")]
public class BillingController : ControllerBase
{
    private readonly DbConnectionFactory factory;
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public BillingController(DbConnectionFactory factory, PermissionContext access)
    {
        this.factory = factory;
        this.access = access;
    }

    [HttpPost("drafts/{id:guid}/approval")]
    public async Task<IActionResult> RequestApproval(Guid id, CreateSaleRequest request, [FromServices] StockFlow.Application.Interfaces.ISalesService sales)
    {
        factory.RequireLocation();
        SaleRules.Validate(request, false);
        if (id == Guid.Empty || id != request.RequestId)
        {
            throw new ValidationException("Choose a valid bill.");
        }

        if (!access.Can("SALES", "DISCOUNT"))
        {
            return Forbid();
        }

        request.CanDiscount=access.Can("SALES","DISCOUNT");request.CanOverridePrice=access.Can("SALES","PRICE_OVERRIDE");
        var quote=await sales.QuoteAsync(request);
        await using var c = factory.CreateConnection();
        var policy=await BillingPolicyStore.ReadAsync(c);
        if(quote.AdjustmentReview is not {} review || !review.RequiresApproval)throw new ValidationException("This bill does not require manager approval.");
        policy.ValidateAdjustments(request,review,true);
        var snapshotColumn=policy.Version>0?",financial_snapshot":"";
        var snapshotValue=policy.Version>0?",CAST(@snapshot AS jsonb)":"";
        var resetSnapshot=policy.Version>0?",financial_snapshot=excluded.financial_snapshot":"";
        var resetRejection=policy.Version>0?",rejected_by=NULL,rejected_at=NULL":"";
        var saved = await c.ExecuteAsync("INSERT INTO billing_approvals(draft_id,requested_by,financial_hash,summary"+snapshotColumn+") VALUES(@id,@userId,@hash,CAST(@summary AS jsonb)"+snapshotValue+") ON CONFLICT(tenant_id,location_id,draft_id) DO UPDATE SET financial_hash=excluded.financial_hash,summary=excluded.summary,approved_by=NULL,approved_at=NULL,requested_at=now()"+resetRejection+resetSnapshot+" WHERE billing_approvals.requested_by=@userId",
new
{
    id,
    userId = factory.Scope!.UserId,
    hash = BillingApproval.Hash(request,policy.Stamp,policy.Version>0?quote:null),
    snapshot = JsonSerializer.Serialize(quote),
    summary = JsonSerializer.Serialize(request)
});
        if (saved != 1)
        {
            return Conflict(new
            {
                message = "This approval request belongs to another cashier."
            });
        }

        return NoContent();
    }

    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals()
    {
        if (!access.Can("SALES", "DISCOUNT_APPROVE"))
        {
            return Forbid();
        }

        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        var unreviewed=await BillingPolicyStore.AvailableAsync(c)?" AND rejected_at IS NULL":"";
        var rows = await c.QueryAsync<ApprovalRow>("SELECT a.draft_id AS DraftId,u.full_name AS Cashier,a.summary::text AS Summary,a.financial_hash AS Hash,(to_jsonb(a)->'financial_snapshot')::text AS FinancialSnapshot FROM billing_approvals a JOIN users u ON u.user_id=a.requested_by WHERE approved_at IS NULL AND requested_by<>public.sf_actor() AND requested_at>now()-interval '1 day'"+unreviewed+" ORDER BY requested_at LIMIT 100");
        var result = new List<object>();
        foreach (var row in rows)
        {
            var details = JsonSerializer.Deserialize<CreateSaleRequest>(row.Summary)!;
            var savedQuote=row.FinancialSnapshot is null?null:JsonSerializer.Deserialize<SaleQuote>(row.FinancialSnapshot);
            var variants = (await c.QueryAsync<ApprovalVariant>("SELECT v.variant_id AS VariantId,p.product_name||' / '||v.variant_name AS Name,v.selling_price AS CatalogPrice,v.sku AS Sku FROM product_variants v JOIN products p USING(product_id) WHERE v.variant_id=ANY(@ids)",
new
{
    ids = details.Items.Where(i => i.VariantId.HasValue).Select(i => i.VariantId!.Value).Distinct().ToArray()
})).ToDictionary(v => v.VariantId);
            result.Add(new
            {
                row.DraftId,
                row.Cashier,
                row.Hash,
                summary = details,
                items = details.Items.Select((i,n) => new { name = i.VariantId.HasValue && variants.TryGetValue(i.VariantId.Value, out var v) ? v.Name : i.ManualItem?.Name ?? "Product " + i.ProductId, sku=i.VariantId.HasValue&&variants.TryGetValue(i.VariantId.Value,out var sku)?sku.Sku:null,i.Quantity, i.SellingPrice, i.DiscountAmount,i.DiscountPercent,i.AdjustmentReason,i.PriceAdjustmentReason, catalogPrice = savedQuote?.Items.ElementAtOrDefault(n)?.CatalogPrice ?? (i.VariantId.HasValue && variants.TryGetValue(i.VariantId.Value, out var price) ? price.CatalogPrice : i.SellingPrice) })
            });
        }

        return Ok(result);
    }

    [HttpPut("approvals/{id:guid}")]
    public async Task<IActionResult> Approve(Guid id, ApprovalRequest request)
    {
        if (!access.Can("SALES", "DISCOUNT_APPROVE"))
        {
            return Forbid();
        }

        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock_shared(hashtextextended('billing-policy:'||public.sf_tenant()::text||':'||public.sf_store()::text,0))",transaction:t);
        var policy=await BillingPolicyStore.ReadAsync(c,t);
        var summary=await c.QuerySingleOrDefaultAsync<string>("SELECT summary::text FROM billing_approvals WHERE draft_id=@id AND requested_by<>@userId FOR UPDATE",new{id,userId=factory.Scope!.UserId},t);
        if(summary is null)return Conflict(new{message="A different authorized manager must review this request."});
        var details=JsonSerializer.Deserialize<CreateSaleRequest>(summary)!;
        var snapshot=policy.Version>0?await c.QuerySingleOrDefaultAsync<string>("SELECT financial_snapshot::text FROM billing_approvals WHERE draft_id=@id",new{id},t):null;
        var approvedQuote=snapshot is null?null:JsonSerializer.Deserialize<SaleQuote>(snapshot);
        if(BillingApproval.Hash(details,policy.Stamp,approvedQuote)!=request.Hash)return Conflict(new{message="The bill or shop policy changed. Request approval again."});
        if(request.Reject && policy.Version==0)throw new ValidationException("Apply migration 082 before rejecting approval requests.");
        var notRejected=policy.Version>0?" AND rejected_at IS NULL":"";
        var assignment=request.Reject?"rejected_by=@userId,rejected_at=now()":"approved_by=@userId,approved_at=now()";
        if(await c.ExecuteAsync("UPDATE billing_approvals SET "+assignment+" WHERE draft_id=@id AND financial_hash=@Hash AND approved_at IS NULL AND requested_by<>@userId AND requested_at>now()-interval '1 day'"+notRejected,new{id,request.Hash,userId=factory.Scope.UserId},t)!=1)
            return Conflict(new{message="Approval changed or expired. Refresh the requests."});
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,reason,new_values) VALUES(@userId,'Sales',@action,'billing_approvals',@reason,CAST(@payload AS jsonb))",
new
{
    userId = factory.Scope.UserId,
    action = request.Reject ? "DISCOUNT_REJECT" : "DISCOUNT_APPROVE",
    reason = request.Reject ? "Exception rejected" : "Financial details approved for 30 minutes",
    payload = JsonSerializer.Serialize(new
    {
        id,
        request.Hash
    })
},
t);
        await t.CommitAsync();
        return NoContent();
    }

    public record ApprovalRequest(string Hash, bool Reject = false);
    private sealed record ApprovalRow(Guid DraftId, string Cashier, string Summary, string Hash, string? FinancialSnapshot);
    private sealed record ApprovalVariant(long VariantId, string Name, decimal CatalogPrice, string? Sku);
    [HttpGet("policy")]
    [HttpGet("/api/settings/billing-policy")]
    public async Task<IActionResult> Policy()
    {
        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        var policy=await BillingPolicyStore.ReadAsync(c);
        return Ok(new
        {
            discountLimitPercent = policy.DiscountLimitPercent,
            policy.FastBillingEnabled,policy.AllowRoutineDiscounts,policy.RequireRoutineReason,policy.RequireExceptionalReason,policy.RequireApprovalAboveLimit,policy.Version,
            shopScoped = policy.Version>0,
            canOverridePrice = access.Can("SALES", "PRICE_OVERRIDE"),
            canOverrideTax = access.Can("SALES", "TAX_OVERRIDE"),
            canDiscount = access.Can("SALES", "DISCOUNT"),
            canApproveDiscount = access.Can("SALES", "DISCOUNT_APPROVE"),
            canTradeIn = access.Can("SALES", "FULFIL"),
            canManage = access.Can("COMPANY_SETTINGS", "EDIT")
        });
    }

    [HttpPut("policy")]
    [HttpPut("/api/settings/billing-policy")]
    public async Task<IActionResult> SavePolicy(PolicyRequest request)
    {
        if (!access.Can("COMPANY_SETTINGS", "EDIT"))
        {
            return Forbid();
        }

        if (request.DiscountLimitPercent is < 0 or > 100 || request.DiscountLimitPercent != SaleRules.Money(request.DiscountLimitPercent))
        {
            throw new ValidationException("Discount limit must be between 0 and 100%.");
        }

        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        factory.RequireLocation();
        if(!await BillingPolicyStore.AvailableAsync(c,t))throw new ValidationException("Apply migration 082 to manage the shop-specific Fast Billing Policy.");
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('billing-policy:'||public.sf_tenant()::text||':'||public.sf_store()::text,0))",transaction:t);
        var current=await BillingPolicyStore.ReadAsync(c,t);
        if(request.Version.HasValue&&request.Version.Value!=current.Version)return Conflict(new{message="The policy changed. Reload it before saving."});
        await c.ExecuteAsync("INSERT INTO billing_policy(store_id,version,discount_limit_percent,fast_billing_enabled,allow_routine_discounts,require_routine_reason,require_exceptional_reason,require_approval_above_limit) VALUES(public.sf_store(),@NextVersion,@DiscountLimitPercent,@FastBillingEnabled,@AllowRoutineDiscounts,@RequireRoutineReason,@RequireExceptionalReason,@RequireApprovalAboveLimit) ON CONFLICT(tenant_id,store_id) DO UPDATE SET discount_limit_percent=excluded.discount_limit_percent,fast_billing_enabled=excluded.fast_billing_enabled,allow_routine_discounts=excluded.allow_routine_discounts,require_routine_reason=excluded.require_routine_reason,require_exceptional_reason=excluded.require_exceptional_reason,require_approval_above_limit=excluded.require_approval_above_limit",new{request.DiscountLimitPercent,NextVersion=current.Version+1,FastBillingEnabled=request.FastBillingEnabled??current.FastBillingEnabled,AllowRoutineDiscounts=request.AllowRoutineDiscounts??current.AllowRoutineDiscounts,RequireRoutineReason=request.RequireRoutineReason??current.RequireRoutineReason,RequireExceptionalReason=request.RequireExceptionalReason??current.RequireExceptionalReason,RequireApprovalAboveLimit=request.RequireApprovalAboveLimit??current.RequireApprovalAboveLimit},t);
        var updated=await BillingPolicyStore.ReadAsync(c,t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,'Sales','POLICY_CHANGE','billing_policy',@tenantId,CAST(@payload AS jsonb))",
new
{
    userId = factory.Scope!.UserId,
    tenantId = factory.Scope.TenantId,
    payload = JsonSerializer.Serialize(new{ShopId=factory.Scope.StoreId,Previous=current,Current=updated})
},
t);
        await t.CommitAsync();
        return Ok(new{version=updated.Version});
    }

    [HttpGet("customers/{id:int}/credit")]
    public async Task<IActionResult> Credit(int id)
    {
        await using var c = factory.CreateConnection();
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM customers WHERE customer_id=@id AND is_active)", new
        {
            id
        }))
        {
            return NotFound();
        }

        return Ok(new
        {
            outstanding = await c.ExecuteScalarAsync<decimal>("SELECT sf_customer_credit_balance(@id)", new
            {
                id
            }),
            creditLimit = await c.QuerySingleOrDefaultAsync<decimal?>("SELECT credit_limit FROM customer_credit_limits WHERE customer_id=@id", new
            {
                id
            })
        });
    }

    [HttpPut("customers/{id:int}/credit")]
    public async Task<IActionResult> SetCredit(int id, CreditRequest request)
    {
        if (!access.Can("CUSTOMERS", "EDIT"))
        {
            return Forbid();
        }

        if (request.CreditLimit is < 0 or > 9999999999.99m)
        {
            throw new ValidationException("Enter a nonnegative credit limit, or leave it empty for no limit.");
        }

        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if (await c.QuerySingleOrDefaultAsync<int?>("SELECT customer_id FROM customers WHERE customer_id=@id AND is_active FOR UPDATE", new { id }, t) is null)
        {
            return NotFound();
        }

        await c.ExecuteAsync("INSERT INTO customer_credit_limits(customer_id,credit_limit) VALUES(@id,@CreditLimit) ON CONFLICT(tenant_id,customer_id) DO UPDATE SET credit_limit=excluded.credit_limit",
new
{
    id,
    request.CreditLimit
},
t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,'Customers','CREDIT_LIMIT','customers',@id,CAST(@payload AS jsonb))",
new
{
    userId = factory.Scope!.UserId,
    id,
    payload = JsonSerializer.Serialize(request)
},
t);
        await t.CommitAsync();
        return NoContent();
    }

    [HttpGet("drafts")]
    public async Task<IActionResult> Drafts(string? search = null, int page = 1, bool paged = false, bool recovery = false, bool includeActive = false, Guid? editorId = null)
    {
        factory.RequireLocation();await using var c=factory.CreateConnection();
        page=Math.Clamp(page,1,100000);if(search?.Length>150)throw new ValidationException("Search must be at most 150 characters.");
        // Keep the held-only API default for existing callers. New Sale includes
        // its own editor and recoverable Active records without exposing live
        // sessions owned by another editor. RLS still scopes every read.
        var scope=recovery?"(draft_status='LEGACY' OR draft_status='ACTIVE' AND (lease_until IS NULL OR lease_until<=now()))":includeActive?"(draft_status='HELD' OR draft_status='ACTIVE' AND (editor_id=@editorId OR lease_until IS NULL OR lease_until<=now()))":"draft_status='HELD'";
        var eligible=" FROM billing_drafts d WHERE "+scope+" AND NOT EXISTS(SELECT 1 FROM sales s WHERE s.request_id=d.draft_id) AND (@search='' OR strpos(lower(sf_draft_number(draft_sequence)||' '||coalesce(payload->>'customerLabel','')||' '||coalesce(payload->>'customerMobile','')||' '||label||' '||draft_id::text),lower(@search))>0)";
        var args=new{search=search?.Trim()??"",offset=(page-1)*25,editorId};
        await c.OpenAsync();await using var read=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var total=await c.ExecuteScalarAsync<int>("SELECT count(*)"+eligible,args,read);
        var rows=await c.QueryAsync<DraftListRow>("SELECT draft_id AS DraftId,sf_draft_number(draft_sequence) AS DraftNumber,creation_time_source AS CreationTimeSource,draft_status AS DraftStatus,revision AS Revision,label,updated_at AS UpdatedAt,payload::text AS Payload"+eligible+" ORDER BY updated_at DESC,draft_id LIMIT 25 OFFSET @offset",args,read);
        await read.CommitAsync();
        var items=rows.Select(row=>{
            using var doc=JsonDocument.Parse(row.Payload);var p=doc.RootElement;
            string? Text(string name)=>p.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
            var selected=p.TryGetProperty("customerId",out var customer)&&int.TryParse(customer.ToString(),out var customerId)&&customerId>0;
            var lines=p.TryGetProperty("cart",out var cart)&&cart.ValueKind==JsonValueKind.Array?cart.EnumerateArray().ToArray():[];
            decimal? estimate=lines.Length==0?0:p.TryGetProperty("estimatedTotal",out var amount)&&amount.ValueKind==JsonValueKind.Number&&amount.TryGetDecimal(out var value)?value:null;
            return new{row.DraftId,row.DraftNumber,row.CreationTimeSource,row.DraftStatus,row.Revision,row.Label,row.UpdatedAt,customerName=selected?Text("customerLabel"):null,itemCount=lines.Length,units=lines.Sum(line=>line.ValueKind==JsonValueKind.Object&&line.TryGetProperty("quantity",out var q)&&q.ValueKind==JsonValueKind.Number&&q.TryGetDecimal(out var n)?n:0),estimatedTotal=estimate};
        }).ToArray();return paged?Ok(new{items,total,page,pageSize=25}):Ok(items);
    }
    [HttpGet("drafts/{id:guid}")]
    public async Task<IActionResult> Draft(Guid id)
    {
        factory.RequireLocation();await using var c=factory.CreateConnection();
        var payload=await c.QuerySingleOrDefaultAsync<string>("SELECT (payload||jsonb_build_object('draftNumber',sf_draft_number(draft_sequence),'draftCreatedAt',created_at,'creationTimeSource',creation_time_source,'draftStatus',draft_status,'revision',revision,'leaseUntil',lease_until))::text FROM billing_drafts d WHERE draft_id=@id AND draft_status IN('ACTIVE','HELD','LEGACY') AND NOT EXISTS(SELECT 1 FROM sales s WHERE s.request_id=d.draft_id)",new{id});
        return payload is null?NotFound():Content(payload,"application/json");
    }
    [HttpPut("drafts/{id:guid}")]
    public async Task<IActionResult> SaveDraft(Guid id,DraftRequest request,bool reference=false)
    {
        if(id==Guid.Empty||string.IsNullOrWhiteSpace(request.Label)||request.Label.Length>150||request.Payload.ValueKind!=JsonValueKind.Object||request.Payload.GetRawText().Length>250000
            ||!request.Payload.TryGetProperty("draftId",out var payloadId)||payloadId.ValueKind!=JsonValueKind.String||!Guid.TryParse(payloadId.GetString(),out var matching)||matching!=id)
            throw new ValidationException("Invalid billing draft or mismatched identity.");
        return await DraftAction(id,"SAVE",request.EditorId,request.Revision,request.OperationId,request.Label,request.Payload.GetRawText());
    }
    private async Task<IActionResult> DraftAction(Guid id,string action,Guid editorId,long revision,Guid operationId,string? label=null,string? payload=null)
    {
        factory.RequireLocation();if(editorId==Guid.Empty||operationId==Guid.Empty||revision<0)return BadRequest(new{message="Reload New Sale to establish a valid editing session."});
        await using var c=factory.CreateConnection();
        try{var result=await c.ExecuteScalarAsync<string>("SELECT public.sf_draft_action(@action,@id,@editorId,@revision,@operationId,@label,@payload::jsonb)::text",new{action,id,editorId,revision,operationId,label,payload});return Content(result!,"application/json");}
        catch(Npgsql.PostgresException ex)when(ex.SqlState=="P0001"){return Conflict(new{message=ex.MessageText});}
    }
    [HttpPost("drafts/{id:guid}/hold")]
    public Task<IActionResult> HoldDraft(Guid id,DraftCommand request)=>DraftAction(id,"HOLD",request.EditorId,request.Revision,request.OperationId);
    [HttpPost("drafts/{id:guid}/heartbeat")]
    public Task<IActionResult> HeartbeatDraft(Guid id,DraftCommand request)=>DraftAction(id,"HEARTBEAT",request.EditorId,request.Revision,request.OperationId);
    [HttpPost("drafts/{id:guid}/release")]
    public Task<IActionResult> ReleaseDraft(Guid id,DraftCommand request)=>DraftAction(id,"RELEASE",request.EditorId,request.Revision,request.OperationId);
    [HttpPost("drafts/{id:guid}/resume")]
    public async Task<IActionResult> ResumeDraft(Guid id,ResumeDraftRequest request)
    {
        factory.RequireLocation();if(request.EditorId==Guid.Empty||request.OperationId==Guid.Empty||request.Revision<1)return BadRequest(new{message="Refresh saved bills before resuming."});
        await using var c=factory.CreateConnection();
        try{var result=await c.ExecuteScalarAsync<string>("SELECT public.sf_draft_switch(@id,@EditorId,@Revision,@OperationId,@Recover,@CurrentDraftId,@CurrentRevision,@CurrentAction)::text",new{id,request.EditorId,request.Revision,request.OperationId,request.Recover,request.CurrentDraftId,request.CurrentRevision,request.CurrentAction});return Content(result!,"application/json");}
        catch(Npgsql.PostgresException ex)when(ex.SqlState=="P0001"){return Conflict(new{message=ex.MessageText});}
    }
    [HttpDelete("drafts/{id:guid}")]
    public Task<IActionResult> DeleteDraft(Guid id,[FromBody] DraftCommand request)=>DraftAction(id,"DISCARD",request.EditorId,request.Revision,request.OperationId);
    public record PolicyRequest(decimal DiscountLimitPercent, bool? FastBillingEnabled = null, bool? AllowRoutineDiscounts = null, bool? RequireRoutineReason = null, bool? RequireExceptionalReason = null, bool? RequireApprovalAboveLimit = null, long? Version = null);
    public record CreditRequest(decimal? CreditLimit);
    public record DraftRequest(string Label,JsonElement Payload,Guid EditorId=default,long Revision=0,Guid OperationId=default);
    public record DraftCommand(Guid EditorId,long Revision,Guid OperationId);
    public record ResumeDraftRequest(Guid EditorId,long Revision,Guid OperationId,bool Recover=false,Guid? CurrentDraftId=null,long? CurrentRevision=null,string? CurrentAction=null);
    private sealed record DraftListRow(Guid DraftId,string DraftNumber,string CreationTimeSource,string DraftStatus,long Revision,string Label,DateTime UpdatedAt,string Payload);
}
