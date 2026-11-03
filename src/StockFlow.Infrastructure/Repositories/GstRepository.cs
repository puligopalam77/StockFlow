using System.ComponentModel.DataAnnotations;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed partial class GstRepository(DbConnectionFactory factory) : IGstRepository
{
    private static async Task RequireAsync(Npgsql.NpgsqlConnection c,string action,string module="GST_SETTINGS")
    {
        if(!await c.ExecuteScalarAsync<bool>("SELECT public.sf_gst_allowed(@action,@module)",new{action,module})) throw new UnauthorizedAccessException("GST permission required.");
    }
    public async Task<GstSettings> SettingsAsync(bool catalog = true)
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_registrations') IS NOT NULL")) return new();
        await RequireAsync(c,"VIEW");
        // Explicit aliases keep snake_case mapping independent of process-wide Dapper settings.
        using var result=await c.QueryMultipleAsync("""
        SELECT business_id BusinessId,company_settings_id CompanySettingsId,legal_name LegalName,trade_name TradeName,constitution,pan,address,state_code StateCode,pin_code PinCode,mobile,email,updated_at UpdatedAt FROM gst_legal_businesses ORDER BY business_id;
        SELECT registration_id RegistrationId,business_id BusinessId,gstin,registration_type RegistrationType,other_type OtherType,state_code StateCode,status,effective_from EffectiveFrom,effective_to EffectiveTo,authorized_contact AuthorizedContact,address,updated_at UpdatedAt FROM gst_registrations ORDER BY registration_id;
        SELECT mapping_id MappingId,registration_id RegistrationId,business_profile_code BusinessProfileCode,store_id StoreId,effective_from EffectiveFrom,effective_to EffectiveTo,status,updated_at UpdatedAt FROM gst_shop_mappings ORDER BY mapping_id;
        SELECT tax_config_id TaxConfigId,product_id ProductId,classification,hsn_sac HsnSac,uqc,treatment,tax_rate TaxRate,cess_rate CessRate,reverse_charge ReverseCharge,prices_include_tax PricesIncludeTax,effective_from EffectiveFrom,effective_to EffectiveTo,review_reference ReviewReference FROM gst_tax_configurations WHERE @catalog ORDER BY product_id,effective_from DESC;
        SELECT store_id StoreId,store_name StoreName FROM stores WHERE is_active ORDER BY store_name;
        SELECT p.product_id ProductId,p.product_name ProductName,p.hsn_code HsnCode,p.unit_of_measure UnitOfMeasure,p.tax_percentage TaxPercentage FROM products p JOIN categories c USING(tenant_id,category_id) WHERE @catalog AND c.business_profile_code=public.sf_visible_business_profile() AND p.is_active ORDER BY p.product_name;
        SELECT audit_id AuditId,entity,record_id RecordId,action,actor_id ActorId,created_at CreatedAt,old_value::text OldValue,new_value::text NewValue FROM gst_audit_history ORDER BY audit_id DESC LIMIT 200;
        """,new{catalog});
        var settings=new GstSettings{Available=true,Businesses=(await result.ReadAsync<GstBusiness>()).ToArray(),Registrations=(await result.ReadAsync<GstRegistration>()).ToArray(),Mappings=(await result.ReadAsync<GstMapping>()).ToArray(),Taxes=(await result.ReadAsync<GstTaxConfiguration>()).ToArray(),Shops=(await result.ReadAsync<GstShop>()).ToArray(),Products=(await result.ReadAsync<GstProduct>()).ToArray(),History=(await result.ReadAsync<GstAudit>()).ToArray()};
        result.Dispose();
        settings.CatalogAvailable=await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_tax_rules') IS NOT NULL");
        settings.Company=await c.QuerySingleAsync<StockFlow.Application.DTOs.Invoices.CompanySettings>(CompanySettingsRepository.ReadSql);
        settings.Today=(await c.ExecuteScalarAsync<string>("SELECT CURRENT_DATE::text"))!;
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('EDIT')"))
        {
            foreach(var b in settings.Businesses)if(b.Pan is not null)b.Pan=MaskIdentity(b.Pan);
            foreach(var r in settings.Registrations)if(r.Gstin is not null)r.Gstin=MaskIdentity(r.Gstin);
            if(settings.Company.PanNumber is not null)settings.Company.PanNumber=MaskIdentity(settings.Company.PanNumber);
            if(settings.Company.GstNumber is not null)settings.Company.GstNumber=MaskIdentity(settings.Company.GstNumber);
            settings.History=settings.History.Select(a=>a with{OldValue=MaskAudit(a.OldValue),NewValue=MaskAudit(a.NewValue)}).ToArray();
        }
        return settings;
    }
    public async Task<long> SaveBusinessAsync(GstBusiness b)
    {
        GstRules.Validate(b);
        await using var c=factory.CreateConnection(); await RequireAsync(c,"EDIT");
        if(b.BusinessId==0) return await InsertOrReuse(c,"gst_legal_businesses","business_id",b,"""
        INSERT INTO gst_legal_businesses(company_settings_id,legal_name,trade_name,constitution,pan,address,state_code,pin_code,mobile,email)
        VALUES(@CompanySettingsId,@LegalName,@TradeName,@Constitution,@Pan,@Address,@StateCode,@PinCode,@Mobile,@Email) RETURNING business_id
        """);
        return await c.QuerySingleAsync<long>("""
        UPDATE gst_legal_businesses SET legal_name=@LegalName,trade_name=@TradeName,constitution=@Constitution,pan=@Pan,address=@Address,state_code=@StateCode,pin_code=@PinCode,mobile=@Mobile,email=@Email,updated_at=now()
        WHERE business_id=@BusinessId RETURNING business_id
        """,b);
    }
    public async Task<long> SaveRegistrationAsync(GstRegistration r)
    {
        GstRules.Validate(r);
        await using var c=factory.CreateConnection(); await RequireAsync(c,"EDIT");
        var pan=await c.QuerySingleOrDefaultAsync<string?>("SELECT pan FROM gst_legal_businesses WHERE business_id=@BusinessId",r);
        if(r.Gstin is not null && pan is not null && r.Gstin.Substring(2,10)!=pan) throw new ValidationException("GSTIN PAN must match the linked legal business.");
        if(r.RegistrationId==0) return await InsertOrReuse(c,"gst_registrations","registration_id",r,"""
        INSERT INTO gst_registrations(business_id,gstin,registration_type,other_type,state_code,status,effective_from,effective_to,authorized_contact,address)
        VALUES(@BusinessId,@Gstin,@RegistrationType,@OtherType,@StateCode,@Status,@EffectiveFrom,@EffectiveTo,@AuthorizedContact,@Address) RETURNING registration_id
        """);
        return await c.QuerySingleAsync<long>("""
        UPDATE gst_registrations SET business_id=@BusinessId,gstin=@Gstin,registration_type=@RegistrationType,other_type=@OtherType,state_code=@StateCode,status=@Status,effective_from=@EffectiveFrom,effective_to=@EffectiveTo,authorized_contact=@AuthorizedContact,address=@Address,updated_at=now()
        WHERE registration_id=@RegistrationId RETURNING registration_id
        """,r);
    }
    public async Task<long> SaveMappingAsync(GstMapping m)
    {
        GstRules.Validate(m);
        await using var c=factory.CreateConnection(); await RequireAsync(c,"EDIT");
        if(m.MappingId==0) return await InsertOrReuse(c,"gst_shop_mappings","mapping_id",m,"""
        INSERT INTO gst_shop_mappings(registration_id,business_profile_code,store_id,effective_from,effective_to,status)
        VALUES(@RegistrationId,@BusinessProfileCode,@StoreId,@EffectiveFrom,@EffectiveTo,@Status) RETURNING mapping_id
        """);
        return await c.QuerySingleAsync<long>("""
        UPDATE gst_shop_mappings SET registration_id=@RegistrationId,business_profile_code=@BusinessProfileCode,store_id=@StoreId,effective_from=@EffectiveFrom,effective_to=@EffectiveTo,status=@Status,updated_at=now()
        WHERE mapping_id=@MappingId RETURNING mapping_id
        """,m);
    }
    public async Task<long> SaveTaxAsync(GstTaxConfiguration t)
    {
        GstRules.Validate(t);
        await using var c=factory.CreateConnection(); await RequireAsync(c,"EDIT");
        if(await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_tax_rules') IS NOT NULL"))return await SaveScopedTax(c,t);
        if(t.RuleId is not null||t.VariantId is not null||t.StoreId is not null)throw new ValidationException("Apply migration 073 before using shop/variant tax rules.");
        if(t.TaxConfigId!=0) return await c.QuerySingleAsync<long>("UPDATE gst_tax_configurations SET effective_to=@EffectiveTo WHERE tax_config_id=@TaxConfigId RETURNING tax_config_id",t);
        return await InsertOrReuse(c,"gst_tax_configurations","tax_config_id",t,"""
        INSERT INTO gst_tax_configurations(business_profile_code,product_id,classification,hsn_sac,uqc,treatment,tax_rate,cess_rate,reverse_charge,prices_include_tax,effective_from,effective_to,review_reference)
        VALUES(public.sf_visible_business_profile(),@ProductId,@Classification,@HsnSac,@Uqc,@Treatment,@TaxRate,@CessRate,@ReverseCharge,@PricesIncludeTax,@EffectiveFrom,@EffectiveTo,@ReviewReference) RETURNING tax_config_id
        """);
    }
}
