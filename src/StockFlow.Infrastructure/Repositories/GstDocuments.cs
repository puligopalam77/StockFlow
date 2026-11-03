using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Infrastructure.Repositories;

public sealed partial class GstRepository
{
    public async Task<GstSourceOptions> SourceOptionsAsync()
    {
        await using var c=factory.CreateConnection();await RequireAsync(c,"CREATE","GST_REPORTS");factory.RequireLocation();
        using var rows=await c.QueryMultipleAsync("""
        SELECT product_id ProductId,product_name ProductName,hsn_code HsnCode,unit_of_measure UnitOfMeasure,tax_percentage TaxPercentage FROM products WHERE is_active ORDER BY product_name;
        SELECT supplier_id SupplierId,supplier_name SupplierName,gst_number Gstin FROM suppliers WHERE is_active ORDER BY supplier_name;
        SELECT purchase_id PurchaseId,supplier_id SupplierId,purchase_number PurchaseNumber FROM purchases WHERE location_id=public.sf_location() AND status<>'CANCELLED' ORDER BY purchase_id DESC;
        """);
        return new((await rows.ReadAsync<GstProduct>()).ToArray(),(await rows.ReadAsync<GstSupplierOption>()).ToArray(),(await rows.ReadAsync<GstPurchaseOption>()).ToArray());
    }
    public async Task<IEnumerable<GstExportHistory>> ExportHistoryAsync()
    {
        await using var c=factory.CreateConnection();await RequireAsync(c,"VIEW","GST_REPORTS");
        return await c.QueryAsync<GstExportHistory>("SELECT export_id ExportId,registration_id RegistrationId,report,from_date::timestamp FromDate,to_date::timestamp ToDate,scope_label ScopeLabel,row_count::bigint RowCount,sha256,created_by CreatedBy,created_at CreatedAt,version FROM gst_export_history ORDER BY export_id DESC LIMIT 200");
    }
    public async Task<IEnumerable<GstReviewHistory>> ReviewHistoryAsync(long id)
    {
        await using var c=factory.CreateConnection();await RequireAsync(c,"VIEW","GST_REPORTS");
        return await c.QueryAsync<GstReviewHistory>("SELECT review_id ReviewId,status,notes,created_by CreatedBy,created_at CreatedAt FROM gst_document_reviews WHERE document_id=@id ORDER BY review_id DESC",new{id});
    }
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    private const string DocumentColumns="""
        d.document_id DocumentId,d.registration_id RegistrationId,d.business_profile_code BusinessProfileCode,d.store_id StoreId,d.location_id LocationId,d.store_name_snapshot StoreName,d.location_name_snapshot LocationName,
        d.direction,d.kind,d.document_number DocumentNumber,d.document_date DocumentDate,d.party_name PartyName,d.party_gstin PartyGstin,d.party_address PartyAddress,
        d.party_state_code PartyStateCode,d.place_of_supply PlaceOfSupply,d.delivery_address DeliveryAddress,d.registration_snapshot::text RegistrationSnapshot,d.business_snapshot::text BusinessSnapshot,
        d.sale_id SaleId,d.parent_document_id ParentDocumentId,d.purchase_ids PurchaseIds,d.evidence_reference EvidenceReference,d.reason,
        d.taxable_value TaxableValue,d.cgst,d.sgst,d.utgst,d.igst,d.cess,d.invoice_discount InvoiceDiscount,d.total,d.lines::text LinesJson,d.warnings,
        d.created_by CreatedBy,d.created_at CreatedAt,
        coalesce((SELECT e.status FROM gst_document_events e WHERE e.document_id=d.document_id ORDER BY event_id DESC LIMIT 1),CASE WHEN d.sale_id IS NOT NULL THEN 'COMPLETED' ELSE 'RECORDED' END) SourceStatus,
        coalesce((SELECT r.status FROM gst_document_reviews r WHERE r.tenant_id=d.tenant_id AND r.document_id=d.document_id ORDER BY r.review_id DESC LIMIT 1),'PENDING_REVIEW') ReviewStatus
        """;
    public async Task<IEnumerable<GstRegistration>> RegistrationsAsync()
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_documents') IS NOT NULL"))throw new ValidationException("Install manual GST migrations 070 and 071 before opening tax reports.");
        await RequireAsync(c,"VIEW","GST_REPORTS");
        return await c.QueryAsync<GstRegistration>("SELECT registration_id RegistrationId,business_id BusinessId,gstin,registration_type RegistrationType,state_code StateCode,status,effective_from EffectiveFrom,effective_to EffectiveTo,address FROM gst_registrations ORDER BY gstin NULLS LAST,registration_id");
    }
    private const string ReportFilter=" "+"""
        d.registration_id=@RegistrationId AND d.document_date BETWEEN @From AND @To
        AND (@RegistrationWide OR (d.business_profile_code=public.sf_visible_business_profile() AND d.store_id=public.sf_store() AND public.sf_can_location(d.location_id)))
        AND (@Term IS NULL OR d.document_number ILIKE @Pattern OR d.party_name ILIKE @Pattern OR d.party_gstin ILIKE @Pattern)
        AND (@PartyType::text IS NULL OR CASE WHEN d.party_gstin IS NULL THEN 'B2C' ELSE 'B2B' END=@PartyType)
        AND (@TaxRate::numeric IS NULL OR EXISTS(SELECT 1 FROM jsonb_array_elements(d.lines) l WHERE (l->>'taxRate')::numeric=@TaxRate))
        """;
    public async Task<GstReport> ReportAsync(GstReportQuery q,bool export=false)
    {
        GstDocumentRules.Query(q);
        await using var c=factory.CreateConnection();await c.OpenAsync();
        await RequireAsync(c,"VIEW","GST_REPORTS");
        if(q.RegistrationWide)await RequireAsync(c,"VIEW_REGISTRATION","GST_REPORTS");
        if(export)await RequireAsync(c,"EXPORT","GST_REPORTS");
        await using var t=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var gstin=await c.QuerySingleOrDefaultAsync<string?>("SELECT coalesce(gstin,'UNREGISTERED') FROM gst_registrations WHERE registration_id=@RegistrationId",q,t)
            ??throw new KeyNotFoundException("GST registration is not available in this shop.");
        var args=new{q.RegistrationId,q.From,q.To,q.RegistrationWide,q.Term,Pattern="%"+q.Term?.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%",q.PartyType,q.TaxRate,Offset=(q.Page-1)*q.Size,Limit=q.Size};
        var type=q.Report switch{"sales"=>"d.direction='SALES' AND d.kind='INVOICE'","purchases"=>"d.direction='PURCHASE' AND d.kind='INVOICE'","notes"=>"d.kind<>'INVOICE'",_=>"true"};
        var order=q.Sort switch{"number"=>"d.document_number","value"=>"d.total",_=>"d.document_date"};
        order+=q.Ascending?" ASC":" DESC";
        var result=new GstReport{Gstin=gstin,ScopeLabel=q.RegistrationWide?"Registration-wide tax records (current tenant only)":"Selected shop/profile tax records — partial registration view"};
        result.Summary=await c.QuerySingleAsync<GstSummary>("""
            SELECT coalesce(sum((CASE WHEN kind='CREDIT_NOTE' THEN -1 ELSE 1 END)*(SELECT coalesce(sum((l->>'taxableValue')::numeric),0) FROM jsonb_array_elements(d.lines) l WHERE l->>'treatment'='TAXABLE')) FILTER(WHERE direction='SALES'),0) TaxableSales,
            coalesce(sum((CASE WHEN kind='CREDIT_NOTE' THEN -1 ELSE 1 END)*(SELECT coalesce(sum((l->>'taxableValue')::numeric),0) FROM jsonb_array_elements(d.lines) l WHERE l->>'treatment'<>'TAXABLE')) FILTER(WHERE direction='SALES'),0) OtherSupplyValue,
            coalesce(sum((CASE WHEN kind='CREDIT_NOTE' THEN -1 ELSE 1 END)*(cgst+sgst+utgst+igst+cess)) FILTER(WHERE direction='SALES'),0) OutputGst,
            coalesce(sum((CASE WHEN kind='CREDIT_NOTE' THEN -1 ELSE 1 END)*(cgst+sgst+utgst+igst+cess)) FILTER(WHERE direction='PURCHASE'),0) RecordedPurchaseGst,
            coalesce(sum((CASE WHEN kind='CREDIT_NOTE' THEN -1 ELSE 1 END)*total) FILTER(WHERE kind<>'INVOICE'),0) NoteAdjustments,
            count(*) FILTER(WHERE NOT EXISTS(SELECT 1 FROM gst_document_reviews r WHERE r.document_id=d.document_id AND r.review_id=(SELECT max(review_id) FROM gst_document_reviews WHERE document_id=d.document_id) AND r.status='ACCOUNTANT_REVIEWED')) PendingReview
            FROM gst_documents d WHERE
            """+ReportFilter,args,t);
        if(q.Report=="hsn")
        {
            // Aggregate line facts once; never repeat invoice totals for each line.
            var hsnSql="""
                SELECT l->>'hsnSac' HsnSac,l->>'uqc' Uqc,CASE WHEN d.party_gstin IS NULL THEN 'B2C' ELSE 'B2B' END PartyType,d.direction,l->>'treatment' Treatment,(l->>'taxRate')::numeric TaxRate,
                sum(sign*(l->>'quantity')::numeric) Quantity,sum(sign*(l->>'taxableValue')::numeric) TaxableValue,
                sum(sign*(l->>'cgst')::numeric) Cgst,sum(sign*(l->>'sgst')::numeric) Sgst,sum(sign*(l->>'utgst')::numeric) Utgst,sum(sign*(l->>'igst')::numeric) Igst,sum(sign*(l->>'cess')::numeric) Cess,sum(sign*(l->>'total')::numeric) Total
                FROM gst_documents d CROSS JOIN LATERAL jsonb_array_elements(d.lines) l CROSS JOIN LATERAL(SELECT CASE WHEN d.kind='CREDIT_NOTE' THEN -1 ELSE 1 END sign) x
                WHERE
                """+ReportFilter+" AND (@TaxRate::numeric IS NULL OR (l->>'taxRate')::numeric=@TaxRate) GROUP BY 1,2,3,4,5,6";
            result.Count=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM ("+hsnSql+") grouped",args,t);
            result.Hsn=(await c.QueryAsync<GstHsnRow>(hsnSql+" ORDER BY 1,2,3,4,5,6"+(export?"":" OFFSET @Offset LIMIT @Limit"),args,t)).ToList();
        }
        else
        {
            result.Count=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_documents d WHERE "+ReportFilter+" AND "+type,args,t);
            if(export&&result.Count>100000)throw new ValidationException("Narrow the export to at most 100,000 documents.");
            result.Documents=(await c.QueryAsync<GstDocument>("SELECT "+DocumentColumns+" FROM gst_documents d WHERE "+ReportFilter+" AND "+type+" ORDER BY "+order+",d.document_id"+(export?"":" OFFSET @Offset LIMIT @Limit"),args,t)).ToList();
        }
        // This warning deliberately has no inferred legacy GSTIN or tax-component backfill.
        result.LegacySalesWithoutSnapshots=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM sales s WHERE s.sale_date::date BETWEEN @From AND @To AND s.status='COMPLETED' AND NOT EXISTS(SELECT 1 FROM gst_documents d WHERE d.tenant_id=s.tenant_id AND d.sale_id=s.sale_id) AND NOT EXISTS(SELECT 1 FROM sale_items i WHERE i.tenant_id=s.tenant_id AND i.sale_id=s.sale_id AND to_jsonb(i)->'manual_tax_snapshot'->>'gstOptional'='true')",args,t);
        await t.CommitAsync();return result;
    }
    public async Task<GstDocument?> DocumentAsync(long id)
    {
        await using var c=factory.CreateConnection();await RequireAsync(c,"VIEW","GST_REPORTS");
        return await c.QuerySingleOrDefaultAsync<GstDocument>("SELECT "+DocumentColumns+" FROM gst_documents d WHERE d.document_id=@id",new{id});
    }
    public async Task<long> RecordAsync(GstRecordRequest r)
    {
        GstDocumentRules.Record(r);factory.RequireLocation();
        await using var c=factory.CreateConnection();await c.OpenAsync();await RequireAsync(c,"CREATE","GST_REPORTS");await RequireAsync(c,"VIEW","GST_REPORTS");
        await using var t=await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock_shared(hashtextextended('gst-config:'||public.sf_tenant()::text,0))",transaction:t);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-record:'||@TenantId::text,0))",factory.Scope,t);
        var payload=JsonSerializer.Serialize(r,Json);
        var old=await c.QuerySingleOrDefaultAsync<(long Id,bool Same)?>("SELECT document_id,request_payload=CAST(@payload AS jsonb) FROM gst_documents WHERE request_id=@RequestId",new{r.RequestId,payload},t);
        if(old is {} saved){if(!saved.Same)throw new ValidationException("This request ID already recorded different tax information.");return saved.Id;}
        var reg=await c.QuerySingleOrDefaultAsync<string>("SELECT to_jsonb(r)::text FROM gst_registrations r JOIN gst_shop_mappings m USING(tenant_id,registration_id) WHERE r.registration_id=@RegistrationId AND r.status='ACTIVE' AND m.status='ACTIVE' AND m.business_profile_code=public.sf_visible_business_profile() AND m.store_id=public.sf_store() AND @DocumentDate BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') AND @DocumentDate BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity')",new{r.RegistrationId,r.DocumentDate},t)??throw new ValidationException("The source document date needs a valid selected-shop registration mapping.");
        using var registration=JsonDocument.Parse(reg);
        var business=await c.QuerySingleAsync<string>("SELECT to_jsonb(b)::text FROM gst_legal_businesses b WHERE business_id=@id",new{id=registration.RootElement.GetProperty("business_id").GetInt64()},t);
        string name;string? gstin,address,state;
        if(r.Kind=="INVOICE")
        {
            GstRules.State(r.SupplierStateCode);
            var supplier=await c.QuerySingleOrDefaultAsync<(string Name,string? Gst,string? Address)?>("SELECT supplier_name,gst_number,address FROM suppliers WHERE supplier_id=@SupplierId",new{r.SupplierId},t)??throw new ValidationException("Choose a supplier in the current shop.");
            name=supplier.Name;gstin=GstRules.Clean(supplier.Gst)?.ToUpperInvariant();address=supplier.Address;state=r.SupplierStateCode;GstRules.Gstin(gstin);
            if(gstin is not null&&!gstin.StartsWith(state,StringComparison.Ordinal))throw new ValidationException("Supplier GSTIN and registration state differ.");
            if(await c.ExecuteScalarAsync<int>("SELECT count(*) FROM purchases WHERE purchase_id=ANY(@ids) AND supplier_id=@SupplierId AND location_id=public.sf_location()",new{ids=r.PurchaseIds.ToArray(),r.SupplierId},t)!=r.PurchaseIds.Count)throw new ValidationException("All linked purchases must belong to this supplier and selected shop/location.");
        }
        else
        {
            var parent=await c.QuerySingleOrDefaultAsync<GstDocument>("SELECT "+DocumentColumns+" FROM gst_documents d WHERE d.document_id=@ParentDocumentId AND d.kind='INVOICE' AND d.location_id=public.sf_location() AND d.business_profile_code=public.sf_visible_business_profile()",new{r.ParentDocumentId},t)??throw new ValidationException("Choose an original invoice in this shop.");
            if(parent.RegistrationId!=r.RegistrationId || parent.Direction!=r.Direction || r.DocumentDate<parent.DocumentDate || parent.PlaceOfSupply!=r.PlaceOfSupply)throw new ValidationException("Adjustment registration, direction, place of supply and dates must agree with the original invoice.");
            reg=parent.RegistrationSnapshot;business=parent.BusinessSnapshot;name=parent.PartyName;gstin=parent.PartyGstin;address=parent.PartyAddress;state=parent.PartyStateCode;
            if(r.Kind=="CREDIT_NOTE")
            {
                var credited=await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(total),0) FROM gst_documents WHERE parent_document_id=@ParentDocumentId AND kind='CREDIT_NOTE'",new{r.ParentDocumentId},t);
                if(credited+r.Lines.Sum(l=>l.Total)>parent.Total)throw new ValidationException("Cumulative credit notes cannot exceed the original invoice value.");
                var priorJson=await c.QueryAsync<string>("SELECT lines::text FROM gst_documents WHERE parent_document_id=@ParentDocumentId AND kind='CREDIT_NOTE'",new{r.ParentDocumentId},t);
                var requested=priorJson.SelectMany(value=>JsonSerializer.Deserialize<List<GstLine>>(value,Json)!).Concat(r.Lines);
                var original=JsonSerializer.Deserialize<List<GstLine>>(parent.LinesJson,Json)!;
                foreach(var group in requested.GroupBy(l=>(l.ProductId,l.HsnSac,l.Uqc,l.TaxRate,l.Treatment,ManualLineId:l.IsManual?l.SourceLineId:null)))
                {
                    var source=original.Where(l=>(l.ProductId,l.HsnSac,l.Uqc,l.TaxRate,l.Treatment,ManualLineId:l.IsManual?l.SourceLineId:null)==group.Key).ToArray();
                    if(group.Sum(l=>l.Quantity)>source.Sum(l=>l.Quantity) || group.Sum(l=>l.TaxableValue)>source.Sum(l=>l.TaxableValue)
                        || group.Sum(l=>l.Cgst)>source.Sum(l=>l.Cgst) || group.Sum(l=>l.Sgst)>source.Sum(l=>l.Sgst)
                        || group.Sum(l=>l.Utgst)>source.Sum(l=>l.Utgst) || group.Sum(l=>l.Igst)>source.Sum(l=>l.Igst) || group.Sum(l=>l.Cess)>source.Sum(l=>l.Cess))
                        throw new ValidationException("Cumulative credit-note quantities and tax components cannot exceed the original source lines.");
                }
            }
            var originalLines=JsonSerializer.Deserialize<List<GstLine>>(parent.LinesJson,Json)!;
            foreach(var line in r.Lines)
                if(!originalLines.Any(l=>l.ProductId==line.ProductId && l.IsManual==line.IsManual && (!line.IsManual || l.SourceLineId==line.SourceLineId) && l.HsnSac==line.HsnSac && l.Uqc==line.Uqc && l.TaxRate==line.TaxRate && l.Treatment==line.Treatment))throw new ValidationException("Adjustment tax classifications must match an original invoice line.");
        }
        foreach(var line in r.Lines)
        {
            if(!line.IsManual && !await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM products WHERE product_id=@ProductId)",line,t))throw new ValidationException("A tax line refers to another shop's product.");
            var issuerState=r.Direction=="PURCHASE"?state:JsonDocument.Parse(reg).RootElement.GetProperty("state_code").GetString();
            if(issuerState==r.PlaceOfSupply && line.Igst>0 || issuerState!=r.PlaceOfSupply && line.Cgst+line.Sgst+line.Utgst>0)throw new ValidationException("Tax components disagree with the recorded supplier state and place of supply.");
        }
        var warnings=new[]{"WORKING_REGISTER_PRACTITIONER_REVIEW",r.Direction=="PURCHASE"?"RECORDED_INPUT_NOT_APPROVED_ITC":"TAX_NOTE_NOT_AUTOMATICALLY_A_REFUND"};
        var id=await c.ExecuteScalarAsync<long>("""
        INSERT INTO gst_documents(registration_id,business_profile_code,store_id,location_id,direction,kind,document_number,document_date,party_name,party_gstin,party_address,party_state_code,place_of_supply,registration_snapshot,business_snapshot,parent_document_id,purchase_ids,evidence_reference,reason,taxable_value,cgst,sgst,utgst,igst,cess,total,lines,warnings,request_id,request_payload)
        VALUES(@RegistrationId,public.sf_visible_business_profile(),public.sf_store(),public.sf_location(),@Direction,@Kind,@DocumentNumber,@DocumentDate,@name,@gstin,@address,@state,@PlaceOfSupply,CAST(@reg AS jsonb),CAST(@business AS jsonb),@ParentDocumentId,@purchases,@EvidenceReference,@Reason,@taxable,@cgst,@sgst,@utgst,@igst,@cess,@total,CAST(@lines AS jsonb),@warnings,@RequestId,CAST(@payload AS jsonb)) RETURNING document_id
        """,new{r.RegistrationId,r.Direction,r.Kind,r.DocumentNumber,r.DocumentDate,name,gstin,address,state,r.PlaceOfSupply,reg,business,r.ParentDocumentId,purchases=r.PurchaseIds.ToArray(),r.EvidenceReference,r.Reason,taxable=r.Lines.Sum(l=>l.TaxableValue),cgst=r.Lines.Sum(l=>l.Cgst),sgst=r.Lines.Sum(l=>l.Sgst),utgst=r.Lines.Sum(l=>l.Utgst),igst=r.Lines.Sum(l=>l.Igst),cess=r.Lines.Sum(l=>l.Cess),total=r.Lines.Sum(l=>l.Total),lines=JsonSerializer.Serialize(r.Lines,Json),warnings,r.RequestId,payload},t);
        await t.CommitAsync();return id;
    }
    public async Task ReviewAsync(GstReviewRequest r)
    {
        if(r.DocumentId<=0 || r.Status is not ("PENDING_REVIEW" or "ACCOUNTANT_REVIEWED" or "NEEDS_CORRECTION") || string.IsNullOrWhiteSpace(r.Notes) || r.Notes.Trim().Length<3 || r.Notes.Length>1000)throw new ValidationException("Choose a working review status and provide review notes (3–1,000 characters).");
        await using var c=factory.CreateConnection();await RequireAsync(c,"REVIEW","GST_REPORTS");
        await c.ExecuteAsync("INSERT INTO gst_document_reviews(document_id,status,notes) VALUES(@DocumentId,@Status,@Notes)",r);
    }
    public async Task<GstExportResult> ExportAsync(GstReportQuery q)
    {
        var report=await ReportAsync(q,true);var sb=new StringBuilder();
        static string Cell(object? value){var text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";if(value is string && (text.TrimStart().StartsWith('=')||text.TrimStart().StartsWith('+')||text.TrimStart().StartsWith('-')||text.TrimStart().StartsWith('@')))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
        void Row(params object?[] values)=>sb.AppendLine(string.Join(",",values.Select(Cell)));
        var timestamp=DateTime.UtcNow;
        Row("StockFlow accountant working report — NOT FILED","Version 1");Row("GSTIN",report.Gstin,"Registration",q.RegistrationId,"From",q.From.ToString("yyyy-MM-dd"),"To",q.To.ToString("yyyy-MM-dd"));
        Row("Scope",report.ScopeLabel,"Exported UTC",timestamp.ToString("O"),"Prepared by user",factory.Scope!.UserId);Row("Filters",JsonSerializer.Serialize(q,Json));
        Row("Limitations","Recorded input GST is not eligible ITC; legacy coverage and source corrections require review. Document reviews do not establish filing.");
        if(q.Report=="hsn")
        {
            Row("HSN/SAC","UQC","Party type","Direction","Treatment","Rate","Quantity","Supply base before GST (see HSN treatment)","CGST","SGST","UTGST","IGST","Cess","Gross line value before invoice-level discount");
            foreach(var h in report.Hsn)Row(h.HsnSac,h.Uqc,h.PartyType,h.Direction,h.Treatment,h.TaxRate,h.Quantity,h.TaxableValue,h.Cgst,h.Sgst,h.Utgst,h.Igst,h.Cess,h.Total);
        }
        else
        {
            Row("Document ID","Number","Date","Profile","Store","Location","Direction","Type","Party","Party GSTIN","B2B/B2C","Place of supply","Supply base before GST (see HSN treatment)","CGST","SGST","UTGST","IGST","Cess","Total GST","After-tax invoice discount","Invoice value","Review status","Source status","Warnings","Parent ID");
            foreach(var d in report.Documents)Row(d.DocumentId,d.DocumentNumber,d.DocumentDate.ToString("yyyy-MM-dd"),d.BusinessProfileCode,d.StoreName,d.LocationName,d.Direction,d.Kind,d.PartyName,d.PartyGstin,d.PartyGstin is null?"B2C":"B2B",d.PlaceOfSupply,d.TaxableValue*d.Sign,d.Cgst*d.Sign,d.Sgst*d.Sign,d.Utgst*d.Sign,d.Igst*d.Sign,d.Cess*d.Sign,d.TotalGst*d.Sign,d.InvoiceDiscount*d.Sign,d.Total*d.Sign,d.ReviewStatus,d.SourceStatus,string.Join(";",d.Warnings),d.ParentDocumentId);
        }
        var bytes=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        await using var c=factory.CreateConnection();
        await c.ExecuteAsync("INSERT INTO gst_export_history(registration_id,report,from_date,to_date,scope_label,filters,row_count,sha256) VALUES(@RegistrationId,@Report,@From,@To,@scope,CAST(@filters AS jsonb),@count,@hash)",new{q.RegistrationId,q.Report,q.From,q.To,scope=report.ScopeLabel,filters=JsonSerializer.Serialize(q,Json),count=report.Count,hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()});
        return new(bytes,$"gst-working-{q.Report}-{report.Gstin}-{q.From:yyyyMMdd}-{q.To:yyyyMMdd}.csv");
    }
}
