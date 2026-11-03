using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Infrastructure.Repositories;

public sealed partial class GstRepository
{
    internal static async Task AssignRegistrationRule(NpgsqlConnection c,NpgsqlTransaction tx,int productId,long ruleId)
    {
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('EDIT') AND sf_can_location(sf_location())",transaction:tx))throw new UnauthorizedAccessException("GST mapping permission required to select a rule.");
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_tax_rules') IS NOT NULL",transaction:tx))throw new ValidationException("Apply migration 073 before selecting a reusable tax rule.");
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        var rule=(await c.QuerySingleOrDefaultAsync<RuleRecord>(RuleSelect+" WHERE rule_id=@ruleId AND review_status='REVIEWED' AND status='ACTIVE' AND CURRENT_DATE BETWEEN effective_from AND coalesce(effective_to,'infinity')",new{ruleId},tx))?.Model()??throw new ValidationException("Choose a reviewed rule effective today in this shop.");
        var hsn=await c.ExecuteScalarAsync<string?>("SELECT hsn_code FROM products WHERE product_id=@productId",new{productId},tx);
        if(GstRules.Clean(hsn)!=rule.Tax.HsnSac)throw new ValidationException("Product HSN must match the selected reviewed rule. Review the classification before saving.");
        var existing=await c.QueryAsync<GstTaxConfiguration>(TaxSelect+" WHERE product_id=@productId AND variant_id IS NULL AND CURRENT_DATE BETWEEN effective_from AND coalesce(effective_to,'infinity')",new{productId},tx);
        if(existing.Any()){if(existing.Any(t=>t.RuleId==ruleId))return;throw new ValidationException("This product already has a tax classification. Schedule reviewed changes from GST Settings to preserve its history.");}
        var tax=rule.Tax;tax.ProductId=productId;tax.RuleId=ruleId;tax.EffectiveFrom=await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE",transaction:tx);
        await c.ExecuteAsync("""
        INSERT INTO gst_tax_configurations(store_id,business_profile_code,product_id,rule_id,classification,hsn_sac,uqc,treatment,tax_rate,cess_rate,reverse_charge,prices_include_tax,effective_from,effective_to,review_reference,change_reason)
        VALUES(sf_store(),sf_visible_business_profile(),@ProductId,@RuleId,@Classification,@HsnSac,@Uqc,@Treatment,@TaxRate,@CessRate,@ReverseCharge,@PricesIncludeTax,@EffectiveFrom,@EffectiveTo,@ReviewReference,'Reviewed rule selected during product registration')
        """,tax,tx);
    }
    private static readonly JsonSerializerOptions CatalogJson=new(JsonSerializerDefaults.Web);
    private static string CatalogSerialize(object value)=>JsonSerializer.Serialize(value,CatalogJson);
    private async Task CatalogPermission(NpgsqlConnection c,string action="VIEW")
    {
        factory.RequireLocation();await RequireAsync(c,action);
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_can_location(public.sf_location())"))throw new UnauthorizedAccessException("Choose an authorized shop location.");
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_tax_rules') IS NOT NULL"))throw new ValidationException("Apply migration 073 before managing catalog tax mappings.");
    }
    private static void CatalogQuery(GstCatalogQuery q)
    {
        if(q.Page<1||q.Page>100000||!new[]{10,25,50,100}.Contains(q.PageSize))throw new ValidationException("Choose a valid page and rows-per-page value.");q.Search=GstRules.Clean(q.Search);
        if(q.CategoryIds.Length>100||q.CategoryIds.Any(id=>id<=0)||q.Search?.Length>150||q.ParentCategoryId<=0||q.CategoryId<=0||q.BrandId<=0||q.ProductId<=0||q.Rate is <0 or >100)throw new ValidationException("Use valid catalog filters and a search of at most 150 characters.");
        if(q.Status is not (null or "" or "Configured" or "Needs Review" or "Missing" or "Expired" or "Conflict")||q.Treatment is not (null or "" or "TAXABLE" or "NIL" or "EXEMPT" or "NON_GST" or "ZERO_RATED")||q.Sort is not ("name" or "code" or "status"))throw new ValidationException("Choose valid tax mapping filters and sort order.");
        if(q.Date?.TimeOfDay!=null&&q.Date.Value.TimeOfDay!=TimeSpan.Zero)throw new ValidationException("Use a date without a time.");
    }
    // Parameters, never user-supplied SQL. One row per product, or one per active sellable variant.
    private const string CatalogSql="""
    WITH catalog AS (
     SELECT p.product_id "ProductId",v.variant_id "VariantId",p.product_name||CASE WHEN v.variant_id IS NOT NULL THEN ' / '||v.variant_name ELSE '' END "ProductName",
      coalesce(p.parent_product_code,p.sku) "ProductCode",p.sku "ProductSku",coalesce(v.sku,p.sku) "Sku",c.category_name "Category",pc.category_name "ParentCategory",b.brand_name "Brand",p.hsn_code "ProductHsn",
      t.hsn_sac "HsnSac",t.treatment "Treatment",t.tax_rate "TaxRate",r.rule_name "RuleName",t.rule_id "RuleId",t.tax_config_id "TaxConfigId",t.effective_from "EffectiveFrom",t.effective_to "EffectiveTo",t.prices_include_tax "PricesIncludeTax",t.review_reference "ReviewReference",
      CASE WHEN t.tax_config_id IS NULL THEN CASE WHEN EXISTS(SELECT 1 FROM gst_tax_configurations h WHERE h.product_id=p.product_id AND (h.store_id=(SELECT sf_store()) OR h.store_id IS NULL) AND (h.variant_id=v.variant_id OR h.variant_id IS NULL) AND h.effective_to<coalesce(@Date,CURRENT_DATE)) THEN 'Expired' ELSE 'Missing' END
       WHEN length(btrim(t.review_reference))<3 OR t.classification<>'GOODS' OR t.treatment='ZERO_RATED' OR t.cess_rate<>0 OR t.reverse_charge OR (t.prices_include_tax IS NOT NULL AND t.prices_include_tax IS DISTINCT FROM (SELECT coalesce(profile->>'DefaultTaxMode','EXCLUSIVE')='INCLUSIVE' FROM company_settings WHERE company_settings_id=1)) OR EXISTS(SELECT 1 FROM gst_tax_configurations ov WHERE ov.product_id=p.product_id AND ov.store_id=(SELECT sf_store()) AND ov.variant_id IS NOT NULL AND coalesce(@Date,CURRENT_DATE) BETWEEN ov.effective_from AND coalesce(ov.effective_to,'infinity') AND (ov.classification<>'GOODS' OR ov.treatment='ZERO_RATED' OR ov.reverse_charge OR ov.cess_rate<>0)) OR (t.rule_id IS NOT NULL AND (r.rule_id IS NULL OR r.review_status<>'REVIEWED' OR r.status<>'ACTIVE' OR coalesce(@Date,CURRENT_DATE) NOT BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity'))) THEN 'Needs Review' ELSE 'Configured' END "MappingStatus",
      CASE WHEN t.store_id IS NULL AND t.tax_config_id IS NOT NULL THEN 'Legacy profile mapping' WHEN t.variant_id IS NOT NULL THEN 'Variant override' WHEN v.variant_id IS NOT NULL THEN 'Inherited product default' ELSE 'Product default' END "MappingSource",
      (SELECT count(*)::integer FROM product_variants pv WHERE pv.tenant_id=p.tenant_id AND pv.product_id=p.product_id AND pv.is_active) "VariantCount",
      (SELECT count(DISTINCT ov.variant_id)::integer FROM gst_tax_configurations ov WHERE ov.product_id=p.product_id AND ov.store_id=(SELECT sf_store()) AND ov.variant_id IS NOT NULL AND coalesce(@Date,CURRENT_DATE) BETWEEN ov.effective_from AND coalesce(ov.effective_to,'infinity')) "OverrideCount",
      md5(p.xmin::text||coalesce((SELECT string_agg(to_jsonb(h)::text,',' ORDER BY h.tax_config_id) FROM gst_tax_configurations h WHERE h.product_id=p.product_id AND (h.store_id=(SELECT sf_store()) OR h.store_id IS NULL) AND (h.variant_id IS NULL OR h.variant_id=v.variant_id)),'')) "Fingerprint",
      c.category_id category_id,c.parent_category_id parent_id,p.brand_id brand_id,
      EXISTS(SELECT 1 FROM product_variants sv WHERE sv.product_id=p.product_id AND sv.sku ILIKE '%'||@Search||'%') sku_match
     FROM products p JOIN categories c USING(tenant_id,category_id) LEFT JOIN categories pc ON pc.tenant_id=c.tenant_id AND pc.category_id=c.parent_category_id
     LEFT JOIN brands b ON b.tenant_id=p.tenant_id AND b.brand_id=p.brand_id
     LEFT JOIN product_variants v ON @Variants AND v.tenant_id=p.tenant_id AND v.product_id=p.product_id AND v.is_active
     LEFT JOIN LATERAL (SELECT * FROM gst_tax_configurations m WHERE m.product_id=p.product_id AND (m.store_id=(SELECT sf_store()) OR m.store_id IS NULL AND NOT EXISTS(SELECT 1 FROM gst_tax_configurations scoped WHERE scoped.product_id=p.product_id AND scoped.store_id=(SELECT sf_store()) AND (scoped.variant_id=v.variant_id OR scoped.variant_id IS NULL) AND scoped.effective_from<=coalesce(@Date,CURRENT_DATE))) AND (m.variant_id=v.variant_id OR m.variant_id IS NULL AND NOT EXISTS(SELECT 1 FROM gst_tax_configurations overridden WHERE overridden.product_id=p.product_id AND overridden.store_id=(SELECT sf_store()) AND overridden.variant_id=v.variant_id AND overridden.effective_from<=coalesce(@Date,CURRENT_DATE))) AND coalesce(@Date,CURRENT_DATE) BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') ORDER BY (m.variant_id IS NOT NULL) DESC,(m.store_id IS NOT NULL) DESC LIMIT 1) t ON true
     LEFT JOIN gst_tax_rules r ON r.rule_id=t.rule_id
     WHERE p.is_active AND p.identifier_profile=(SELECT sf_visible_business_profile()) AND (@ProductId IS NULL OR p.product_id=@ProductId)
    ), filtered AS (SELECT * FROM catalog WHERE (@Search IS NULL OR "ProductName" ILIKE '%'||@Search||'%' OR "ProductCode" ILIKE '%'||@Search||'%' OR "Sku" ILIKE '%'||@Search||'%' OR "HsnSac" ILIKE '%'||@Search||'%' OR "ProductHsn" ILIKE '%'||@Search||'%' OR "Category" ILIKE '%'||@Search||'%' OR "ParentCategory" ILIKE '%'||@Search||'%' OR "Brand" ILIKE '%'||@Search||'%' OR sku_match)
     AND (@ParentCategoryId IS NULL OR category_id=@ParentCategoryId OR parent_id=@ParentCategoryId) AND (@CategoryId IS NULL OR category_id=@CategoryId) AND (cardinality(@CategoryIds)=0 OR category_id=ANY(@CategoryIds)) AND (@BrandId IS NULL OR brand_id=@BrandId)
     AND (@Treatment IS NULL OR @Treatment='' OR "Treatment"=@Treatment) AND (@Rate IS NULL OR "TaxRate"=@Rate) AND (@Status IS NULL OR @Status='' OR "MappingStatus"=@Status) AND (NOT @NeedsAttention OR "MappingStatus"<>'Configured'))
    """;
    private static string CatalogOrder(GstCatalogQuery q)=>q.Sort switch{"code"=>"\"ProductCode\",\"ProductId\",\"VariantId\"","status"=>"\"MappingStatus\",\"ProductId\",\"VariantId\"",_=>"\"ProductName\",\"ProductId\",\"VariantId\""};
    private static DynamicParameters CatalogParameters(GstCatalogQuery q)
    {
        var p=new DynamicParameters(q);p.Add("Search",q.Search,DbType.String);p.Add("Date",q.Date,DbType.Date);p.Add("Rate",q.Rate,DbType.Decimal);
        foreach(var name in new[]{"ParentCategoryId","CategoryId","BrandId","ProductId"})p.Add(name,(int?)typeof(GstCatalogQuery).GetProperty(name)!.GetValue(q),DbType.Int32);
        p.Add("Treatment",q.Treatment,DbType.String);p.Add("Status",q.Status,DbType.String);return p;
    }
    public async Task<GstCatalogPage> CatalogAsync(GstCatalogQuery q)
    {
        CatalogQuery(q);await using var c=factory.CreateConnection();await CatalogPermission(c);
        await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var p=CatalogParameters(q);p.Add("Offset",(q.Page-1L)*q.PageSize);p.Add("Size",q.PageSize);
        using var reader=await c.QueryMultipleAsync(CatalogSql+"SELECT count(*) FROM filtered;"+CatalogSql+"SELECT * FROM filtered ORDER BY "+CatalogOrder(q)+" OFFSET @Offset LIMIT @Size;"+CatalogSql+"SELECT count(*) Total,count(*) FILTER(WHERE \"MappingStatus\"='Configured') Configured,count(*) FILTER(WHERE \"MappingStatus\" IN('Needs Review','Conflict')) NeedsReview,count(*) FILTER(WHERE \"MappingStatus\" IN('Missing','Expired')) Missing FROM catalog",p,tx);
        var total=await reader.ReadSingleAsync<long>();var rows=(await reader.ReadAsync<GstCatalogRow>()).ToArray();var summary=await reader.ReadSingleAsync<GstCatalogSummary>();reader.Dispose();await tx.CommitAsync();return new(total,rows,summary);
    }
    public async Task<GstCatalogOptions> CatalogOptionsAsync()
    {
        await using var c=factory.CreateConnection();await CatalogPermission(c);
        var categories=await c.QueryAsync<GstChoice>("SELECT category_id Id,category_name Name,parent_category_id ParentId FROM categories WHERE business_profile_code=sf_visible_business_profile() AND is_active ORDER BY category_name");
        var brands=await c.QueryAsync<GstChoice>("SELECT brand_id Id,brand_name Name,NULL::integer ParentId FROM brands WHERE is_active ORDER BY brand_name");
        var permissions=new Dictionary<string,bool>();foreach(var a in new[]{"EDIT","APPROVE_TAX","OVERRIDE_TAX","BULK_TAX","IMPORT_TAX","EXPORT_TAX"})permissions[a]=await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed(@a)",new{a});
        return new(categories,brands,permissions,"Existing Business & Billing defaults apply to the selected business profile; they are not separate defaults for each physical branch.");
    }
    private const string RuleSelect="SELECT rule_id RuleId,rule_name RuleName,description,tax::text TaxJson,review_status ReviewStatus,status,notes,reviewed_by ReviewedBy,reviewed_at ReviewedAt FROM gst_tax_rules";
    private sealed class RuleRecord
    {
        public long RuleId{get;set;} public string RuleName{get;set;}=""; public string Description{get;set;}=""; public string TaxJson{get;set;}="{}"; public string ReviewStatus{get;set;}="";public string Status{get;set;}="";public string? Notes{get;set;}public int? ReviewedBy{get;set;}public DateTime? ReviewedAt{get;set;}
        public GstTaxRule Model()=>new(){RuleId=RuleId,RuleName=RuleName,Description=Description,Tax=JsonSerializer.Deserialize<GstTaxConfiguration>(TaxJson,CatalogJson)!,ReviewStatus=ReviewStatus,Status=Status,Notes=Notes,ReviewedBy=ReviewedBy,ReviewedAt=ReviewedAt};
    }
    public async Task<GstRulePage> TaxRulesAsync(GstCatalogQuery q)
    {
        CatalogQuery(q);await using var c=factory.CreateConnection();await CatalogPermission(c);var p=CatalogParameters(q);p.Add("Offset",(q.Page-1L)*q.PageSize);p.Add("Size",q.PageSize);
        const string where=" WHERE (@Search IS NULL OR rule_name ILIKE '%'||@Search||'%' OR description ILIKE '%'||@Search||'%' OR tax->>'hsnSac' ILIKE '%'||@Search||'%') AND (NOT @ReviewedOnly OR review_status='REVIEWED' AND status='ACTIVE') AND (NOT @CurrentOnly OR CURRENT_DATE BETWEEN effective_from AND coalesce(effective_to,'infinity'))";
        var total=await c.ExecuteScalarAsync<long>("SELECT count(*) FROM gst_tax_rules"+where,p);var rows=(await c.QueryAsync<RuleRecord>(RuleSelect+where+" ORDER BY rule_name,effective_from DESC,rule_id OFFSET @Offset LIMIT @Size",p)).Select(x=>x.Model()).ToArray();
        var counts=await c.QueryAsync<(long Id,long Count)>("SELECT rule_id,count(DISTINCT product_id) FROM gst_tax_configurations WHERE rule_id=ANY(@ids) GROUP BY rule_id",new{ids=rows.Select(r=>r.RuleId).ToArray()});foreach(var count in counts)rows.First(r=>r.RuleId==count.Id).ProductsAssigned=count.Count;return new(total,rows);
    }
    public async Task<long> SaveRuleAsync(GstTaxRule rule)
    {
        rule.RuleName=rule.RuleName.Trim();rule.Description=rule.Description.Trim();rule.Tax.ProductId=1;GstRules.Validate(rule.Tax);
        if(rule.RuleName.Length is <3 or >150||rule.Description.Length is <3 or >2000||rule.Notes?.Length>2000||rule.Status is not ("ACTIVE" or "INACTIVE")||rule.RuleId<0||rule.ReviewStatus!="DRAFT")throw new ValidationException("Create or edit a draft rule version with a name, classification description, dates and reference. Reviewed versions cannot be edited.");
        await using var c=factory.CreateConnection();await CatalogPermission(c,"EDIT");
        if(rule.RuleId>0)return await c.QuerySingleOrDefaultAsync<long?>("UPDATE gst_tax_rules SET rule_name=@RuleName,description=@Description,tax=CAST(@tax AS jsonb),effective_from=@EffectiveFrom,effective_to=@EffectiveTo,status=@Status,notes=@Notes WHERE rule_id=@RuleId AND review_status='DRAFT' RETURNING rule_id",new{rule.RuleId,rule.RuleName,rule.Description,tax=CatalogSerialize(rule.Tax),rule.Tax.EffectiveFrom,rule.Tax.EffectiveTo,rule.Status,rule.Notes})??throw new ValidationException("Only an available draft rule may be edited. Create a new version of a reviewed rule.");
        return await c.ExecuteScalarAsync<long>("INSERT INTO gst_tax_rules(rule_name,description,tax,effective_from,effective_to,status,notes) VALUES(@RuleName,@Description,CAST(@tax AS jsonb),@EffectiveFrom,@EffectiveTo,@Status,@Notes) RETURNING rule_id",new{rule.RuleName,rule.Description,tax=CatalogSerialize(rule.Tax),rule.Tax.EffectiveFrom,rule.Tax.EffectiveTo,rule.Status,rule.Notes});
    }
    public async Task ReviewRuleAsync(long ruleId)
    {
        await using var c=factory.CreateConnection();await CatalogPermission(c,"EDIT");await RequireAsync(c,"APPROVE_TAX");
        if(await c.ExecuteAsync("UPDATE gst_tax_rules SET review_status='REVIEWED' WHERE rule_id=@ruleId AND review_status='DRAFT'",new{ruleId})==0&&!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM gst_tax_rules WHERE rule_id=@ruleId AND review_status='REVIEWED')",new{ruleId}))throw new ValidationException("Rule is unavailable in this shop.");
    }
    public async Task EndRuleAsync(GstRuleEnd request)
    {
        GstRules.Dates(request.EffectiveTo,null);if(!request.Confirm||GstRules.Clean(request.Reason)?.Length is not (>=3 and <=1000))throw new ValidationException("Confirm the prospective rule end date with a reason.");
        await using var c=factory.CreateConnection();await CatalogPermission(c,"EDIT");await RequireAsync(c,"APPROVE_TAX");await RequireAsync(c,"OVERRIDE_TAX");await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,tx);
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
        if(request.EffectiveTo<await c.ExecuteScalarAsync<DateTime>("SELECT CURRENT_DATE",transaction:tx)||!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM gst_tax_rules WHERE rule_id=@RuleId AND review_status='REVIEWED' AND effective_from<=@EffectiveTo AND (effective_to IS NULL OR effective_to>=@EffectiveTo))",request,tx))throw new ValidationException("Choose a prospective shortening of this reviewed rule's validity.");
        if(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM gst_tax_configurations WHERE rule_id=@RuleId AND effective_from>@EffectiveTo)",request,tx))throw new ValidationException("Future mappings start after this end date. Resolve those mappings before shortening the rule.");
        await c.ExecuteAsync("UPDATE gst_tax_configurations SET effective_to=@EffectiveTo WHERE rule_id=@RuleId AND (effective_to IS NULL OR effective_to>@EffectiveTo)",request,tx);
        await c.ExecuteAsync("UPDATE gst_tax_rules SET effective_to=@EffectiveTo,tax=jsonb_set(tax,'{effectiveTo}',to_jsonb(to_char(CAST(@EffectiveTo AS date),'YYYY-MM-DD')||'T00:00:00')),notes=concat_ws(E'\\n',notes,'End-date reason: '||@Reason) WHERE rule_id=@RuleId",request,tx);await tx.CommitAsync();
    }
    private const string TaxSelect="SELECT tax_config_id TaxConfigId,product_id ProductId,store_id StoreId,variant_id VariantId,rule_id RuleId,classification,hsn_sac HsnSac,uqc,treatment,tax_rate TaxRate,cess_rate CessRate,reverse_charge ReverseCharge,prices_include_tax PricesIncludeTax,effective_from EffectiveFrom,effective_to EffectiveTo,review_reference ReviewReference,change_reason ChangeReason,created_by CreatedBy,created_at CreatedAt FROM gst_tax_configurations";
    public async Task<GstMappingHistory> MappingHistoryAsync(int productId,long? variantId)
    {
        await using var c=factory.CreateConnection();await CatalogPermission(c);
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM products WHERE product_id=@productId AND identifier_profile=sf_visible_business_profile())",new{productId}))throw new ValidationException("Product is unavailable in this shop.");
        var versions=await c.QueryAsync<GstTaxConfiguration>(TaxSelect+" WHERE product_id=@productId AND (variant_id IS NULL OR variant_id=@variantId) ORDER BY effective_from DESC,tax_config_id DESC",new{productId,variantId});
        var audit=await c.QueryAsync<GstAuditEntry>("SELECT audit_id AuditId,entity,record_id RecordId,action,actor_id ActorId,created_at CreatedAt,old_value::text OldValue,new_value::text NewValue FROM gst_audit_history WHERE entity='gst_tax_configurations' AND (new_value->>'product_id')::integer=@productId ORDER BY audit_id DESC LIMIT 200",new{productId});return new(versions,audit);
    }
    private static async Task<long> SaveScopedTax(NpgsqlConnection c,GstTaxConfiguration t)
    {
        if(t.TaxConfigId!=0)
        {
            if(c.State!=ConnectionState.Open)await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
            await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('gst-config:'||sf_tenant()::text,0))",transaction:tx);
            var id=await c.QuerySingleOrDefaultAsync<long?>("UPDATE gst_tax_configurations SET effective_to=@EffectiveTo WHERE tax_config_id=@TaxConfigId RETURNING tax_config_id",t,tx)??throw new ValidationException("This tax mapping is unavailable in the selected shop.");await tx.CommitAsync();return id;
        }
        t.CreatedAt=null;t.CreatedBy=null;t.StoreId=await c.ExecuteScalarAsync<long>("SELECT sf_store()");
        return await InsertOrReuse(c,"gst_tax_configurations","tax_config_id",t,"""
        INSERT INTO gst_tax_configurations(store_id,business_profile_code,product_id,variant_id,rule_id,change_reason,classification,hsn_sac,uqc,treatment,tax_rate,cess_rate,reverse_charge,prices_include_tax,effective_from,effective_to,review_reference)
        VALUES(sf_store(),sf_visible_business_profile(),@ProductId,@VariantId,@RuleId,@ChangeReason,@Classification,@HsnSac,@Uqc,@Treatment,@TaxRate,@CessRate,@ReverseCharge,@PricesIncludeTax,@EffectiveFrom,@EffectiveTo,@ReviewReference) RETURNING tax_config_id
        """);
    }
    public async Task<GstExportResult> ImportTemplateAsync(GstCatalogQuery q)
    {
        CatalogQuery(q);await using var c=factory.CreateConnection();await CatalogPermission(c);await RequireAsync(c,"IMPORT_TAX");
        var rows=(await c.QueryAsync<GstCatalogRow>(CatalogSql+"SELECT * FROM filtered ORDER BY "+CatalogOrder(q)+" LIMIT 50001",CatalogParameters(q))).ToArray();
        if(rows.Length>50000)throw new ValidationException("Download at most 50,000 rows per template; narrow the filters.");
        var b=new StringBuilder(GstImportRow.TemplateHeader+"\r\n");
        // Only identifiers are prefilled. No category-derived HSN, percentage or unreviewed tax assumption.
        foreach(var r in rows)b.AppendLine(string.Join(',',new object?[]{r.ProductId,r.VariantId,r.ProductCode,r.ProductName,r.ProductSku,r.VariantId is null?null:r.Sku,null,null,null,null,null,null,null,null}.Select(SpreadsheetCell)));
        return new(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray(),"stockflow-tax-import-template.csv");
    }
    private static string SpreadsheetCell(object? value)
    {
        var text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        if(text.Length>0&&"=+-@\t\r\n".Contains(text[0]))text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
    private static bool SpreadsheetIdentifier(string? imported,string? actual)=>imported is null||imported==actual||actual is {Length:>0}&&"=+-@\t\r\n".Contains(actual[0])&&imported=="'"+actual;
    public async Task<GstExportResult> ExportMappingsAsync(GstCatalogQuery q,List<GstTarget>? targets=null)
    {
        CatalogQuery(q);await using var c=factory.CreateConnection();await CatalogPermission(c);await RequireAsync(c,"EXPORT_TAX");
        var rows=(await c.QueryAsync<GstCatalogRow>(CatalogSql+"SELECT * FROM filtered ORDER BY "+CatalogOrder(q)+" LIMIT 50001",CatalogParameters(q))).ToArray();if(rows.Length>50000)throw new ValidationException("Export at most 50,000 rows per file; narrow the filters.");
        if(targets is not null)rows=rows.Where(r=>targets.Contains(new(r.ProductId,r.VariantId))).ToArray();
        var b=new StringBuilder("ProductId,VariantId,ProductCode,ProductName,VariantSku,ParentCategory,Subcategory,Brand,HsnSac,RuleId,Treatment,TaxRate,PricesIncludeTax,EffectiveFrom,EffectiveTo,MappingStatus,ReviewReference\r\n");
        static string Csv(object? v){var s=Convert.ToString(v,CultureInfo.InvariantCulture)??"";if(s.Length>0&&"=+-@\t\r".Contains(s[0]))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
        foreach(var r in rows)b.AppendLine(string.Join(',',new object?[]{r.ProductId,r.VariantId,r.ProductCode,r.ProductName,r.VariantId is not null?r.Sku:null,r.ParentCategory,r.Category,r.Brand,r.HsnSac,r.RuleId,r.Treatment,r.TaxRate,r.PricesIncludeTax,r.EffectiveFrom?.ToString("yyyy-MM-dd"),r.EffectiveTo?.ToString("yyyy-MM-dd"),r.MappingStatus,r.ReviewReference}.Select(Csv)));
        return new(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray(),"stockflow-tax-mapping.csv");
    }
}
