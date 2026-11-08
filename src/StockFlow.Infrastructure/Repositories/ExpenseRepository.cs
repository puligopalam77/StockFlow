using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Common;
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
          'taxRules',sf_expense_tax_options(),
          'businessDate',to_char((now() AT TIME ZONE 'Asia/Kolkata')::date,'YYYY-MM-DD'),
          'permissions',(SELECT jsonb_object_agg(act,sf_expense_allowed(act)) FROM unnest(ARRAY['CREATE','EDIT','POST','PAY','REVERSE','CANCEL','ATTACH','EXPORT']) act))::text
          """));
    }
    public async Task<JsonElement> TaxPreviewAsync(ExpenseTaxPreview request)
    {
        if(request.Amount is <=0 or >9999999999.99m||SaleRules.Money(request.Amount)!=request.Amount||request.IncurredDate==default||request.IncurredDate.TimeOfDay!=TimeSpan.Zero||request.Gst.ValueKind!=JsonValueKind.Object)
            throw new ValidationException("Enter an expense amount, date and configured GST rate.");
        await using var c=factory.CreateConnection();await Require(c);
        var businessDate=await c.ExecuteScalarAsync<DateTime>("SELECT (now() AT TIME ZONE 'Asia/Kolkata')::date");
        if(request.IncurredDate.Date>businessDate)
            throw new ValidationException($"Expense date cannot be after the database business date ({businessDate:dd-MM-yyyy}). Check the database server clock if this date is incorrect.");
        JsonElement result;
        try{result=Read(await c.ExecuteScalarAsync<string>("SELECT sf_expense_gst(@gst::jsonb,@amount,@day::date,NULL::jsonb)::text",new{gst=request.Gst.GetRawText(),amount=request.Amount,day=request.IncurredDate}));}
        catch(Npgsql.PostgresException error)when(error.SqlState=="23514"){throw new ValidationException(error.MessageText);}
        if(result.ValueKind==JsonValueKind.Object)
        {
            var calculated=GstRules.InclusiveAmounts(request.Amount,result.GetProperty("rate").GetDecimal());
            if(calculated.TaxableAmount!=result.GetProperty("taxableAmount").GetDecimal()||calculated.GstAmount!=result.GetProperty("gstAmount").GetDecimal())
                throw new ValidationException("GST amounts could not be confirmed. Review the tax details.");
        }
        return result;
    }
    private static string Rows(bool cutoff=false)=>$"""
        SELECT e.*,c.name category,p.name payee,coalesce((SELECT sum(amount) FROM expense_payments x WHERE x.tenant_id=e.tenant_id AND x.expense_id=e.expense_id{(cutoff?" AND x.paid_date<=@to::date":"")}),0) paid
        FROM expenses e JOIN expense_categories c USING(tenant_id,category_id) JOIN expense_payees p USING(tenant_id,payee_id)
        """;
    public async Task<JsonElement> ListAsync(string? term,DateTime? from,DateTime? to,int page,int pageSize,Guid? category=null,Guid? payee=null,string? status=null,string? paymentStatus=null,string sort="newest")
    {
        if(page<1||page>100000||pageSize<1||pageSize>100||term?.Length>150||from>to)throw new ValidationException("Invalid expense filters or page.");
        if(status is not(null or "" or "DRAFT" or "POSTED" or "CANCELLED")||paymentStatus is not(null or "" or "UNPAID" or "PARTIAL" or "PAID"))throw new ValidationException("Choose valid expense lifecycle and payment filters.");
        var ordering=sort switch { "newest"=>"incurred_date DESC,expense_id", "oldest"=>"incurred_date,expense_id", "amount-desc"=>"amount DESC,incurred_date DESC,expense_id", "amount-asc"=>"amount,incurred_date DESC,expense_id", _=>throw new ValidationException("Choose a valid expense sort order.") };
        await using var c=factory.CreateConnection();await Require(c);
        return Read(await c.ExecuteScalarAsync<string>("""
          WITH filtered AS (
          """+Rows()+"""
          WHERE (@term IS NULL OR e.description ILIKE '%'||@term||'%' OR p.name ILIKE '%'||@term||'%' OR c.name ILIKE '%'||@term||'%')
           AND (@from::date IS NULL OR e.incurred_date>=@from::date) AND (@to::date IS NULL OR e.incurred_date<=@to::date)
           AND (@category::uuid IS NULL OR e.category_id=@category) AND (@payee::uuid IS NULL OR e.payee_id=@payee)
           AND ((@status IS NULL AND e.status<>'DRAFT') OR e.status=@status)
          ), selected AS(SELECT * FROM filtered WHERE @paymentStatus IS NULL OR status='POSTED' AND CASE @paymentStatus WHEN 'UNPAID' THEN paid=0 WHEN 'PARTIAL' THEN paid>0 AND paid<amount WHEN 'PAID' THEN paid=amount ELSE false END), paged AS(SELECT * FROM selected ORDER BY
          """+" "+ordering+" "+"""
           OFFSET @offset LIMIT @pageSize)
          SELECT jsonb_build_object('total',(SELECT count(*) FROM selected),'page',@page,'pageSize',@pageSize,
           'hasExpenses',EXISTS(SELECT 1 FROM expenses),
           'legacyDraftCount',(SELECT count(*) FROM expenses WHERE status='DRAFT'),
           'summary',(SELECT jsonb_build_object('totalExpenses',coalesce(sum(amount),0),'amountPaid',coalesce(sum(paid),0),'outstanding',coalesce(sum(amount-paid),0)) FROM selected WHERE status='POSTED'),
           'rows',(SELECT coalesce(jsonb_agg(to_jsonb(paged)||jsonb_build_object('receiptId',(SELECT a.attachment_id FROM expense_attachments a WHERE a.tenant_id=paged.tenant_id AND a.expense_id=paged.expense_id ORDER BY a.created_at,a.attachment_id LIMIT 1))),'[]') FROM paged))::text
          """,new{term=string.IsNullOrWhiteSpace(term)?null:term.Trim(),from,to,page,pageSize,category,payee,status=string.IsNullOrEmpty(status)?null:status,paymentStatus=string.IsNullOrEmpty(paymentStatus)?null:paymentStatus,offset=(page-1)*pageSize}));
    }
    public async Task<JsonElement> GetAsync(Guid id)
    {
        await using var c=factory.CreateConnection();await Require(c);
        var value=await c.ExecuteScalarAsync<string>("""
          SELECT (to_jsonb(r)||jsonb_build_object('payments',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p.created_at,p.payment_id),'[]') FROM expense_payments p WHERE p.expense_id=r.expense_id),
          'events',(SELECT coalesce(jsonb_agg(jsonb_build_object('action',x.action,'version',x.version,'at',x.created_at,'actor',x.actor_id,'reason',x.input->'body'->>'reason','snapshot',x.snapshot) ORDER BY x.version),'[]') FROM expense_events x WHERE x.expense_id=r.expense_id),
          'attachments',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',a.attachment_id,'name',a.name,'at',a.created_at)),'[]') FROM expense_attachments a WHERE a.expense_id=r.expense_id)))::text
          FROM (
          """+Rows()+" WHERE e.expense_id=@id) r",new{id});
        return value is null?throw new KeyNotFoundException("Expense not found in selected shop."):Read(value);
    }
    public async Task<long> WriteAsync(Guid id,string action,ExpenseOperation operation)
    {
        // RECORD retains its internal SAVE/POST transaction; standalone drafts are retired.
        if(string.Equals(action,"SAVE",StringComparison.OrdinalIgnoreCase))throw new ValidationException("Saving expense drafts is no longer supported. Use Record Expense.");
        if(id==Guid.Empty||operation.OperationId==Guid.Empty||operation.ExpectedVersion<0||operation.Body.ValueKind!=JsonValueKind.Object)throw new ValidationException("A valid expense, operation identity, version and structured body are required.");
        await using var c=factory.CreateConnection();await Require(c);
        return await c.ExecuteScalarAsync<long>("SELECT sf_expense_write(@id,@op,@expected,@action,@body::jsonb)",new{id,op=operation.OperationId,expected=operation.ExpectedVersion,action,body=operation.Body.GetRawText()});
    }
    public async Task<long> RecordAsync(Guid id,ExpenseOperation operation)
    {
        if(id==Guid.Empty||operation.OperationId==Guid.Empty||operation.ExpectedVersion<0||operation.Body.ValueKind!=JsonValueKind.Object||!operation.Body.TryGetProperty("expense",out var input)||input.ValueKind!=JsonValueKind.Object)throw new ValidationException("A valid expense, operation identity, version and expense details are required.");
        JsonElement? payment=operation.Body.TryGetProperty("payment",out var p)&&p.ValueKind!=JsonValueKind.Null?p:null;
        if(payment is {} pay&&(pay.ValueKind!=JsonValueKind.Object||!pay.TryGetProperty("confirmed",out var confirmed)||confirmed.ValueKind!=JsonValueKind.True))throw new ValidationException("Confirm the actual initial payment before recording it.");
        await using var c=factory.CreateConnection();await Require(c);await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
        Guid Stage(string stage)=>new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operation.OperationId.ToString("N")+":expense-record:"+stage)).AsSpan(0,16));
        async Task<long> Write(string action,long expected,JsonElement body)=>await c.ExecuteScalarAsync<long>("SELECT sf_expense_write(@id,@op,@expected,@action,@body::jsonb)",new{id,op=Stage(action),expected,action,body=body.GetRawText()},t);
        // Bind the complete intent into the first immutable event, including an
        // absent payment. A changed retry cannot append a new initial payment.
        var details=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(input.GetRawText())!;details["recordIntent"]=operation.Body;
        var version=await Write("SAVE",operation.ExpectedVersion,JsonSerializer.SerializeToElement(details));
        version=await Write("POST",version,JsonSerializer.SerializeToElement(new{}));
        if(payment is {} initial)version=await Write("PAY",version,initial);
        await t.CommitAsync();return version;
    }
    public async Task<Guid> MasterAsync(string kind,Guid id,string name,string? mobileNumber=null)
    {
        if(kind is not("CATEGORY" or "PAYEE")||id==Guid.Empty||string.IsNullOrWhiteSpace(name)||name.Trim().Length>150)throw new ValidationException("Enter a valid category or payee name.");
        mobileNumber=string.IsNullOrWhiteSpace(mobileNumber)?null:mobileNumber.Trim();
        if(mobileNumber is not null&&(kind!="PAYEE"||!System.Text.RegularExpressions.Regex.IsMatch(mobileNumber,@"\A[6-9][0-9]{9}\z")))throw new ValidationException("Enter a valid 10-digit mobile number starting with 6, 7, 8 or 9.");
        await using var c=factory.CreateConnection();await Require(c,"EDIT");
        await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('catalog-registration:'||sf_tenant()::text,0))",transaction:t);
        var table=kind=="CATEGORY"?"expense_categories":"expense_payees";var key=kind=="CATEGORY"?"category_id":"payee_id";
        var existing=(await c.QueryAsync<Guid>($"SELECT {key} FROM {table} WHERE lower(btrim(name))=lower(@name)",new{name=name.Trim()},t)).ToArray();
        if(existing.Length>1)throw new ValidationException("Several existing names match. Choose the existing record instead of merging them.");
        var result=existing.Length==1?existing[0]:await c.ExecuteScalarAsync<Guid>("SELECT sf_expense_master(@kind,@id,@name,@mobileNumber)",new{kind,id,name=name.Trim(),mobileNumber},t);await t.CommitAsync();return result;
    }
    public async Task<JsonElement> ReportAsync(DateTime from,DateTime to,bool export=false)
    {
        if(from==default||to==default||from>to||to-from>TimeSpan.FromDays(3660))throw new ValidationException("Choose a valid expense report period (up to ten years).");
        await using var c=factory.CreateConnection();await Require(c,export?"EXPORT":"VIEW");
        return Read(await c.ExecuteScalarAsync<string>("""
          WITH period_expenses AS (
          """+Rows()+"""
           WHERE e.status='POSTED' AND e.incurred_date BETWEEN @from::date AND @to::date)
          SELECT jsonb_build_object('from',@from::date,'to',@to::date,'storeId',sf_store(),'locationId',sf_location(),'profile',sf_visible_business_profile(),
           'summary',(SELECT jsonb_build_object('totalExpenses',coalesce(sum(amount),0),'amountPaid',coalesce(sum(paid),0),'outstanding',coalesce(sum(amount-paid),0)) FROM period_expenses),
           'incurred',(SELECT coalesce(sum(amount),0) FROM expenses WHERE status='POSTED' AND incurred_date BETWEEN @from::date AND @to::date),
           'paid',(SELECT coalesce(sum(amount),0) FROM expense_payments WHERE paid_date BETWEEN @from::date AND @to::date),
           'outstandingAtCutoff',(SELECT coalesce(sum(e.amount-coalesce((SELECT sum(p.amount) FROM expense_payments p WHERE p.expense_id=e.expense_id AND p.paid_date<=@to::date),0)),0) FROM expenses e WHERE e.status='POSTED' AND e.incurred_date<=@to::date),
           'expenses',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r.incurred_date,r.expense_id),'[]') FROM (
          """+Rows(true)+"""
           WHERE e.incurred_date BETWEEN @from::date AND @to::date) r),
           'payments',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY paid_date,payment_id),'[]') FROM expense_payments p WHERE paid_date BETWEEN @from::date AND @to::date),
           'monthly',(SELECT coalesce(jsonb_agg(to_jsonb(m) ORDER BY m."month"),'[]') FROM
            (SELECT to_char(incurred_date,'YYYY-MM') AS "month",count(*) AS count,sum(amount) total FROM expenses
             WHERE status='POSTED' AND incurred_date BETWEEN @from::date AND @to::date GROUP BY 1) m),
           'categories',(SELECT coalesce(jsonb_agg(to_jsonb(g) ORDER BY total DESC,category),'[]') FROM
            (SELECT e.category_id,c.name category,count(*) count,sum(e.amount) total FROM expenses e JOIN expense_categories c USING(tenant_id,category_id)
             WHERE e.status='POSTED' AND e.incurred_date BETWEEN @from::date AND @to::date GROUP BY e.category_id,c.name) g),
           'outstanding',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY incurred_date,expense_id),'[]') FROM (
          """+Rows(true)+"""
           WHERE e.status='POSTED' AND e.incurred_date<=@to::date) o WHERE o.amount>o.paid),
           'definition','Recorded expense totals use the current corrected amount and expense date, including any GST. Payments use signed payment/refund/reversal amounts and payment dates. Outstanding balances use payments through the cutoff, including earlier expenses. Drafts and cancelled expenses are excluded from expense totals. GST reference ITC remains unclaimed; these reports do not calculate net profit.')::text
          """,new{from=from.Date,to=to.Date}));
    }
    public async Task<(string Name,string ContentType,byte[] Data)?> AttachmentAsync(Guid expenseId,Guid attachmentId)
    {
        await using var c=factory.CreateConnection();await Require(c);
        return await c.QuerySingleOrDefaultAsync<(string,string,byte[])?> ("SELECT name,content_type,data FROM expense_attachments WHERE expense_id=@expenseId AND attachment_id=@attachmentId",new{expenseId,attachmentId});
    }
}
