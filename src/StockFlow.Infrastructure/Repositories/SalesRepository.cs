using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public partial class SalesRepository : ISalesRepository
{
    private readonly DbConnectionFactory factory;
    private readonly ShopTimeOptions shopTime;
    private readonly TimeProvider clock;
    // ASP.NET supplies these services when it creates this class.
    public SalesRepository(DbConnectionFactory factory, Microsoft.Extensions.Options.IOptions<ShopTimeOptions>? time = null, TimeProvider? clock = null)
    {
        this.factory = factory;
        this.shopTime = time?.Value ?? new();
        this.clock = clock ?? TimeProvider.System;
    }

    private const string Header = """
        SELECT s.sale_id AS SaleId, s.invoice_number AS InvoiceNumber, s.customer_id AS CustomerId,
            COALESCE(s.customer_name_snapshot, c.customer_name) AS CustomerName, s.sale_date AT TIME ZONE current_setting('TimeZone') AS SaleDate,

            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (SELECT coalesce(sum(i.quantity*i.selling_price-i.discount_amount),0) FROM sale_items i WHERE i.sale_id=s.sale_id AND i.tax_percentage>0) END AS TaxableAmount,
            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (SELECT sum((i->>'Quantity')::numeric*(i->>'SellingPrice')::numeric) FROM jsonb_array_elements(s.request_payload->'Items') i) END AS ItemSubtotal,
            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (SELECT sum((i->>'DiscountAmount')::numeric) FROM jsonb_array_elements(s.request_payload->'Items') i) END AS ItemDiscountAmount,
            CASE WHEN s.request_payload->>'AllocateInvoiceDiscount'='true' THEN
                (s.request_payload->>'DiscountAmount')::numeric-coalesce((s.request_payload->'TradeIn'->>'AllowanceAmount')::numeric,0) END AS InvoiceDiscountAmount,
            s.subtotal AS Subtotal, s.discount_amount AS DiscountAmount, s.tax_amount AS TaxAmount,
            s.total_amount AS TotalAmount, s.status AS Status, s.notes AS Notes, u.full_name AS CreatedByName
        FROM sales s LEFT JOIN customers c ON c.customer_id = s.customer_id JOIN users u ON u.user_id = s.created_by
        """;
    public async Task<IEnumerable<SaleResponse>> GetAllAsync(string? term, int? customerId, int offset, DateTime? from = null, DateTime? to = null)
    {
        await using var connection = factory.CreateConnection();
        return await connection.QueryAsync<SaleResponse>(Header + """
             WHERE (@Term IS NULL OR s.invoice_number ILIKE @Term OR COALESCE(s.customer_name_snapshot,c.customer_name) ILIKE @Term
                OR (CASE WHEN s.invoice_snapshot_version > 0 THEN s.customer_mobile_snapshot ELSE c.mobile_number END) ILIKE @Term)
                AND (@CustomerId IS NULL OR s.customer_id = @CustomerId)
                AND (CAST(@FromDate AS date) IS NULL OR s.sale_date >= CAST(@FromDate AS date))
                AND (CAST(@ToDate AS date) IS NULL OR s.sale_date < CAST(@ToDate AS date) + INTERVAL '1 day')
            ORDER BY s.sale_date DESC, s.sale_id DESC LIMIT 100 OFFSET @Offset;
            """,
new
{
    Term = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim() + "%",
    CustomerId = customerId,
    Offset = offset,
    FromDate = from?.Date,
    ToDate = to?.Date
});
    }

    public async Task<SaleResponse?> GetByIdAsync(long id)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var result = await connection.QuerySingleOrDefaultAsync<SaleResponse>(Header + " WHERE s.sale_id = @Id;", new
        {
            Id = id
        }, transaction);
        if (result is null)
        {
            return null;
        }

        result.Lines = (await connection.QueryAsync<SaleItemResponse>("""
            SELECT coalesce((to_jsonb(i)->>'is_manual')::boolean,false) IsManual,(to_jsonb(i)->>'mrp_snapshot')::numeric Mrp,
                coalesce(to_jsonb(i)->>'discount_type','FIXED') DiscountType,(to_jsonb(i)->>'discount_percent')::numeric DiscountPercent,
                to_jsonb(i)->>'description_snapshot' Description,to_jsonb(i)->'manual_tax_snapshot'->>'hsnSac' HsnSac,
                to_jsonb(i)->'manual_tax_snapshot'->>'treatment' GstTreatment,(to_jsonb(i)->'manual_tax_snapshot'->>'ruleId')::bigint TaxRuleId,
                i.sale_item_id AS SaleItemId, i.product_id AS ProductId, i.variant_id AS VariantId, i.product_unit_id AS ProductUnitId,
                COALESCE(i.product_name_snapshot,p.product_name) || CASE WHEN i.variant_name_snapshot IS NULL OR i.variant_name_snapshot='DEFAULT' THEN '' ELSE ' / ' || i.variant_name_snapshot END AS ProductName,
                COALESCE(to_jsonb(pu)->>'serial_source','LEGACY_UNKNOWN') AS SerialSource, COALESCE(i.serial_number_snapshot,pu.serial_number) AS SerialNumber, i.quantity AS Quantity,
                COALESCE(to_jsonb(i)->>'variant_attributes_snapshot','{}') AS AttributesJson,to_jsonb(i)->>'sku_snapshot' AS Sku,
                COALESCE(to_jsonb(i)->>'unit_of_measure_snapshot',p.unit_of_measure) AS UnitOfMeasure,
                i.selling_price AS SellingPrice, i.discount_amount AS DiscountAmount, i.tax_percentage AS TaxPercentage,
                i.line_total AS LineTotal, w.warranty_start_date AS WarrantyStartDate, w.warranty_end_date AS WarrantyEndDate,
                w.warranty_status AS WarrantyStatus
            FROM sale_items i LEFT JOIN products p ON p.product_id = i.product_id
            LEFT JOIN product_units pu ON pu.product_unit_id = i.product_unit_id
            LEFT JOIN LATERAL (SELECT * FROM warranties WHERE sale_item_id = i.sale_item_id ORDER BY warranty_id LIMIT 1) w ON TRUE
            WHERE i.sale_id = @Id ORDER BY i.sale_item_id;
            """,
new
{
    Id = id
},
transaction)).ToList();
        result.Payments = (await connection.QueryAsync<PaymentResponse>("SELECT payment_date AT TIME ZONE current_setting('TimeZone') AS PaymentDate, payment_mode AS PaymentMode, amount AS Amount, reference_number AS ReferenceNumber, CASE WHEN payment_mode='CARD' THEN coalesce(request_payload->>'cardType',request_payload->>'CardType') END AS CardType FROM payments WHERE sale_id = @Id ORDER BY payment_id;",
new
{
    Id = id
},
transaction)).ToList();
        await transaction.CommitAsync();
        return result;
    }

    public async Task<SalesListPage> GetListAsync(string? term,int? customerId,int offset,int limit,DateTime? from,DateTime? to,string? status,string sort="date",bool ascending = false, string? paymentStatus = null, string? paymentMethod = null)
    {
        // Invoice-date cohort with current ledger settlements. Payments and returns
        // are aggregated separately, so multiple lines/receipts never multiply totals.
        const string filtered = """
            WITH balances AS (
            SELECT s.sale_id AS SaleId,s.invoice_number AS InvoiceNumber,s.sale_date AT TIME ZONE current_setting('TimeZone') AS SaleDate,
              s.customer_id AS CustomerId,
              COALESCE(s.customer_name_snapshot,c.customer_name) AS CustomerName,
              CASE WHEN s.invoice_snapshot_version>0 THEN s.customer_mobile_snapshot ELSE c.mobile_number END AS CustomerMobile,
              s.total_amount AS TotalAmount,s.status AS Status,s.payment_due_date AS PaymentDueDate,
              CASE WHEN s.status='COMPLETED' THEN greatest(s.total_amount-r.returned-p.paid+r.refunded,0) ELSE 0 END AS OutstandingAmount,
              p.paid-r.refunded AS CollectedAmount,r.refunded AS RefundedAmount,
              (SELECT count(*) FROM sale_items i WHERE i.sale_id=s.sale_id) AS LineCount,
              COALESCE((SELECT sum(i.quantity) FROM sale_items i WHERE i.sale_id=s.sale_id),0)::bigint AS ItemCount,
              CASE WHEN cardinality(p.methods)>1 THEN 'MIXED' ELSE coalesce(p.methods[1],'CREDIT') END AS PaymentMode,
              p.methods AS PaymentMethods,r.returned AS ReturnedAmount,
              CASE WHEN s.status<>'COMPLETED' THEN s.status WHEN r.returned>=s.total_amount AND r.returned>0 THEN 'FULLY_RETURNED'
                WHEN r.returned>0 THEN 'PARTIALLY_RETURNED' ELSE s.status END AS InvoiceStatus
            FROM sales s LEFT JOIN customers c ON c.customer_id=s.customer_id
            CROSS JOIN LATERAL (
              SELECT coalesce(sum(amount) FILTER(WHERE payment_mode<>'CREDIT'),0) paid,
                array_agg(DISTINCT CASE WHEN payment_mode='CARD' AND coalesce(request_payload->>'cardType',request_payload->>'CardType') IN ('DEBIT','CREDIT')
                  THEN coalesce(request_payload->>'cardType',request_payload->>'CardType')||'_CARD'
                  WHEN payment_mode='BANK' THEN 'BANK_TRANSFER' ELSE payment_mode END)
                  FILTER(WHERE payment_mode<>'CREDIT' AND amount>0) methods
              FROM payments WHERE sale_id=s.sale_id
            ) p
            CROSS JOIN LATERAL (
              SELECT coalesce(sum(return_total),0) returned,coalesce(sum(refund_amount),0) refunded FROM sale_returns WHERE sale_id=s.sale_id
            ) r
            WHERE (@Term IS NULL OR s.invoice_number ILIKE @Term OR COALESCE(s.customer_name_snapshot,c.customer_name) ILIKE @Term
              OR (CASE WHEN s.invoice_snapshot_version>0 THEN s.customer_mobile_snapshot ELSE c.mobile_number END) ILIKE @Term
              OR EXISTS(SELECT 1 FROM sale_items i LEFT JOIN products pr ON pr.product_id=i.product_id
                LEFT JOIN product_variants v ON v.variant_id=i.variant_id WHERE i.sale_id=s.sale_id AND (
                coalesce(i.product_name_snapshot,pr.product_name) ILIKE @Term OR i.variant_name_snapshot ILIKE @Term
                OR coalesce(to_jsonb(i)->>'sku_snapshot',v.sku,pr.sku) ILIKE @Term
                OR pr.parent_product_code ILIKE @Term OR pr.model ILIKE @Term OR i.serial_number_snapshot ILIKE @Term
                OR (coalesce(to_jsonb(i)->'variant_attributes_snapshot',v.attributes)->>'manufacturer_part_number') ILIKE @Term)))
              AND (@CustomerId IS NULL OR s.customer_id=@CustomerId)
              AND s.status<>'DRAFT'
              AND (@FromDate::timestamp IS NULL OR s.sale_date>=@FromDate::timestamp)
              AND (@ToDate::timestamp IS NULL OR s.sale_date<@ToDate::timestamp)
            ), settled AS (
              SELECT *,CASE WHEN Status<>'COMPLETED' OR TotalAmount-ReturnedAmount<=0 THEN 'ADJUSTED'
                WHEN OutstandingAmount=0 THEN 'PAID' WHEN CollectedAmount>0 THEN 'PARTIAL' ELSE 'UNPAID' END AS PaymentStatus FROM balances
            ) SELECT * FROM settled WHERE
              (@Status IS NULL OR Status=@Status OR InvoiceStatus=@Status OR (@Status IN ('OUTSTANDING','OVERDUE')
                AND Status='COMPLETED' AND CustomerId IS NOT NULL AND OutstandingAmount>0
                AND (@Status='OUTSTANDING' OR PaymentDueDate<@Today)))
              AND (@PaymentStatus IS NULL OR PaymentStatus=@PaymentStatus)
              AND (@PaymentMethod IS NULL OR @PaymentMethod=ANY(PaymentMethods)
                OR (@PaymentMethod='MIXED' AND PaymentMode='MIXED') OR (@PaymentMethod='CREDIT' AND PaymentMode='CREDIT'))
            """;
        const string aggregate = """
            SELECT count(*) AS Count,COALESCE(sum(TotalAmount) FILTER(WHERE Status='COMPLETED'),0) AS TotalSales,
              COALESCE(avg(TotalAmount) FILTER(WHERE Status='COMPLETED'),0) AS AverageValue,
              COALESCE(sum(ReturnedAmount),0) AS ReturnedAmount,
              count(*) FILTER(WHERE Status='COMPLETED') AS InvoiceCount,
              COALESCE(sum(TotalAmount-ReturnedAmount) FILTER(WHERE Status='COMPLETED'),0) AS NetSales,
              COALESCE(sum(CollectedAmount) FILTER(WHERE Status='COMPLETED'),0) AS AmountCollected,
              COALESCE(sum(OutstandingAmount) FILTER(WHERE Status='COMPLETED'),0) AS OutstandingDues FROM filtered;
            """;
        await using var connection=factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(shopTime.TimeZoneId);
        // sale_date is a legacy timestamp stored in the database session timezone.
        DateTime? Boundary(DateTime? date)=>date.HasValue ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.Value.Date,DateTimeKind.Unspecified),zone) : null;
        // Convert UTC boundaries to the database timezone, matching cash closing's interpretation.
        object Parameters(DateTime? start,DateTime? end)=>new {Term=string.IsNullOrWhiteSpace(term)?null:"%"+term.Trim()+"%",CustomerId=customerId,Status=status,PaymentStatus=paymentStatus,PaymentMethod=paymentMethod,
            FromDate=Boundary(start),ToDate=end?.Date==DateTime.MaxValue.Date?null:Boundary(end?.Date.AddDays(1)),Today=shopTime.Today(clock),Offset=offset,Limit=limit};
        var column=sort switch {"invoice"=>"InvoiceNumber","customer"=>"CustomerName","items"=>"ItemCount","total"=>"TotalAmount","payment"=>"PaymentMode","status"=>"InvoiceStatus","paid"=>"CollectedAmount","due"=>"OutstandingAmount","paymentStatus"=>"PaymentStatus",_=>"SaleDate"};
        var order=column+(ascending?" ASC":" DESC")+",SaleId DESC";
        using var result=await connection.QueryMultipleAsync("WITH filtered AS ("+filtered+") SELECT * FROM filtered ORDER BY "+order+" LIMIT @Limit OFFSET @Offset; WITH filtered AS ("+filtered+") "+aggregate,Parameters(from,to),transaction);
        var page=new SalesListPage {Rows=(await result.ReadAsync<SalesListRow>()).ToList(),Summary=await result.ReadSingleAsync<SalesListSummary>(),TimeZoneId=shopTime.TimeZoneId};
        if(from.HasValue&&to.HasValue){var days=(to.Value.Date-from.Value.Date).Days+1;if(from.Value.Date>DateTime.MinValue.AddDays(days))page.Previous=await connection.QuerySingleAsync<SalesListSummary>("WITH filtered AS ("+filtered+") "+aggregate,Parameters(from.Value.Date.AddDays(-days),from.Value.Date.AddDays(-1)),transaction);}
        await transaction.CommitAsync();
        return page;
    }

    private static void ValidateTaxMode(CreateSaleRequest request, CompanySettings company)
    {
        var inclusive = company.DefaultTaxMode == "INCLUSIVE";
        if (request.PricesIncludeTax.HasValue && request.PricesIncludeTax.Value != inclusive)
            throw new ValidationException("The shop tax mode has changed. Reopen New sale and check item prices before billing.");
        request.PricesIncludeTax = inclusive;
    }

    public async Task<SaleQuote> QuoteAsync(CreateSaleRequest request)
    {
        SaleRules.Validate(request, false);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock_shared(hashtextextended('billing-policy:'||public.sf_tenant()::text||':'||public.sf_store()::text,0))",transaction:transaction);
        var quoteCompany = await connection.QuerySingleAsync<CompanySettings>(CompanySettingsRepository.ReadSql, transaction:transaction);
        ValidateTaxMode(request, quoteCompany);
        var taxes = new List<decimal>();
        var resolved = new List<StockFlow.Application.DTOs.Products.VariantResponse>();
        foreach (var item in request.Items)
        {
            if (item.ManualItem is not null)
            {
                var manual = await ResolveManualAsync(connection, transaction, item, request.PricesIncludeTax == true);
                resolved.Add(manual.Variant); taxes.Add(manual.Variant.TaxPercentage); continue;
            }
            var variant = await VariantRepository.ResolveAsync(connection, transaction, item.ProductId, item.VariantId, item.ProductUnitId, requireExplicitVariant: factory.RequiresExplicitRetailVariant);
            if (item.ProductUnitId.HasValue && !await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE product_unit_id=@id AND variant_id=@variantId AND unit_status='IN_STOCK' AND sale_id IS NULL)",
new
{
    id = item.ProductUnitId,
    variantId = variant.VariantId
},
transaction))
            {
                throw new ValidationException("A selected serial is no longer available. Remove it and select another unit.");
            }

            resolved.Add(variant);
            variant.TaxPercentage = await ResolveLineTaxAsync(connection,transaction,request,item,variant);
            taxes.Add(variant.TaxPercentage);
        }

        var quote = SaleRules.CalculateLines(request, taxes, false);
        var mapped = false;
        string? registrationType = null, supplierState = null;
        if (await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_gst_sale_context()') IS NOT NULL", transaction: transaction))
        {
            string contextJson;
            try { contextJson = (await connection.ExecuteScalarAsync<string>("SELECT public.sf_gst_sale_context()::text", transaction: transaction))!; }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation) { throw new ValidationException(ex.MessageText); }
            using var context = JsonDocument.Parse(contextJson);
            mapped = context.RootElement.GetProperty("mapped").GetBoolean();
            if (mapped)
            {
                registrationType = context.RootElement.GetProperty("registrationType").GetString();
                supplierState = context.RootElement.GetProperty("stateCode").GetString();
            }
        }
        quote.TaxBreakdown = GstBreakdown.ForQuote(quote, mapped, registrationType, supplierState, request.GstPlaceOfSupplyStateCode);
        for (var index = 0; index < resolved.Count; index++)
        {
            var variant = resolved[index];
            var line = quote.Items[index];
            line.Mrp = variant.Mrp > 0 ? variant.Mrp : null;
            CheckMrp(line, request.Items[index]);
            line.CurrentStock = request.Items[index].ManualItem is not null ? 0 : await connection.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@id",
new
{
    id = variant.VariantId
},
transaction);
            line.CatalogPrice = variant.SellingPrice;
            line.WarrantyMonths = variant.WarrantyMonths;
            line.Model = variant.Model;
        }

        var policy=await BillingPolicyStore.ReadAsync(connection,transaction);
        var review=policy.Evaluate(request,quote,resolved.Select(v=>v.SellingPrice).ToArray());
        policy.ValidateAdjustments(request,review,false);
        var approvalStatus="NOT_REQUIRED";
        if(review.RequiresApproval){
            var rejected=policy.Version>0?"WHEN rejected_at IS NOT NULL THEN 'REJECTED' ":"";
            approvalStatus=await connection.QuerySingleOrDefaultAsync<string>("SELECT CASE WHEN financial_hash<>@hash THEN 'STALE' "+rejected+"WHEN approved_at>now()-interval '30 minutes' AND approved_by<>requested_by THEN 'APPROVED' WHEN approved_at IS NOT NULL THEN 'EXPIRED' WHEN requested_at>now()-interval '1 day' THEN 'PENDING' ELSE 'EXPIRED' END FROM billing_approvals WHERE draft_id=@RequestId AND requested_by=public.sf_actor()",new{request.RequestId,hash=BillingApproval.Hash(request,policy.Stamp,policy.Version>0?quote:null)},transaction)??"REQUIRED";
        }
        var approvingUser=approvalStatus=="APPROVED"?await connection.QuerySingleOrDefaultAsync<string>("SELECT u.full_name FROM billing_approvals a JOIN users u ON u.tenant_id=a.tenant_id AND u.user_id=a.approved_by WHERE a.draft_id=@RequestId AND a.requested_by=public.sf_actor()",new{request.RequestId},transaction):null;
        quote.AdjustmentReview=review with {ApprovalStatus=approvalStatus,ApprovingUser=approvingUser};
        await transaction.CommitAsync();
        return quote;
    }

    public async Task<long> CreateSaleAsync(CreateSaleRequest request, int userId)
    {
        factory.RequireLocation(request.LocationId);
        SaleRules.Validate(request, true);
        if(request.PaymentDueDate is {} dueDate && (dueDate.TimeOfDay!=TimeSpan.Zero || dueDate.Date<shopTime.Today(clock) || request.CustomerId is null || !request.Payments.Any(p=>p.PaymentMode=="CREDIT" && p.Amount>0)))
            throw new ValidationException("A payment due date must be today or later and requires a named customer with a credit payment.");
        var payload = JsonSerializer.Serialize(request);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var draftLifecycle=await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_draft_checkout(uuid,uuid,bigint)') IS NOT NULL",transaction:transaction);
        if(draftLifecycle)
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0))",transaction:transaction);
        await factory.LockBusinessProfileAsync(connection, transaction);
        // Shared with other postings, exclusive against GST configuration changes.
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock_shared(hashtextextended('gst-config:'||public.sf_tenant()::text,0))",transaction:transaction);
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@Key, 0));",
new
{
    Key = "StockFlow.Sale:" + request.RequestId
},
transaction);
        var prior = await connection.QuerySingleOrDefaultAsync<SaleReceipt>("""
            SELECT sale_id AS SaleId, created_by AS CreatedBy, jsonb_strip_nulls(request_payload) = jsonb_strip_nulls(CAST(@Payload AS jsonb)) AS SamePayload
            FROM sales WHERE request_id = @RequestId;
            """,
new
{
    request.RequestId,
    Payload = payload
},
transaction);
        if (prior is not null)
        {
            if (!prior.SamePayload || prior.CreatedBy != userId)
            {
                throw new ValidationException("This request ID already belongs to a different sale.");
            }

            await transaction.CommitAsync();
            return prior.SaleId;
        }

        if(draftLifecycle)
        {
            try{await connection.ExecuteAsync("SELECT public.sf_draft_checkout(@RequestId,@DraftEditorId,@DraftRevision)",new{request.RequestId,request.DraftEditorId,request.DraftRevision},transaction);}
            catch(Npgsql.PostgresException e)when(e.SqlState=="P0001"){throw new ValidationException(e.MessageText);}
        }
        if(request.SourceQuotationId.HasValue && !await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_quotation_sale_link()') IS NOT NULL",transaction:transaction))
            throw new ValidationException("Quotation checkout requires database migration 083. No sale was posted.");
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock_shared(hashtextextended('billing-policy:'||public.sf_tenant()::text||':'||public.sf_store()::text,0))",transaction:transaction);
        SaleRules.RequireCollectedPayments(request);
        if(!await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_pos_manual_tax(bigint)') IS NOT NULL",transaction:transaction))
            throw new ValidationException("This New Sale release requires database migration 074. Ask the administrator to apply it before checkout.");
        var collection=request.Payments.Single();
        if(collection.PaymentMode!="CASH" && !string.IsNullOrWhiteSpace(collection.ReferenceNumber))
        {
            var reference=collection.ReferenceNumber!.Trim().ToUpperInvariant();
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('sale-payment:'||public.sf_tenant()::text||':'||public.sf_store()::text||':'||@Mode||':'||@Reference,0))",new{Mode=collection.PaymentMode,Reference=reference},transaction);
            if(await connection.ExecuteScalarAsync<bool>("SELECT public.sf_pos_payment_reference_used(@Mode,@Reference)",new{Mode=collection.PaymentMode,Reference=reference},transaction))
                throw new ValidationException("This transaction reference is already recorded on an invoice in this shop.");
        }
        InvoiceCustomer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await connection.QuerySingleOrDefaultAsync<InvoiceCustomer>("SELECT customer_name AS Name,mobile_number AS Mobile,address AS Address,gst_number AS Gst FROM customers WHERE customer_id = @CustomerId AND is_active FOR UPDATE;",
new
{
    request.CustomerId
},
transaction);
            if (customer is null)
            {
                throw new ValidationException("Choose an active customer.");
            }
        }

        var company = await connection.QuerySingleAsync<CompanySettings>(CompanySettingsRepository.ReadSql + " FOR SHARE;", transaction: transaction);
        ValidateTaxMode(request, company);
        company.SalespersonName = await connection.QuerySingleOrDefaultAsync<string>("SELECT full_name FROM users WHERE user_id=@Id AND is_active",new {Id=company.DefaultSalespersonId ?? userId},transaction);
        if (company.SalespersonName is null) throw new ValidationException("Choose an active default salesperson in shop settings.");
        var manualLines = new Dictionary<CreateSaleItemRequest, ManualResolution>();
        var products = new Dictionary<int, SaleProduct>();
        // All stock writers lock products in ID order, before units, preventing overselling/deadlocks.
        foreach (var productId in request.Items.Where(i=>i.ManualItem is null).Select(i => i.ProductId).Distinct().Order())
        {
            var product = await connection.QuerySingleOrDefaultAsync<SaleProduct>("""
                SELECT product_id AS ProductId, product_name AS ProductName, is_serial_tracked AS IsSerialTracked,
                    tax_percentage AS TaxPercentage, warranty_months AS WarrantyMonths, model AS Model,
                    (SELECT brand_name FROM brands WHERE brand_id=products.brand_id) AS BrandName
                FROM products WHERE product_id = @Id AND is_active FOR UPDATE;
                """,
new
{
    Id = productId
},
transaction);
            if (product is null)
            {
                throw new ValidationException("A product is missing or inactive.");
            }

            products.Add(productId, product);
            if (await connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE product_id = @Id AND (quantity <= 0 OR
                    transaction_type NOT IN ('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN','SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS')));
                """,
new
{
    Id = productId
},
transaction))
            {
                throw new ValidationException("Review legacy/invalid inventory movements before selling this product.");
            }

            var stock = await connection.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id = @Id;", new
            {
                Id = productId
            }, transaction);
            if (stock < request.Items.Where(i => i.ProductId == productId).Sum(i => (long)i.Quantity))
            {
                throw new ValidationException("Insufficient stock for " + product.ProductName + ".");
            }
        }

        var variants = new Dictionary<CreateSaleItemRequest, StockFlow.Application.DTOs.Products.VariantResponse>();
        foreach (var item in request.Items.OrderBy(i => i.ProductId).ThenBy(i => i.VariantId))
        {
            if (item.ManualItem is not null)
            {
                var manual=await ResolveManualAsync(connection,transaction,item,request.PricesIncludeTax == true);
                manualLines[item]=manual; variants[item]=manual.Variant; continue;
            }
            variants[item] = await VariantRepository.ResolveAsync(connection, transaction, item.ProductId, item.VariantId, item.ProductUnitId, requireExplicitVariant: factory.RequiresExplicitRetailVariant);
            variants[item].TaxPercentage = await ResolveLineTaxAsync(connection,transaction,request,item,variants[item]);
        }

        foreach (var group in request.Items.Where(i=>i.ManualItem is null).GroupBy(i => variants[i].VariantId))
        {
            var stock = await connection.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_variant_current_stock WHERE variant_id=@id", new
            {
                id = group.Key
            }, transaction);
            if (stock < group.Sum(i => (long)i.Quantity))
            {
                throw new ValidationException("Insufficient stock for the selected variant.");
            }

            if (variants[group.First()].IsSerialTracked && stock != await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM product_units WHERE variant_id=@id AND unit_status='IN_STOCK'",
new
{
    id = group.Key
},
transaction))
            {
                throw new ValidationException("Variant units disagree with the stock ledger.");
            }
        }

        var serials = new Dictionary<long, string?>();
        foreach (var item in request.Items.OrderBy(i => i.ProductUnitId))
        {
            var tracked = variants[item].IsSerialTracked;
            if (tracked != item.ProductUnitId.HasValue)
            {
                throw new ValidationException(tracked ? "Select a physical unit for each serialized sale line." : "Quantity-only products cannot include physical units.");
            }

            if (!tracked)
            {
                continue;
            }

            var unit = await connection.QuerySingleOrDefaultAsync<SaleUnit>("""
                SELECT serial_number AS SerialNumber, unit_status AS UnitStatus, sale_id AS SaleId FROM product_units
                WHERE product_unit_id = @ProductUnitId AND product_id = @ProductId AND variant_id=@VariantId FOR UPDATE;
                """,
new
{
    item.ProductUnitId,
    item.ProductId,
    variants[item].VariantId
},
transaction);
            if (unit is null || unit.UnitStatus != "IN_STOCK" || unit.SaleId.HasValue)
            {
                throw new ValidationException("A selected unit is sold, damaged, unavailable, or belongs to another product.");
            }

            serials.Add(item.ProductUnitId!.Value, unit.SerialNumber);
        }

        var quote = SaleRules.CalculateLines(request, request.Items.Select(i => variants[i].TaxPercentage).ToArray(), true);
        for(var n=0;n<request.Items.Count;n++) { quote.Items[n].CatalogPrice=variants[request.Items[n]].SellingPrice; quote.Items[n].Mrp=variants[request.Items[n]].Mrp>0 ? variants[request.Items[n]].Mrp : null; CheckMrp(quote.Items[n],request.Items[n]); }
        var priceChanged = request.Items.Any(i => i.ManualItem is null && i.SellingPrice != variants[i].SellingPrice);
        var tradeAllowance = request.TradeIn?.AllowanceAmount ?? 0;
        var discretionaryDiscount = request.DiscountAmount - tradeAllowance + request.Items.Sum(i => i.DiscountAmount);
        if (priceChanged && !request.CanOverridePrice)
        {
            throw new ValidationException("You need selling-price override permission for this change.");
        }

        if (discretionaryDiscount > 0 && !request.CanDiscount)
        {
            throw new ValidationException("You need discount permission for this change.");
        }

        if (request.TradeIn is not null && !request.CanTradeIn)
        {
            throw new ValidationException("You need fulfilment permission to receive a trade-in.");
        }

        // Share policy decisions with quote; lock policy writes until this posting commits.
        var policy=await BillingPolicyStore.ReadAsync(connection,transaction);
        var review=policy.Evaluate(request,quote,request.Items.Select(i=>variants[i].SellingPrice).ToArray());
        policy.ValidateAdjustments(request,review,true);
        var approvalHash=BillingApproval.Hash(request,policy.Stamp,policy.Version>0?quote:null);
        if(review.RequiresApproval && policy.Version>0)
        {
            var unresolved=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM billing_approvals WHERE draft_id=@RequestId AND requested_by=@userId AND financial_hash=@hash AND (rejected_at IS NOT NULL OR approved_at IS NULL))",new{request.RequestId,userId,hash=approvalHash},transaction);
            if(unresolved)throw new ValidationException("This exception is pending or rejected. Resolve its manager approval before checkout.");
        }
        if(review.RequiresApproval && !request.CanApproveDiscount)
        {
            var notRejected=policy.Version>0?" AND rejected_at IS NULL":"";
            var approved=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM billing_approvals WHERE draft_id=@RequestId AND requested_by=@userId AND financial_hash=@hash AND approved_by<>requested_by AND approved_at>now()-interval '30 minutes'"+notRejected+")",new{request.RequestId,userId,hash=approvalHash},transaction);
            if(!approved)throw new ValidationException($"This reduction exceeds the {policy.DiscountLimitPercent:0.##}% limit. Request approval before completing the bill.");
        }
        var credit = request.Payments.Where(p => p.PaymentMode == "CREDIT").Sum(p => p.Amount);
        if (credit > 0)
        {
            var limit = await connection.QuerySingleOrDefaultAsync<decimal?>("SELECT credit_limit FROM customer_credit_limits WHERE customer_id=@CustomerId",
new
{
    request.CustomerId
},
transaction);
            var due = await connection.ExecuteScalarAsync<decimal>("SELECT sf_customer_credit_balance(@CustomerId)", new
            {
                request.CustomerId
            }, transaction);
            if (limit.HasValue && due + credit > limit.Value)
            {
                throw new ValidationException($"Customer credit limit exceeded. Outstanding {due:0.00}; limit {limit:0.00}.");
            }
        }

        var invoice = await connection.ExecuteScalarAsync<string>("SELECT replace(@Prefix,'{YYYY}',to_char(CURRENT_DATE,'YYYY')) || lpad(n::text, GREATEST(6,length(n::text)), '0') FROM nextval('invoice_number_seq') n;", new {Prefix=company.InvoicePrefix},
transaction: transaction);
        var saleId = await connection.ExecuteScalarAsync<long>("""
            INSERT INTO sales (customer_id, customer_name_snapshot, invoice_number, subtotal, discount_amount, tax_amount, total_amount,
                status, notes, created_by, request_id, request_payload,invoice_snapshot_version,customer_mobile_snapshot,customer_address_snapshot,customer_gst_snapshot,company_snapshot)
            VALUES (@CustomerId, @CustomerName, @Invoice, @Subtotal, @DiscountAmount, @TaxAmount, @TotalAmount,
                'COMPLETED', @Notes, @UserId, @RequestId, CAST(@Payload AS jsonb),1,@CustomerMobile,@CustomerAddress,@CustomerGst,CAST(@Company AS jsonb)) RETURNING sale_id;
            """,
new
{
    request.CustomerId,
    CustomerName = customer?.Name,
    CustomerMobile = customer?.Mobile,
    CustomerAddress = customer?.Address,
    CustomerGst = customer?.Gst,
    Company = JsonSerializer.Serialize(company),
    Invoice = invoice,
    quote.Subtotal,
    quote.DiscountAmount,
    quote.TaxAmount,
    quote.TotalAmount,
    request.Notes,
    UserId = userId,
    request.RequestId,
    Payload = payload
},
transaction);
        if (company.DefaultSalespersonId is {} salesperson)
        {
            if (!await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE user_id=@Id AND is_active)",new {Id=salesperson},transaction))
                throw new ValidationException("The default salesperson is inactive. Update shop settings before billing.");
            await connection.ExecuteAsync("UPDATE sales SET salesperson_user_id=@Person WHERE sale_id=@Id",new {Person=salesperson,Id=saleId},transaction);
        }
        if(request.PaymentDueDate is {} paymentDueDate)
        {
            var installed=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='sales' AND column_name='payment_due_date')",transaction:transaction);
            if(!installed) throw new ValidationException("Payment due dates require database migration 037. Ask your administrator to install it.");
            await connection.ExecuteAsync("UPDATE sales SET payment_due_date=@Due WHERE sale_id=@Id",new {Due=paymentDueDate.Date,Id=saleId},transaction);
        }
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var line = quote.Items[index];
            if (item.ManualItem is {} manual)
            {
                await connection.ExecuteAsync("""
                    INSERT INTO sale_items(sale_id,product_id,variant_id,quantity,selling_price,discount_amount,tax_percentage,line_total,
                     product_name_snapshot,is_manual,description_snapshot,mrp_snapshot,discount_type,discount_percent,manual_tax_snapshot,warranty_months_snapshot)
                    VALUES(@saleId,NULL,NULL,@Quantity,@NetUnitPrice,@NetDiscountAmount,@TaxPercentage,@LineTotal,@Name,true,@Description,@Mrp,@DiscountType,@DiscountPercent,@Tax::jsonb,0)
                    """,new {saleId,item.Quantity,line.NetUnitPrice,line.NetDiscountAmount,line.TaxPercentage,line.LineTotal,
                        manual.Name,manual.Description,manual.Mrp,DiscountType=item.DiscountPercent.HasValue?"PERCENTAGE":"FIXED",item.DiscountPercent,Tax=manualLines[item].TaxJson},transaction);
                continue;
            }
            var product = products[item.ProductId];
            var variant = variants[item];
            var itemId = await connection.ExecuteScalarAsync<long>("""
                INSERT INTO sale_items (sale_id, product_id, variant_id, variant_name_snapshot, product_unit_id, quantity, selling_price, discount_amount, tax_percentage, line_total, product_name_snapshot, serial_number_snapshot,brand_name_snapshot,model_snapshot,warranty_months_snapshot,discount_type,discount_percent)
                VALUES (@SaleId, @ProductId, @VariantId, @VariantName, @ProductUnitId, @Quantity, @SellingPrice, @DiscountAmount, @TaxPercentage, @LineTotal, @ProductName, @Serial,@BrandName,@Model,@WarrantyMonths,@DiscountType,@DiscountPercent) RETURNING sale_item_id;
                """,
new
{
    SaleId = saleId,
    item.ProductId,
    variant.VariantId,
    variant.VariantName,
    item.ProductUnitId,
    item.Quantity,
    SellingPrice = line.NetUnitPrice,
    DiscountAmount = line.NetDiscountAmount,
    line.TaxPercentage,
    line.LineTotal,
    product.ProductName,
    product.BrandName,
    product.Model,
    variant.WarrantyMonths,
    DiscountType=item.DiscountPercent.HasValue?"PERCENTAGE":"FIXED",item.DiscountPercent,
    Serial = item.ProductUnitId.HasValue ? serials[item.ProductUnitId.Value] : null
},
transaction);
            if (item.ProductUnitId.HasValue)
            {
                await connection.ExecuteAsync("UPDATE product_units SET unit_status = 'SOLD', sale_id = @SaleId, updated_at = CURRENT_TIMESTAMP WHERE product_unit_id = @Id;",
new
{
    SaleId = saleId,
    Id = item.ProductUnitId.Value
},
transaction);
            }

            if (variant.WarrantyMonths > 0)
            {
                var warrantyId = await connection.ExecuteScalarAsync<long>("""
                        INSERT INTO warranties (sale_item_id, product_unit_id, warranty_start_date, warranty_end_date, warranty_status)
                        VALUES (@ItemId, @UnitId, CURRENT_DATE, (CURRENT_DATE + make_interval(months => @Months))::date, 'ACTIVE')
                        RETURNING warranty_id;
                        """,
new
{
    ItemId = itemId,
    UnitId = item.ProductUnitId,
    Months = variant.WarrantyMonths
},
transaction);
                await connection.ExecuteAsync("""
                    INSERT INTO warranty_events(warranty_id,action_name,new_status,remarks,created_by)
                    VALUES(@warrantyId,'REGISTER','ACTIVE','Registered automatically at sale.',@userId);
                    """,
new
{
    warrantyId,
    userId
},
transaction);
            }

            await connection.ExecuteAsync("""
                INSERT INTO inventory_transactions (product_id, variant_id, product_unit_id, transaction_type, quantity, reference_type, reference_id, remarks, created_by)
                VALUES (@ProductId, @VariantId, @ProductUnitId, 'SALE', @Quantity, 'SALE', @SaleId, @Invoice, @UserId);
                """,
new
{
    item.ProductId,
    variant.VariantId,
    item.ProductUnitId,
    item.Quantity,
    SaleId = saleId,
    Invoice = invoice,
    UserId = userId
},
transaction);
        }

        foreach (var payment in request.Payments)
        {
            await connection.ExecuteAsync("""
                INSERT INTO payments (sale_id, payment_mode, amount, reference_number, created_by, request_payload)
                VALUES (@SaleId, @PaymentMode, @Amount, @ReferenceNumber, @UserId,
                    CASE WHEN @CardType::text IS NULL THEN NULL ELSE jsonb_build_object('cardType',@CardType::text) END);
                """,
new
{
    SaleId = saleId,
    payment.PaymentMode,
    payment.Amount,
    payment.ReferenceNumber,
    payment.CardType,
    UserId = userId
},
transaction);
        }

        if (request.TradeIn is { } trade)
        {
            await connection.ExecuteAsync("""
                INSERT INTO trade_ins(sale_id,item_type,brand_model,serial_number,allowance_amount,status,notes,request_id,request_payload,created_by)
                VALUES(@saleId,@ItemType,@BrandModel,@SerialNumber,@AllowanceAmount,'RECEIVED','Allowance included in invoice discount at billing.',@RequestId,CAST(@payload AS jsonb),@userId)
                """,
new
{
    saleId,
    trade.ItemType,
    trade.BrandModel,
    trade.SerialNumber,
    trade.AllowanceAmount,
    request.RequestId,
    payload,
    userId
},
transaction);
        }

        review=review with {ApprovalStatus=review.RequiresApproval?(request.CanApproveDiscount?"APPROVER_AUTHORITY":"APPROVED"):"NOT_REQUIRED"};
        await connection.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,'Sales','DISCOUNT_POLICY_APPLIED','sales',@saleId,CAST(@details AS jsonb))",new{userId,saleId,details=JsonSerializer.Serialize(new{ShopId=factory.Scope?.StoreId,request.RequestId,Policy=policy,Review=review,ApprovalHash=review.RequiresApproval?approvalHash:null,Authorization=!review.RequiresApproval?"SHOP_POLICY":request.CanApproveDiscount?"APPROVER_AUTHORITY":"MANAGER_APPROVAL",Items=request.Items,request.DiscountAmount})},transaction);
        if(draftLifecycle)
            await connection.ExecuteAsync("SELECT public.sf_draft_complete(@RequestId,@saleId)",new{request.RequestId,saleId},transaction);
        else await connection.ExecuteAsync("DELETE FROM billing_drafts WHERE draft_id=@RequestId AND user_id=@userId",
new
{
    request.RequestId,
    userId
},
transaction);
        await connection.ExecuteAsync("DELETE FROM billing_approvals WHERE draft_id=@RequestId AND requested_by=@userId",
new
{
    request.RequestId,
    userId
},
transaction);
        await connection.ExecuteAsync("""
            INSERT INTO audit_logs (user_id, module_name, action_name, table_name, record_id, new_values)
            VALUES (@UserId, 'Sales', 'COMPLETE', 'sales', @SaleId, CAST(@Payload AS jsonb));
            """,
new
{
    UserId = userId,
    SaleId = saleId,
    Payload = payload
},
transaction);
        if (await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_gst_capture_sale(bigint,text)') IS NOT NULL",transaction:transaction))
        {
            string? gstContext;
            try{gstContext=await connection.ExecuteScalarAsync<string>("SELECT public.sf_gst_sale_context()::text",transaction:transaction);}
            catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.CheckViolation){throw new ValidationException(ex.MessageText);}
            using var issuingContext=JsonDocument.Parse(gstContext!);
            var ordinaryUnregistered=issuingContext.RootElement.TryGetProperty("registrationType",out var registrationType)
                && registrationType.GetString()=="UNREGISTERED" && request.Items.Any(i=>i.ManualItem?.TaxRuleId==0);
            if(issuingContext.RootElement.GetProperty("mapped").GetBoolean() && !ordinaryUnregistered)
            {
                GstRules.Gstin(GstRules.Clean(customer?.Gst)?.ToUpperInvariant());
                if(request.GstPlaceOfSupplyStateCode is null) throw new ValidationException("Choose the actual place of supply for this GST bill.");
                GstRules.State(request.GstPlaceOfSupplyStateCode);
                try{await connection.ExecuteScalarAsync<long?>("SELECT public.sf_gst_capture_sale(@saleId,@pos)",new{saleId,pos=request.GstPlaceOfSupplyStateCode},transaction);}
                catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.CheckViolation){throw new ValidationException(ex.MessageText);}
            }
        }
        if(request.CrmEnquiryId is {} enquiryId)
        {
            if(enquiryId<=0||!await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_crm_link_sale(bigint,bigint)') IS NOT NULL",transaction:transaction))
                throw new ValidationException("CRM sale integration is unavailable. Review the enquiry before checkout.");
            try{await connection.ExecuteAsync("SELECT public.sf_crm_link_sale(@enquiryId,@saleId)",new{enquiryId,saleId},transaction);}
            catch(PostgresException ex)when(ex.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NoDataFound or PostgresErrorCodes.InsufficientPrivilege)
            {throw new ValidationException("The enquiry is unavailable, already linked or no longer eligible for this customer and shop. Reload it before checkout.");}
        }
        await transaction.CommitAsync();
        return saleId;
    }

    public async Task<long?> RecoverCompletedAsync(CreateSaleRequest request, int userId)
    {
        // Older browser drafts have no account context. Recover only an existing,
        // matching invoice; this endpoint must never create a sale or consume stock.
        factory.RequireLocation(request.LocationId);
        SaleRules.Validate(request, true);
        await using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<long?>("""
            SELECT sale_id FROM sales WHERE request_id=@RequestId AND created_by=@userId
                AND status='COMPLETED' AND location_id=@locationId AND jsonb_strip_nulls(request_payload)=jsonb_strip_nulls(CAST(@payload AS jsonb))
            """,
new
{
    request.RequestId,
    userId,
    locationId = factory.Scope?.LocationId ?? request.LocationId,
    payload = JsonSerializer.Serialize(request)
});
    }

    private static string? EffectiveLineReason(CreateSaleRequest request,CreateSaleItemRequest item)
        => request.Items.Any(line=>line.AdjustmentReason is not null)?item.AdjustmentReason:request.AdjustmentReason;

    private static async Task<decimal> ResolveLineTaxAsync(NpgsqlConnection c,NpgsqlTransaction t,CreateSaleRequest request,CreateSaleItemRequest item,StockFlow.Application.DTOs.Products.VariantResponse variant)
    {
        var mappedRate=await GstRateAsync(c,t,item.ProductId,variant.VariantId);
        var rate=mappedRate??variant.TaxPercentage;
        if(item.TaxOverride is not {} requested || requested==rate)return rate;
        if(mappedRate.HasValue)throw new ValidationException("This shop uses reviewed GST tax mappings. Update the product tax mapping in GST Settings instead of overriding it at checkout.");
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_transfer_permission('SALES','TAX_OVERRIDE')",transaction:t))
            throw new UnauthorizedAccessException("Tax override permission is required.");
        var taxReason=item.TaxAdjustmentReason ?? EffectiveLineReason(request,item);
        if(string.IsNullOrWhiteSpace(taxReason)||taxReason.Trim().Length<3)
            throw new ValidationException("Enter a reason for the manual tax change.");
        return requested;
    }
    private static async Task<decimal?> GstRateAsync(NpgsqlConnection c,NpgsqlTransaction t,int productId,long? variantId=null)
    {
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_gst_product_rate(integer)') IS NOT NULL",transaction:t))return null;
        try{var scoped=await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_gst_product_rate(integer,bigint)') IS NOT NULL",transaction:t);return scoped?await c.ExecuteScalarAsync<decimal?>("SELECT public.sf_gst_product_rate(@productId,@variantId)",new{productId,variantId},t):await c.ExecuteScalarAsync<decimal?>("SELECT public.sf_gst_product_rate(@productId)",new{productId},t);}
        catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.CheckViolation){throw new ValidationException(ex.MessageText);}
    }

    private sealed class SaleProduct
    {
        public string? BrandName
        {
            get; set;
        }
        public string? Model
        {
            get; set;
        }
        public int ProductId
        {
            get; set;
        }
        public string ProductName { get; set; } = "";
        public bool IsSerialTracked
        {
            get; set;
        }
        public decimal TaxPercentage
        {
            get; set;
        }
        public int WarrantyMonths
        {
            get; set;
        }
    }

    private sealed class SaleUnit
    {
        public string? SerialNumber
        {
            get; set;
        }
        public string UnitStatus { get; set; } = "";
        public long? SaleId
        {
            get; set;
        }
    }

    private sealed class SaleReceipt
    {
        public long SaleId
        {
            get; set;
        }
        public int CreatedBy
        {
            get; set;
        }
        public bool SamePayload
        {
            get; set;
        }
    }

    private sealed class InvoiceCustomer
    {
        public string Name { get; set; } = "";
        public string? Mobile
        {
            get; set;
        }
        public string? Address
        {
            get; set;
        }
        public string? Gst
        {
            get; set;
        }
    }
}
