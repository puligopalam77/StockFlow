using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class QuotationRepository(DbConnectionFactory factory, ISalesRepository sales, IOptions<ShopTimeOptions> time, TimeProvider clock) : IQuotationRepository
{
    private const string Header = """
        SELECT q.quotation_id QuotationId,q.quotation_number QuotationNumber,q.customer_name CustomerName,
        CASE WHEN q.status IN('DRAFT','SENT','ACCEPTED') AND q.valid_until<(now() AT TIME ZONE 'Asia/Kolkata')::date THEN 'EXPIRED' ELSE q.status END Status,
        q.document_date DocumentDate,q.valid_until ValidUntil,q.total Total,q.item_count ItemCount,q.current_revision CurrentRevision,q.version Version,c.sale_id SaleId
        FROM quotations q LEFT JOIN quotation_conversions c USING(tenant_id,quotation_id)
        """;
    private async Task Require(NpgsqlConnection c, string action)
    {
        factory.RequireLocation();
        if (!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_quotation_allowed(text)') IS NOT NULL"))
            throw new ValidationException("Quotations require manual database migration 083. Ask the administrator to apply it after backup and review.");
        if (!await c.ExecuteScalarAsync<bool>("SELECT public.sf_quotation_allowed(@action)",new {action}))
            throw new UnauthorizedAccessException("Quotation permission is required in the selected shop and location.");
    }
    public async Task RequireAsync(string action) { await using var c=factory.CreateConnection();await Require(c,action); }
    private async Task Prepare(NpgsqlConnection c, CreateSaleRequest r)
    {
        factory.RequireLocation(r.LocationId);
        if(r.SourceQuotationId.HasValue || r.SourceQuotationRevision.HasValue || r.SourceQuotationVersion.HasValue || r.Payments is null || r.Payments.Count!=0 || r.TradeIn is not null || r.PaymentDueDate.HasValue || r.CashTendered.HasValue)
            throw new ValidationException("Quotation estimates cannot contain payments, trade-ins, credit terms or invoice-conversion authority.");
        r.LocationId=factory.Scope!.LocationId;
        r.CanDiscount=await c.ExecuteScalarAsync<bool>("SELECT public.sf_transfer_permission('SALES','DISCOUNT')");
        r.CanOverridePrice=await c.ExecuteScalarAsync<bool>("SELECT public.sf_transfer_permission('SALES','PRICE_OVERRIDE')");
        r.CanApproveDiscount=await c.ExecuteScalarAsync<bool>("SELECT public.sf_transfer_permission('SALES','DISCOUNT_APPROVE')");
        r.CanTradeIn=false;r.AllocateInvoiceDiscount=true;
    }
    public async Task<SaleQuote> PreviewAsync(CreateSaleRequest request)
    {
        await using var c=factory.CreateConnection();await Require(c,"VIEW");
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_quotation_allowed('CREATE') OR public.sf_quotation_allowed('EDIT')"))throw new UnauthorizedAccessException("Quotation editing permission required.");
        await Prepare(c,request);return await sales.QuoteAsync(request);
    }
    public async Task<long> SaveAsync(SaveQuotationRequest r)
    {
        var input=JsonSerializer.SerializeToElement(r);
        r=JsonSerializer.Deserialize<SaveQuotationRequest>(input)!;
        if(r.QuotationId==Guid.Empty || r.OperationId==Guid.Empty || r.ExpectedVersion<0 || r.Request is null || r.DocumentDate.TimeOfDay!=TimeSpan.Zero || r.ValidUntil.TimeOfDay!=TimeSpan.Zero || r.DocumentDate.Year<2000 || r.DocumentDate.Date>time.Value.Today(clock) || r.ValidUntil<r.DocumentDate || r.ValidUntil.Year>2100 || r.Reference?.Length>150 || r.Terms?.Length>4000)
            throw new ValidationException("Choose valid quotation identifiers, dates, reference and terms.");
        if(r.StoreId.HasValue && r.StoreId!=factory.Scope?.StoreId)throw new UnauthorizedAccessException("The selected quotation shop changed.");
        await using var c=factory.CreateConnection();await Require(c,r.ExpectedVersion==0?"CREATE":"EDIT");
        var replay=await c.ExecuteScalarAsync<long?>("SELECT public.sf_quotation_saved_result(@QuotationId,@ExpectedVersion,@OperationId,@input::jsonb)",new{r.QuotationId,r.ExpectedVersion,r.OperationId,input=input.GetRawText()});
        if(replay.HasValue)return replay.Value;
        await Prepare(c,r.Request);
        if(r.Request.CustomerId is null)throw new ValidationException("Select an active customer for this quotation.");
        // Prices, tax and reductions always come from the existing billing engine.
        var quote=await sales.QuoteAsync(r.Request);
        if(r.Request.Items.Where((item,index)=>item.ManualItem is null&&item.SellingPrice!=quote.Items[index].CatalogPrice).Any()&&!r.Request.CanOverridePrice)
            throw new UnauthorizedAccessException("The existing sales price-override permission is required for a quoted price adjustment.");
        if((r.Request.DiscountAmount>0||r.Request.Items.Any(item=>item.DiscountAmount>0))&&!r.Request.CanDiscount)
            throw new UnauthorizedAccessException("The existing sales discount permission is required to save quotation discounts.");
        var policy=await BillingPolicyStore.ReadAsync(c);
        policy.ValidateAdjustments(r.Request,quote.AdjustmentReview!,true);
        var customer=await c.QuerySingleOrDefaultAsync<CustomerSnapshot>("SELECT customer_name CustomerName,mobile_number MobileNumber,address Address,gst_number GstNumber FROM customers WHERE customer_id=@id AND is_active",new{id=r.Request.CustomerId})
            ??throw new ValidationException("Choose an active customer visible in this setup.");
        var company=await new CompanySettingsRepository(factory).GetAsync();
        var lines=new List<object>();
        for(var i=0;i<r.Request.Items.Count;i++)
        {
            var item=r.Request.Items[i];
            var identity=item.ManualItem is {} manual?new LineIdentity{Name=manual.Name,Unit="NOS"}:
                await c.QuerySingleOrDefaultAsync<LineIdentity>("SELECT p.product_name||' / '||v.variant_name Name,v.sku Sku,p.unit_of_measure Unit FROM products p JOIN product_variants v USING(tenant_id,product_id) WHERE p.product_id=@ProductId AND v.variant_id=@VariantId AND p.is_active AND v.is_active",item)
                ??throw new ValidationException("Select an active exact SKU for each quotation line.");
            lines.Add(new {identity.Name,identity.Sku,identity.Unit,item.Quantity,item.SellingPrice,item.DiscountPercent,item.ManualItem,Calculation=quote.Items[i]});
        }
        var preparedBy=await c.ExecuteScalarAsync<string>("SELECT full_name FROM users WHERE user_id=public.sf_actor()");
        var shop=await c.QuerySingleAsync<ShopSnapshot>("SELECT s.store_name StoreName,l.location_name LocationName FROM store_locations l JOIN stores s USING(tenant_id,store_id) WHERE l.location_id=public.sf_location() AND s.store_id=public.sf_store()");
        var body=JsonSerializer.Serialize(new{input,shop,documentDate=r.DocumentDate.ToString("yyyy-MM-dd"),validUntil=r.ValidUntil.ToString("yyyy-MM-dd"),reference=r.Reference?.Trim(),terms=r.Terms?.Trim(),request=r.Request,quote,customer,company,preparedBy,policyStamp=policy.Stamp,lines});
        return await c.ExecuteScalarAsync<long>("SELECT public.sf_quotation_write(@QuotationId,@ExpectedVersion,@OperationId,'SAVE',@body::jsonb)",new{r.QuotationId,r.ExpectedVersion,r.OperationId,body});
    }
    public async Task<QuotationPage> ListAsync(string? term,string? status,DateTime? from,DateTime? to,int page,int pageSize)
    {
        if(page<1 || page>100000 || pageSize is <1 or >100 || term?.Length>150 || from>to || (from.HasValue&&(from.Value.Year<2000||from.Value.Year>2100||from.Value.TimeOfDay!=TimeSpan.Zero)) || (to.HasValue&&(to.Value.Year<2000||to.Value.Year>2100||to.Value.TimeOfDay!=TimeSpan.Zero)))throw new ValidationException("Choose valid search, dates and pagination.");
        var states=new[]{"DRAFT","SENT","ACCEPTED","REJECTED","EXPIRED","CONVERTED","CANCELLED"};
        if(!string.IsNullOrWhiteSpace(status)&&!states.Contains(status))throw new ValidationException("Choose a supported quotation status.");
        await using var c=factory.CreateConnection();await Require(c,"VIEW");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await c.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:tx);
        const string filter=" WHERE (@term::text IS NULL OR \"QuotationNumber\" ILIKE @term OR \"CustomerName\" ILIKE @term OR \"CustomerMobile\" ILIKE @term) AND (@status::text IS NULL OR \"Status\"=@status) AND (@from::date IS NULL OR \"DocumentDate\">=@from::date) AND (@to::date IS NULL OR \"DocumentDate\"<=@to::date)";
        // PostgreSQL folds unquoted aliases from Header to lower case.
        // Read mobile from the current saved revision under the same RLS scope.
        // Never join a mutable/shared customer master to supply another shop's data.
        var query="WITH filtered AS (SELECT h.quotationid AS \"QuotationId\",quotationnumber AS \"QuotationNumber\",customername AS \"CustomerName\",coalesce(r.body->'customer'->>'MobileNumber',r.body->'customer'->>'mobileNumber') AS \"CustomerMobile\",status AS \"Status\",documentdate AS \"DocumentDate\",validuntil AS \"ValidUntil\",total AS \"Total\",itemcount AS \"ItemCount\",currentrevision AS \"CurrentRevision\",version AS \"Version\",saleid AS \"SaleId\" FROM ("+Header+") h LEFT JOIN quotation_revisions r ON r.tenant_id=public.sf_tenant() AND r.quotation_id=h.quotationid AND r.revision=h.currentrevision) ";
        var args=new{term=string.IsNullOrWhiteSpace(term)?null:"%"+term.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%",status=string.IsNullOrWhiteSpace(status)?null:status,from,to,offset=(page-1)*pageSize,pageSize};
        var rows=(await c.QueryAsync<QuotationRow>(query+"SELECT * FROM filtered"+filter+" ORDER BY \"DocumentDate\" DESC,\"QuotationNumber\" DESC LIMIT @pageSize OFFSET @offset",args,tx)).ToArray();
        var total=await c.ExecuteScalarAsync<long>(query+"SELECT count(*) FROM filtered"+filter,args,tx);await tx.CommitAsync();return new(rows,total,page,pageSize);
    }
    public async Task<QuotationDetail> GetAsync(Guid id,int? revision=null)
    {
        await using var c=factory.CreateConnection();await Require(c,"VIEW");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var row=await c.QuerySingleOrDefaultAsync<QuotationRow>(Header+" WHERE q.quotation_id=@id",new{id},tx)??throw new KeyNotFoundException("Quotation not found in the selected shop/location.");
        var selected=await c.QuerySingleOrDefaultAsync<RevisionData>("SELECT r.revision Revision,r.body::text Body,r.body->>'preparedBy' PreparedBy,r.created_at CreatedAt FROM quotation_revisions r WHERE quotation_id=@id AND revision=@revision",new{id,revision=revision??row.CurrentRevision},tx)??throw new KeyNotFoundException("Quotation revision not found.");
        var revisions=(await c.QueryAsync<int>("SELECT revision FROM quotation_revisions WHERE quotation_id=@id ORDER BY revision DESC",new{id},tx)).ToArray();
        var history=(await c.QueryAsync<QuotationEvent>("SELECT event_id EventId,revision Revision,action Action,notes Notes,coalesce(u.full_name,'Staff') Actor,e.created_at CreatedAt FROM quotation_events e LEFT JOIN users u ON u.tenant_id=e.tenant_id AND u.user_id=e.actor_id WHERE quotation_id=@id ORDER BY event_id",new{id},tx)).ToArray();
        await tx.CommitAsync();return new(row,new(selected.Revision,JsonSerializer.Deserialize<JsonElement>(selected.Body),selected.PreparedBy,selected.CreatedAt),revisions,history);
    }
    public async Task<long> StateAsync(Guid id,QuotationStateRequest r)
    {
        if(id==Guid.Empty||r.OperationId==Guid.Empty||r.ExpectedVersion<1||r.Notes is null||r.Notes.Length>1000||!new[]{"SENT","ACCEPTED","REJECTED","EXPIRED","CANCELLED"}.Contains(r.Status))throw new ValidationException("Enter a valid lifecycle request and confirmation notes.");
        await using var c=factory.CreateConnection();await Require(c,"STATUS");
        return await c.ExecuteScalarAsync<long>("SELECT public.sf_quotation_write(@id,@ExpectedVersion,@OperationId,'STATE',@body::jsonb)",new{id,r.ExpectedVersion,r.OperationId,body=JsonSerializer.Serialize(new{status=r.Status,notes=r.Notes.Trim()})});
    }
    public async Task<object> CheckoutAsync(Guid id,QuotationCheckoutRequest r)
    {
        await using var c=factory.CreateConnection();await Require(c,"CONVERT");
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_transfer_permission('SALES','CREATE')"))throw new UnauthorizedAccessException("New Sale permission is required to convert a quotation.");
        var d=await GetAsync(id,r.Revision);
        if(d.Header.Version!=r.ExpectedVersion||d.Header.CurrentRevision!=r.Revision||d.Header.Status!="ACCEPTED")throw new ValidationException("Only the current accepted, unexpired quotation revision can open in checkout. Reload the quotation.");
        var request=d.Selected.Body.GetProperty("request").Deserialize<CreateSaleRequest>()!;
        await Prepare(c,request);request.RequestId=Guid.NewGuid();
        // A new checkout uses today's catalog prices. Deliberate quoted price overrides retain their reason and require current authority.
        var current=await sales.QuoteAsync(request);var changes=new List<string>();
        var original=d.Selected.Body.GetProperty("quote").Deserialize<SaleQuote>()!;
        for(var i=0;i<request.Items.Count;i++)
        {
            var line=request.Items[i];
            if(line.ManualItem is null && line.SellingPrice==original.Items[i].CatalogPrice && line.SellingPrice!=current.Items[i].CatalogPrice){line.SellingPrice=current.Items[i].CatalogPrice;changes.Add($"Line {i+1}: catalog price changed; current price loaded.");}
            if(current.Items[i].TaxPercentage!=original.Items[i].TaxPercentage)changes.Add($"Line {i+1}: effective tax changed; checkout will recalculate.");
        }
        current=await sales.QuoteAsync(request);
        foreach(var group in request.Items.Select((line,i)=>(line,i)).Where(x=>x.line.ManualItem is null).GroupBy(x=>x.line.VariantId))
            if(group.Sum(x=>x.line.Quantity)>current.Items[group.First().i].CurrentStock)throw new ValidationException("Insufficient stock for this quotation. Receive stock or create a revised quotation; no stock was reserved.");
        var customer=await c.QuerySingleOrDefaultAsync<CustomerSnapshot>("SELECT customer_name CustomerName,mobile_number MobileNumber,address Address,gst_number GstNumber FROM customers WHERE customer_id=@CustomerId AND is_active",request)??throw new ValidationException("Quotation customer is inactive or unavailable.");
        var cart=new List<object>();
        for(var i=0;i<request.Items.Count;i++)
        {
            var item=request.Items[i];var source=d.Selected.Body.GetProperty("lines")[i];var calculation=current.Items[i];
            var serial=item.ManualItem is null&&await c.ExecuteScalarAsync<bool>("SELECT is_serial_tracked FROM product_variants WHERE variant_id=@VariantId",item);
            if(serial&&!item.ProductUnitId.HasValue)throw new ValidationException("This quotation needs an exact available serial. Revise the quotation to select the serial before conversion.");
            cart.Add(new {item.ProductId,item.VariantId,item.ProductUnitId,item.ManualItem,item.Quantity,item.SellingPrice,item.DiscountAmount,item.DiscountPercent,item.AdjustmentReason,item.PriceAdjustmentReason,item.TaxAdjustmentReason,item.TaxOverride,
                ProductName=source.GetProperty("Name").GetString(),Sku=source.GetProperty("Sku").GetString(),calculation.Mrp,calculation.CatalogPrice,calculation.TaxPercentage,CatalogTaxPercentage=calculation.TaxPercentage,calculation.CurrentStock,IsSerialTracked=serial});
        }
        return new{quotationNumber=d.Header.QuotationNumber,changes,snapshot=new{sourceQuotationId=id,sourceQuotationRevision=r.Revision,sourceQuotationVersion=r.ExpectedVersion,sourceQuotationNumber=d.Header.QuotationNumber,sourceQuotationNotice=string.Join(" ",changes),
            draftId=request.RequestId,draftRevision=0,savedAt=clock.GetUtcNow().ToUnixTimeMilliseconds(),cart,payments=Array.Empty<object>(),split=false,customerId=request.CustomerId,customerLabel=customer.CustomerName,discount=request.DiscountAmount,notes=request.Notes,mode="CASH",reason=request.AdjustmentReason,
            request.PricesIncludeTax,request.GstPlaceOfSupplyStateCode,request.GstDeliveryAddress,request.GstRecipientDetailsRequested}};
    }
    private sealed class CustomerSnapshot { public string CustomerName {get;set;}="";public string? MobileNumber {get;set;}public string? Address {get;set;}public string? GstNumber {get;set;} }
    private sealed class ShopSnapshot { public string StoreName {get;set;}="";public string LocationName {get;set;}=""; }
    private sealed class LineIdentity { public string Name {get;set;}="";public string? Sku {get;set;}public string Unit {get;set;}="NOS"; }
    private sealed class RevisionData { public int Revision {get;set;}public string Body {get;set;}="";public string PreparedBy {get;set;}="";public DateTimeOffset CreatedAt {get;set;} }
}
