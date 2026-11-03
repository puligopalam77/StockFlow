using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class EWayRepository(DbConnectionFactory factory) : IEWayRepository
{
    public async Task<IReadOnlyList<EWayRegistration>> RegistrationsAsync()
    {
        await RequireAsync("VIEW");
        await using var c = factory.CreateConnection();
        return (await c.QueryAsync<EWayRegistration>("""
            SELECT DISTINCT r.registration_id RegistrationId,CASE WHEN r.gstin IS NULL THEN 'Unregistered' ELSE 'GSTIN ending '||right(r.gstin,4) END Label
            FROM gst_registrations r JOIN gst_shop_mappings m USING(tenant_id,registration_id)
            WHERE m.store_id=public.sf_store() AND m.business_profile_code=public.sf_visible_business_profile() AND m.status='ACTIVE'
            ORDER BY RegistrationId
            """)).ToList();
    }
    private const string Columns = "document_id DocumentId,version,source_type SourceType,source_id SourceId,registration_id RegistrationId,source_snapshot::text SourceJson,body::text BodyJson,status,external_ebn ExternalEbn,external_evidence ExternalEvidence,created_at CreatedAt,updated_at UpdatedAt";
    public async Task RequireAsync(string action)
    {
        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        if (!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_eway_allowed(text)') IS NOT NULL")) throw new ValidationException("Apply manual migration 084 before opening e-way preparation.");
        if (!await c.ExecuteScalarAsync<bool>("SELECT public.sf_eway_allowed(@action)", new { action })) throw new UnauthorizedAccessException("E-way access requires the action, GST report access and selected shop/location.");
    }
    public async Task<JsonElement> SourceAsync(string type, long id, long registrationId)
    {
        await RequireAsync("VIEW");
        await using var c = factory.CreateConnection();
        var json = await c.ExecuteScalarAsync<string>("SELECT public.sf_eway_source(@type,@id,@registrationId)::text", new { type, id, registrationId });
        return JsonSerializer.Deserialize<JsonElement>(json!);
    }
    public async Task<object> SourcesAsync(string? term, int page, string? type, long? saleId, long? purchaseId)
    {
        await RequireAsync("VIEW");
        if (page is < 1 or > 100000 || term?.Length > 100 || type is not (null or "GST" or "TRANSFER")) throw new ValidationException("Choose valid source filters.");
        await using var c = factory.CreateConnection();
        var rows = await c.QueryAsync("""
            WITH candidates AS (
             SELECT 'GST' type,d.document_id id,d.registration_id "registrationId",d.document_number number,d.document_date::text date,d.party_name party,d.direction
             FROM gst_documents d WHERE d.store_id=public.sf_store() AND d.location_id=public.sf_location() AND d.business_profile_code=public.sf_visible_business_profile() AND d.kind='INVOICE'
              AND (@saleId::bigint IS NULL OR d.sale_id=@saleId) AND (@purchaseId::bigint IS NULL OR @purchaseId=ANY(d.purchase_ids))
              AND ((d.direction='SALES' AND public.sf_transfer_permission('SALES','VIEW') AND EXISTS(SELECT 1 FROM sales s WHERE s.sale_id=d.sale_id AND s.status='COMPLETED')) OR (d.direction='PURCHASE' AND public.sf_transfer_permission('PURCHASES','VIEW')))
             UNION ALL
             SELECT 'TRANSFER',t.transfer_id,m.registration_id,t.transfer_number,t.requested_at::date::text,'Own stock movement','OUTWARD'
             FROM stock_transfers t JOIN gst_shop_mappings m ON m.store_id=public.sf_store() AND m.tenant_id=t.tenant_id
             WHERE t.from_location_id=public.sf_location() AND coalesce(to_jsonb(t)->>'business_profile_code','ELECTRICAL')=public.sf_visible_business_profile()
              AND t.status IN('APPROVED','DISPATCHED','PARTIALLY_RECEIVED','RECEIVED') AND m.status='ACTIVE' AND m.business_profile_code=public.sf_visible_business_profile()
              AND t.requested_at::date BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW')
              AND @saleId::bigint IS NULL AND @purchaseId::bigint IS NULL
            ) SELECT * FROM candidates WHERE (@type::text IS NULL OR type=@type) AND (@term::text IS NULL OR number ILIKE @pattern OR party ILIKE @pattern)
            ORDER BY date DESC,id DESC LIMIT 21 OFFSET @offset
            """, new { saleId, purchaseId, type, term, pattern = "%" + term + "%", offset = (page - 1) * 20 });
        var list = rows.ToList();
        return new { page, hasMore = list.Count > 20, rows = list.Take(20) };
    }
    public async Task<EWayPage> ListAsync(string? term, string? status, DateTime? from, DateTime? to, int page, int size, string? sourceType = null, long? registrationId = null)
    {
        await RequireAsync("VIEW");
        if (page is < 1 or > 100000 || size is < 1 or > 100 || term?.Length > 100 || from > to || status is not (null or "DRAFT" or "EXTERNAL_UNVERIFIED") || sourceType is not (null or "GST" or "TRANSFER") || registrationId is <= 0) throw new ValidationException("Choose valid register filters.");
        await using var c = factory.CreateConnection();
        const string filter = " WHERE (@term::text IS NULL OR external_ebn ILIKE @pattern OR source_snapshot->>'number' ILIKE @pattern OR source_snapshot->>'partyName' ILIKE @pattern OR source_snapshot->>'partyGstin' ILIKE @pattern) AND (@status::text IS NULL OR status=@status) AND (@from::date IS NULL OR created_at::date>=@from::date) AND (@to::date IS NULL OR created_at::date<=@to::date) AND (@sourceType::text IS NULL OR source_type=@sourceType) AND (@registrationId::bigint IS NULL OR registration_id=@registrationId)";
        var args = new { term, status, from, to, sourceType, registrationId, pattern = "%" + term + "%", offset = (page - 1) * size, size };
        using var multi = await c.QueryMultipleAsync("SELECT count(*) FROM eway_documents" + filter + ";SELECT " + Columns + " FROM eway_documents" + filter + " ORDER BY created_at DESC,document_id LIMIT @size OFFSET @offset", args);
        return new(await multi.ReadSingleAsync<long>(), page, size, (await multi.ReadAsync<EWayDocument>()).ToList());
    }
    public async Task<EWayDocument> GetAsync(Guid id)
    {
        await RequireAsync("VIEW");
        await using var c = factory.CreateConnection();
        return await c.QuerySingleOrDefaultAsync<EWayDocument>("SELECT " + Columns + " FROM eway_documents WHERE document_id=@id", new { id }) ?? throw new KeyNotFoundException("E-way preparation is unavailable in this shop.");
    }
    public async Task<long> SaveAsync(EWaySave input)
    {
        EWayPreparationRules.Shape(input);
        await RequireAsync(input.ExpectedVersion == 0 ? "PREPARE" : "EDIT");
        return await Write(input.DocumentId, input.OperationId, input.ExpectedVersion, "SAVE", input.SourceType, input.SourceId, input.RegistrationId, input.Preparation);
    }
    private async Task<long> Write(Guid id, Guid op, long expected, string act, string kind, long source, long reg, object payload)
    {
        await using var c = factory.CreateConnection();
        return await c.ExecuteScalarAsync<long>("SELECT public.sf_eway_write(@id,@op,@expected,@act,@kind,@source,@reg,@payload::jsonb)",
            new { id, op, expected, act, kind, source, reg, payload = JsonSerializer.Serialize(payload, EWayPreparationRules.Json) });
    }
    public async Task<object> ActAsync(Guid id, EWayAction input, string action)
    {
        if (action is not ("VALIDATE" or "EXPORT" or "RECORD_EXTERNAL")) throw new ValidationException("Unsupported e-way action.");
        await RequireAsync(action);
        var d = await GetAsync(id);
        var source = JsonSerializer.Deserialize<JsonElement>(d.SourceJson);
        var p = JsonSerializer.Deserialize<EWayPreparation>(d.BodyJson, EWayPreparationRules.Json)!;
        var validation = EWayPreparationRules.Validate(p, source);
        if (action == "RECORD_EXTERNAL" && validation.Issues.Length > 0) throw new ValidationException("Resolve preparation issues before recording an external EBN. Export a worksheet to review missing information.");
        await Write(id, input.OperationId, input.ExpectedVersion, action, d.SourceType, d.SourceId, d.RegistrationId,
            action == "RECORD_EXTERNAL" ? new { ebn = input.Ebn, evidence = input.Evidence } : (object)new { rulesVersion = validation.RulesVersion });
        return new { validation, source, preparation = p, document = d };
    }
    public async Task<object> HistoryAsync(Guid id)
    {
        await GetAsync(id);
        await using var c = factory.CreateConnection();
        return await c.QueryAsync("SELECT operation_id \"operationId\",version,action,actor_id \"actorId\",created_at \"createdAt\",snapshot::text \"snapshotJson\" FROM eway_events WHERE document_id=@id ORDER BY created_at,operation_id", new { id });
    }
    public async Task<object> UsageAsync(DateTime from, DateTime to)
    {
        await RequireAsync("VIEW_USAGE");
        if (from == default || to == default || to < from || (to - from).TotalDays > 366 || to.Date == DateTime.MaxValue.Date) throw new ValidationException("Choose usage dates within one year.");
        await using var c = factory.CreateConnection();
        var local = await c.QueryAsync("SELECT action,count(*) count FROM eway_events WHERE created_at>=@from::date AND created_at<(@to::date+1) GROUP BY action ORDER BY action", new { from, to });
        return new { from, to, localOperations = local, officialGenerations = 0, providerRequests = 0, providerCharges = (decimal?)null,
            monthlyStockFlowCap = (int?)null, providerStatus = "No authorized adapter installed", note = "Local exports/references are not official generations. Provider costs are unknown, not zero." };
    }
}
