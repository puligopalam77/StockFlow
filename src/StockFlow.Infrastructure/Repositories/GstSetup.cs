using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Infrastructure.Repositories;

public sealed partial class GstRepository
{
    private static async Task<long> InsertOrReuse(NpgsqlConnection c,string table,string key,object body,string insertSql)
    {
        if(c.State!=ConnectionState.Open)await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        var value=JsonSerializer.SerializeToNode(body,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower})!.AsObject();
        value.Remove(key);value.Remove("updated_at");value.Remove("created_at");value.Remove("created_by");
        if(table=="gst_tax_configurations"&&!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_tax_rules') IS NOT NULL",transaction:tx))
            foreach(var field in new[]{"store_id","variant_id","rule_id","change_reason"})value.Remove(field);
        foreach(var date in new[]{"effective_from","effective_to"})if(value[date] is not null)
            value[date]=DateTime.Parse(value[date]!.ToString(),System.Globalization.CultureInfo.InvariantCulture).ToString("yyyy-MM-dd");
        if(value["company_settings_id"] is null)value.Remove("company_settings_id");
        // table/key are private constants supplied only by the four existing save methods.
        var existing=await c.ExecuteScalarAsync<long?>("SELECT "+key+" FROM "+table+" t WHERE to_jsonb(t) @> CAST(@payload AS jsonb) ORDER BY "+key+" LIMIT 1",new{payload=value.ToJsonString()},tx);
        var id=existing??await c.ExecuteScalarAsync<long>(insertSql,body,tx);await tx.CommitAsync();return id;
    }
    private static string MaskIdentity(string value)=>value.Length<6?"••••":value[..2]+new string('•',value.Length-5)+value[^3..];
    private static string? MaskAudit(string? json)
    {
        if(json is null)return null;
        var value=JsonNode.Parse(json);
        void Mask(JsonNode? item)
        {
            if(item is JsonObject o)foreach(var key in o.Select(p=>p.Key).ToArray())
            {
                if(key is "gstin" or "pan" && o[key] is JsonValue v && v.TryGetValue<string>(out var text))o[key]=MaskIdentity(text);
                else Mask(o[key]);
            }
            else if(item is JsonArray a)foreach(var child in a)Mask(child);
        }
        Mask(value);return value?.ToJsonString();
    }
    public async Task<GstSetupReadiness> SetupReadinessAsync(long registrationId)
    {
        await using var c=factory.CreateConnection();await RequireAsync(c,"VIEW");factory.RequireLocation();
        await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock_shared(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        var result=await Readiness(c,tx,registrationId);await tx.CommitAsync();return result;
    }
    private static async Task<GstSetupReadiness> Readiness(NpgsqlConnection c,NpgsqlTransaction tx,long id)
    {
        var today=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE",transaction:tx);
        var scope=await c.QuerySingleAsync<(long Store,string Profile)>("SELECT sf_store(),sf_visible_business_profile()",transaction:tx);
        var result=new GstSetupReadiness{RegistrationId=id,StoreId=scope.Store,BusinessProfileCode=scope.Profile,Today=today.ToString("yyyy-MM-dd")};
        void Check(string key,string label,bool valid,string error,string step)=>result.Checks.Add(new(key,label,valid?"pass":"error",valid?"Saved configuration validated.":error,step));
        void Warn(string key,string label,string message,string step)=>result.Checks.Add(new(key,label,"warning",message,step));
        var r=await c.QuerySingleOrDefaultAsync<GstRegistration>("SELECT registration_id RegistrationId,business_id BusinessId,gstin,registration_type RegistrationType,other_type OtherType,state_code StateCode,status,effective_from EffectiveFrom,effective_to EffectiveTo,address,authorized_contact AuthorizedContact,updated_at UpdatedAt FROM gst_registrations WHERE registration_id=@id",new{id},tx);
        var b=r is null?null:await c.QuerySingleOrDefaultAsync<GstBusiness>("SELECT business_id BusinessId,company_settings_id CompanySettingsId,legal_name LegalName,trade_name TradeName,constitution,pan,address,state_code StateCode,pin_code PinCode,mobile,email,updated_at UpdatedAt FROM gst_legal_businesses WHERE business_id=@id",new{id=r.BusinessId},tx);
        bool Valid(Action validate){try{validate();return true;}catch(ValidationException){return false;}}
        Check("business","Business details complete",b is not null&&Valid(()=>GstRules.Validate(b)),"Save valid legal business details first.","businesses");
        Check("registration","Registration details valid",r is not null&&Valid(()=>GstRules.Validate(r))&&(r.Gstin is null||b?.Pan is null||r.Gstin.Substring(2,10)==b.Pan),"Save a registration with valid identity, state, PAN and dates.","registrations");
        Check("registrationDates","Registration active today",r is not null&&r.Status=="ACTIVE"&&r.EffectiveFrom<=today&&(r.EffectiveTo is null||r.EffectiveTo>=today),"The registration must be active and effective today before activation.","registrations");
        if(r?.Gstin is not null)Warn("portal","GST Portal status not verified","Format and checksum checks do not confirm GST Portal registration.","registrations");
        Check("scheme","Supported billing scheme",r is not null&&r.RegistrationType!="OTHER","This alternative scheme needs a separately verified issuance workflow.","registrations");
        var maps=(await c.QueryAsync<GstMapping>("SELECT mapping_id MappingId,registration_id RegistrationId,business_profile_code BusinessProfileCode,store_id StoreId,effective_from EffectiveFrom,effective_to EffectiveTo,status,updated_at UpdatedAt FROM gst_shop_mappings WHERE store_id=sf_store() AND business_profile_code=sf_visible_business_profile() ORDER BY mapping_id",transaction:tx)).ToArray();
        var candidates=maps.Where(m=>m.RegistrationId==id&&m.EffectiveFrom<=today&&(m.EffectiveTo is null||m.EffectiveTo>=today)).ToArray();
        var active=candidates.Where(m=>m.Status=="ACTIVE").ToArray();
        var candidate=active.Length==1?active[0]:candidates.Length==1?candidates[0]:null;
        Check("mapping","Selected shop assignment saved",candidate is not null,"Save one unambiguous assignment effective today for the selected shop and profile.","mappings");
        Check("conflicts","No conflicting assignment dates",candidate is not null&&!maps.Any(m=>m.MappingId!=candidate.MappingId&&m.Status=="ACTIVE"&&m.EffectiveFrom<=(candidate.EffectiveTo??DateTime.MaxValue)&&(m.EffectiveTo??DateTime.MaxValue)>=candidate.EffectiveFrom),"Review overlapping active assignments for this shop.","mappings");
        Check("mappingDates","Assignment within registration dates",candidate is not null&&r is not null&&candidate.EffectiveFrom>=r.EffectiveFrom&&(r.EffectiveTo is null||candidate.EffectiveTo is not null&&candidate.EffectiveTo<=r.EffectiveTo),"Assignment dates must be within the registration's effective dates.","mappings");
        var products=(await c.QueryAsync<GstProduct>("SELECT product_id ProductId,product_name ProductName,hsn_code HsnCode,unit_of_measure UnitOfMeasure,tax_percentage TaxPercentage FROM products WHERE is_active AND identifier_profile=sf_visible_business_profile() ORDER BY product_id",transaction:tx)).ToArray();
        var scalable=await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_tax_rules') IS NOT NULL",transaction:tx);
        var taxSql=scalable?TaxSelect+" WHERE business_profile_code=sf_visible_business_profile() ORDER BY tax_config_id":"SELECT tax_config_id TaxConfigId,product_id ProductId,classification,hsn_sac HsnSac,uqc,treatment,tax_rate TaxRate,cess_rate CessRate,reverse_charge ReverseCharge,prices_include_tax PricesIncludeTax,effective_from EffectiveFrom,effective_to EffectiveTo,review_reference ReviewReference FROM gst_tax_configurations WHERE business_profile_code=sf_visible_business_profile() ORDER BY tax_config_id";
        var taxes=(await c.QueryAsync<GstTaxConfiguration>(taxSql,transaction:tx)).ToArray();
        var missing=new List<string>();var unsupported=new List<string>();
        foreach(var product in products)
        {
            var versions=taxes.Where(t=>t.ProductId==product.ProductId&&t.VariantId is null&&t.EffectiveFrom<=today&&(t.EffectiveTo is null||t.EffectiveTo>=today)).OrderByDescending(t=>t.StoreId.HasValue).Take(scalable?1:2).ToArray();
            if(versions.Length!=1||!Valid(()=>GstRules.Validate(versions[0])))missing.Add(product.ProductName);
            else if(versions[0].Classification!="GOODS"||versions[0].Treatment=="ZERO_RATED"||versions[0].ReverseCharge||versions[0].CessRate!=0)unsupported.Add(product.ProductName);
        }
        Check("taxes","Product tax versions configured",missing.Count==0,"Review current tax versions for: "+string.Join(", ",missing.Take(5))+(missing.Count>5?"…":""),"taxes");
        Check("supportedTaxes","Tax versions supported by checkout",unsupported.Count==0,"These products need additional verified issuance support: "+string.Join(", ",unsupported.Take(5)),"taxes");
        if(products.Length==0)Warn("products","No active products yet","Add and classify products before receiving or billing them. No product tax readiness is claimed yet.","taxes");
        var company=await c.QuerySingleAsync<StockFlow.Application.DTOs.Invoices.CompanySettings>(CompanySettingsRepository.ReadSql,transaction:tx);
        var pricing=company.DefaultTaxMode;
        if(scalable){var variantQuery=CatalogParameters(new GstCatalogQuery{Variants=true});var unresolved=await c.ExecuteScalarAsync<long>(CatalogSql+"SELECT count(*) FROM filtered WHERE \"MappingStatus\"<>'Configured'",variantQuery,tx);Check("variantTaxes","Sellable variant mappings supported",unresolved==0,"Review missing, expired or unsupported variant classifications in Product Tax Mapping.","taxes");}
        Check("productPricing","Product pricing overrides agree with billing",taxes.Where(t=>t.StoreId is not null&&t.EffectiveFrom<=today&&(t.EffectiveTo is null||t.EffectiveTo>=today)).All(t=>t.PricesIncludeTax is null||t.PricesIncludeTax==(pricing=="INCLUSIVE")),"Review product pricing overrides: the current checkout requires them to agree with the bill price mode.","taxes");
        Check("pricing","Billing price setting available",pricing is "INCLUSIVE" or "EXCLUSIVE","Review the existing Business & Billing price setting.","taxes");
        result.CanActivate=result.Checks.All(x=>x.Status!="error");result.Ready=result.CanActivate&&candidate?.Status=="ACTIVE";
        if(candidate?.Status=="INACTIVE")Warn("activation","Assignment awaiting activation","Activate after reviewing the saved details. Existing mapping history enables setup checks; complete this setup before billing.","mappings");
        result.Fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{today,scope.Store,scope.Profile,b,r,maps,products,taxes,pricing}))));
        return result;
    }
    public async Task<GstSetupReadiness> ActivateSetupAsync(GstActivateSetup request)
    {
        if(request.RegistrationId<=0||string.IsNullOrEmpty(request.Fingerprint)||request.Fingerprint.Length!=64)throw new ValidationException("Review the saved setup before activation.");
        factory.RequireLocation();await using var c=factory.CreateConnection();await RequireAsync(c,"EDIT");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c,tx);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        var current=await Readiness(c,tx,request.RegistrationId);
        if(current.Ready){await tx.CommitAsync();return current;}
        if(!current.CanActivate||current.Fingerprint!=request.Fingerprint)throw new ValidationException("GST setup is incomplete or changed since review. Refresh the review and resolve its blocking items.");
        var changed=await c.ExecuteAsync("""
            UPDATE gst_shop_mappings SET status='ACTIVE',updated_at=now() WHERE registration_id=@RegistrationId
            AND store_id=sf_store() AND business_profile_code=sf_visible_business_profile() AND status='INACTIVE'
            AND CURRENT_DATE BETWEEN effective_from AND coalesce(effective_to,'infinity')
            """,request,tx);
        if(changed!=1)throw new ValidationException("Review the selected shop assignment before activation.");
        var result=await Readiness(c,tx,request.RegistrationId);if(!result.Ready)throw new ValidationException("Setup could not be activated. Review the saved configuration.");
        await tx.CommitAsync();return result;
    }
    public async Task<GstAuditPage> AuditAsync(GstAuditQuery q)
    {
        if(q.Page<1||q.Page>100000||!new[]{10,25,50,100}.Contains(q.PageSize)||q.From.HasValue!=q.To.HasValue||q.From>q.To||q.To?.Date==DateTime.MaxValue.Date
           ||q.RegistrationId<=0||q.StoreId<=0||q.ActorId<=0||q.Action is not(null or "INSERT" or "UPDATE"))throw new ValidationException("Choose valid audit filters and pagination.");
        await using var c=factory.CreateConnection();await RequireAsync(c,"VIEW");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        const string selected="""
            FROM gst_audit_history a LEFT JOIN users u ON u.tenant_id=a.tenant_id AND u.user_id=a.actor_id
            WHERE (@from::date IS NULL OR a.created_at>=@from::date AND a.created_at<@to::date+INTERVAL '1 day')
            AND (@actor IS NULL OR a.actor_id=@actor) AND (@action IS NULL OR a.action=@action)
            AND (@store::bigint IS NULL OR a.entity='gst_shop_mappings' AND ((a.new_value->>'store_id')::bigint=@store OR (a.old_value->>'store_id')::bigint=@store))
            AND (@registration::bigint IS NULL OR (a.entity='gst_registrations' AND a.record_id=@registration)
             OR (a.entity='gst_shop_mappings' AND ((a.new_value->>'registration_id')::bigint=@registration OR (a.old_value->>'registration_id')::bigint=@registration))
             OR (a.entity='gst_legal_businesses' AND EXISTS(SELECT 1 FROM gst_registrations r WHERE r.registration_id=@registration AND r.business_id=a.record_id)))
            """;
        var args=new{from=q.From?.Date,to=q.To?.Date,actor=q.ActorId,action=q.Action,store=q.StoreId,registration=q.RegistrationId,limit=q.PageSize,offset=(q.Page-1)*q.PageSize};
        var count=await c.ExecuteScalarAsync<long>("SELECT count(*) "+selected,args,tx);
        var rows=(await c.QueryAsync<GstAuditEntry>("SELECT a.audit_id AuditId,a.entity,a.record_id RecordId,a.action,a.actor_id ActorId,u.full_name ActorName,a.created_at CreatedAt,a.old_value::text OldValue,a.new_value::text NewValue "+selected+" ORDER BY a.audit_id DESC LIMIT @limit OFFSET @offset",args,tx)).ToArray();
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('EDIT')",transaction:tx))foreach(var row in rows){row.OldValue=MaskAudit(row.OldValue);row.NewValue=MaskAudit(row.NewValue);}
        await tx.CommitAsync();return new(count,rows);
    }
}
