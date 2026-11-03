using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
namespace StockFlow.Infrastructure.Repositories;

public sealed class ExpenseRepository(DbConnectionFactory factory):IExpenseRepository
{
    private static JsonElement Read(string? json)=>JsonSerializer.Deserialize<JsonElement>(json??"null");
    private async Task Require(Npgsql.NpgsqlConnection c,string action="VIEW")
    {
        factory.RequireLocation();
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_expense_allowed('VIEW') AND public.sf_expense_allowed(@action)",new{action}))throw new UnauthorizedAccessException("Expense permission required in the selected shop.");
    }
    public async Task<JsonElement> OptionsAsync()
    {
        await using var c=factory.CreateConnection();await Require(c);
        return Read(await c.ExecuteScalarAsync<string>("""
          SELECT jsonb_build_object('categories',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',category_id,'name',name) ORDER BY name),'[]') FROM expense_categories),
          'payees',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',payee_id,'name',name) ORDER BY name),'[]') FROM expense_payees),
          'permissions',(SELECT jsonb_object_agg(act,sf_expense_allowed(act)) FROM unnest(ARRAY['CREATE','EDIT','POST','PAY','REVERSE','CANCEL','ATTACH','EXPORT']) act))::text
          """));
    }
    private const string RowSql="""
        SELECT e.*,c.name category,p.name payee,coalesce((SELECT sum(amount) FROM expense_payments x WHERE x.tenant_id=e.tenant_id AND x.expense_id=e.expense_id),0) paid
        FROM expenses e JOIN expense_categories c USING(tenant_id,category_id) JOIN expense_payees p USING(tenant_id,payee_id)
        """;
    public async Task<JsonElement> ListAsync(string? term,DateTime? from,DateTime? to,int page,int pageSize)
    {
        if(page<1||page>100000||pageSize<1||pageSize>100||term?.Length>150||from>to)throw new ValidationException("Invalid expense filters or page.");
        await using var c=factory.CreateConnection();await Require(c);
        return Read(await c.ExecuteScalarAsync<string>("""
          WITH filtered AS (
          """+RowSql+"""
          WHERE (@term IS NULL OR e.description ILIKE '%'||@term||'%' OR p.name ILIKE '%'||@term||'%' OR c.name ILIKE '%'||@term||'%')
           AND (@from::date IS NULL OR e.incurred_date>=@from::date) AND (@to::date IS NULL OR e.incurred_date<=@to::date)
          ), paged AS(SELECT * FROM filtered ORDER BY incurred_date DESC,expense_id OFFSET @offset LIMIT @pageSize)
          SELECT jsonb_build_object('total',(SELECT count(*) FROM filtered),'page',@page,'pageSize',@pageSize,'rows',(SELECT coalesce(jsonb_agg(to_jsonb(paged)),'[]') FROM paged))::text
          """,new{term=string.IsNullOrWhiteSpace(term)?null:term.Trim(),from,to,page,pageSize,offset=(page-1)*pageSize}));
    }
    public async Task<JsonElement> GetAsync(Guid id)
    {
        await using var c=factory.CreateConnection();await Require(c);
        var value=await c.ExecuteScalarAsync<string>("""
          SELECT (to_jsonb(r)||jsonb_build_object('payments',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p.created_at,p.payment_id),'[]') FROM expense_payments p WHERE p.expense_id=r.expense_id),
          'events',(SELECT coalesce(jsonb_agg(jsonb_build_object('action',x.action,'version',x.version,'at',x.created_at,'actor',x.actor_id,'reason',x.input->'body'->>'reason','snapshot',x.snapshot) ORDER BY x.version),'[]') FROM expense_events x WHERE x.expense_id=r.expense_id),
          'attachments',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',a.attachment_id,'name',a.name,'at',a.created_at)),'[]') FROM expense_attachments a WHERE a.expense_id=r.expense_id)))::text
          FROM (
          """+RowSql+" WHERE e.expense_id=@id) r",new{id});
        return value is null?throw new KeyNotFoundException("Expense not found in selected shop."):Read(value);
    }
    public async Task<long> WriteAsync(Guid id,string action,ExpenseOperation operation)
    {
        if(id==Guid.Empty||operation.OperationId==Guid.Empty||operation.ExpectedVersion<0||operation.Body.ValueKind!=JsonValueKind.Object)throw new ValidationException("A valid expense, operation identity, version and structured body are required.");
        await using var c=factory.CreateConnection();await Require(c);
        return await c.ExecuteScalarAsync<long>("SELECT sf_expense_write(@id,@op,@expected,@action,@body::jsonb)",new{id,op=operation.OperationId,expected=operation.ExpectedVersion,action,body=operation.Body.GetRawText()});
    }
    public async Task<Guid> MasterAsync(string kind,Guid id,string name)
    {
        await using var c=factory.CreateConnection();await Require(c,"EDIT");
        return await c.ExecuteScalarAsync<Guid>("SELECT sf_expense_master(@kind,@id,@name)",new{kind,id,name});
    }
    public async Task<JsonElement> ReportAsync(DateTime from,DateTime to,bool export=false)
    {
        if(from==default||to==default||from>to||to-from>TimeSpan.FromDays(3660))throw new ValidationException("Choose a valid expense report period (up to ten years).");
        await using var c=factory.CreateConnection();await Require(c,export?"EXPORT":"VIEW");
        return Read(await c.ExecuteScalarAsync<string>("""
          SELECT jsonb_build_object('from',@from::date,'to',@to::date,'storeId',sf_store(),'locationId',sf_location(),'profile',sf_visible_business_profile(),
           'incurred',(SELECT coalesce(sum(amount),0) FROM expenses WHERE status='POSTED' AND incurred_date BETWEEN @from::date AND @to::date),
           'paid',(SELECT coalesce(sum(amount),0) FROM expense_payments WHERE paid_date BETWEEN @from::date AND @to::date),
           'outstandingAtCutoff',(SELECT coalesce(sum(e.amount-coalesce((SELECT sum(p.amount) FROM expense_payments p WHERE p.expense_id=e.expense_id AND p.paid_date<=@to::date),0)),0) FROM expenses e WHERE e.status='POSTED' AND e.incurred_date<=@to::date),
           'expenses',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r.incurred_date,r.expense_id),'[]') FROM (
          """+RowSql+"""
           WHERE e.incurred_date BETWEEN @from::date AND @to::date) r),
           'payments',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY paid_date,payment_id),'[]') FROM expense_payments p WHERE paid_date BETWEEN @from::date AND @to::date),
           'definition','Current corrected posted expense amounts by incurred date; signed recorded payments/refunds/reversals by paid date. Cutoff dues use current corrected posted amounts, not historical as-recorded accruals. No eligible GST ITC or net-profit assertion.')::text
          """,new{from=from.Date,to=to.Date}));
    }
    public async Task<(string Name,string ContentType,byte[] Data)?> AttachmentAsync(Guid expenseId,Guid attachmentId)
    {
        await using var c=factory.CreateConnection();await Require(c);
        return await c.QuerySingleOrDefaultAsync<(string,string,byte[])?> ("SELECT name,content_type,data FROM expense_attachments WHERE expense_id=@expenseId AND attachment_id=@attachmentId",new{expenseId,attachmentId});
    }
}
