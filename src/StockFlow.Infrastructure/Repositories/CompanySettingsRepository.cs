using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class CompanySettingsRepository : ICompanySettingsRepository
{
    public async Task<CompanyGstIdentity?> GetGstIdentityAsync()
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.gst_shop_mappings') IS NOT NULL"))return null;
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('VIEW')"))return null;
        var row=await c.QuerySingleOrDefaultAsync<CompanyGstIdentity>("""
        SELECT r.registration_id RegistrationId,b.legal_name LegalName,r.gstin,b.pan,r.registration_type RegistrationType,r.state_code StateCode,s.store_name StoreName,m.business_profile_code Profile
        FROM gst_shop_mappings m JOIN gst_registrations r USING(tenant_id,registration_id)
        JOIN gst_legal_businesses b USING(tenant_id,business_id) JOIN stores s ON s.tenant_id=m.tenant_id AND s.store_id=m.store_id
        WHERE m.tenant_id=sf_tenant() AND m.store_id=sf_store() AND m.business_profile_code=sf_visible_business_profile()
        AND m.status='ACTIVE' AND r.status='ACTIVE'
        AND CURRENT_DATE BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity')
        AND CURRENT_DATE BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity')
        """);
        if(row is not null&&!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('EDIT')"))
        {
            static string? Mask(string? value)=>value is null?null:value.Length<6?"****":value[..2]+new string('*',value.Length-5)+value[^3..];
            row=row with{Gstin=Mask(row.Gstin),Pan=Mask(row.Pan)};
        }
        return row;
    }
    public async Task SaveTaxModeAsync(string mode,int userId)
    {
        if(mode is not ("INCLUSIVE" or "EXCLUSIVE")||userId<=0)throw new ValidationException("Choose a valid billing price behavior.");
        factory.RequireLocation();await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,t);
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_gst_allowed('EDIT','COMPANY_SETTINGS') AND sf_can_location(sf_location())",transaction:t))throw new UnauthorizedAccessException("Business & Billing settings permission required.");
        var previous=await c.ExecuteScalarAsync<string>("SELECT profile::text FROM company_settings WHERE company_settings_id=1 FOR UPDATE",transaction:t);
        await c.ExecuteAsync("UPDATE company_settings SET profile=jsonb_set(coalesce(profile,'{}'::jsonb),'{DefaultTaxMode}',to_jsonb(CAST(@mode AS text))),updated_at=now() WHERE company_settings_id=1",new{mode},t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,old_values,new_values) VALUES(@userId,'Company settings','UPDATE','company_settings',1,CAST(@previous AS jsonb),jsonb_build_object('DefaultTaxMode',CAST(@mode AS text)))",new{userId,previous,mode},t);await t.CommitAsync();
    }
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public CompanySettingsRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    internal const string ReadSql = """
        SELECT company_name AS CompanyName, address AS Address, mobile_number AS MobileNumber,
            email AS Email, gst_number AS GstNumber, invoice_footer AS InvoiceFooter,
            (to_jsonb(company_settings)->'profile'->>'LegalBusinessName') AS LegalBusinessName,
            (to_jsonb(company_settings)->'profile'->>'AddressLine1') AS AddressLine1,
            (to_jsonb(company_settings)->'profile'->>'AddressLine2') AS AddressLine2,
            (to_jsonb(company_settings)->'profile'->>'District') AS District,
            (to_jsonb(company_settings)->'profile'->>'City') AS City,
            (to_jsonb(company_settings)->'profile'->>'State') AS State,
            (to_jsonb(company_settings)->'profile'->>'PinCode') AS PinCode,
            (to_jsonb(company_settings)->'profile'->>'AlternatePhone') AS AlternatePhone,
            (to_jsonb(company_settings)->'profile'->>'Website') AS Website,
            (to_jsonb(company_settings)->'profile'->>'PanNumber') AS PanNumber,
            (to_jsonb(company_settings)->'profile'->>'TermsAndConditions') AS TermsAndConditions,
            (to_jsonb(company_settings)->'profile'->>'LogoDataUrl') AS LogoDataUrl,
            COALESCE(to_jsonb(company_settings)->'profile'->>'InvoicePrefix','INV-{YYYY}-') AS InvoicePrefix,
            COALESCE(to_jsonb(company_settings)->'profile'->>'ReceiptPrefix','GRN-{YYYY}-') AS ReceiptPrefix,
            COALESCE(to_jsonb(company_settings)->'profile'->>'Currency','INR') AS Currency,
            COALESCE(to_jsonb(company_settings)->'profile'->>'DefaultTaxMode','EXCLUSIVE') AS DefaultTaxMode,
            (to_jsonb(company_settings)->'profile'->>'DefaultPaymentTermsDays')::integer AS DefaultPaymentTermsDays,
            (to_jsonb(company_settings)->'profile'->>'DefaultSalespersonId')::integer AS DefaultSalespersonId
        FROM company_settings WHERE company_settings_id = 1
        """;
    public async Task<CompanySettings> GetAsync()
    {
        await using var connection = factory.CreateConnection();
        return await connection.QuerySingleAsync<CompanySettings>(ReadSql);
    }
    public async Task<IReadOnlyList<CompanySalesperson>> GetSalespeopleAsync()
    {
        await using var connection = factory.CreateConnection();
        return (await connection.QueryAsync<CompanySalesperson>("SELECT user_id AS UserId,full_name AS FullName FROM users WHERE is_active ORDER BY full_name")).ToList();
    }

    public Task SaveAsync(CompanySettings settings,int userId)=>SaveCoreAsync(settings,userId);
    public Task SaveSectionAsync(CompanySettings settings,string section,string expectedVersion,int userId)
    {
        if(!CompanySettingsViews.Valid(section)||string.IsNullOrEmpty(expectedVersion))throw new ValidationException("Reload the settings view before saving.");
        return SaveCoreAsync(settings,userId,section,expectedVersion);
    }
    private async Task SaveCoreAsync(CompanySettings settings, int userId,string? section=null,string? expectedVersion=null)
    {
        if (userId <= 0)
        {
            throw new ValidationException("A valid user is required.");
        }

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        if(factory.Scope is not null && !await connection.ExecuteScalarAsync<bool>("SELECT sf_transfer_permission('COMPANY_SETTINGS','EDIT') AND sf_actor()=@userId",new{userId}))
            throw new UnauthorizedAccessException("Company settings edit permission and authenticated actor required.");
        if (!await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='company_settings' AND column_name='profile')"))
            throw new ValidationException("Install database migration 038 before saving the new shop settings.");
        await using var transaction = await connection.BeginTransactionAsync();
        var previous = await connection.QuerySingleAsync<CompanySettings>(ReadSql + " FOR UPDATE;", transaction: transaction);
        if(section is not null)
        {
            if(CompanySettingsViews.Version(previous,section)!=expectedVersion)throw new ValidationException("These settings changed since you opened this view. Reload before saving; your entered values have been retained.");
            settings=CompanySettingsViews.Merge(previous,settings,section);
        }
        if(section is null or CompanySettingsViews.Business)
        {
            CompanyProfileRules.NormalizeAddress(settings);
            settings.CompanyName=settings.CompanyName?.Trim()??"";
        }
        Validator.ValidateObject(settings,new ValidationContext(settings),true);
        CompanyProfileRules.Validate(settings);
        if (settings.DefaultSalespersonId is {} salesperson && !await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE user_id=@Id AND is_active)", new {Id=salesperson}, transaction))
            throw new ValidationException("Choose an active salesperson from this business.");
        CompanyProfileRules.ValidateTaxDisplay(settings,previous);
        var lengths=await connection.QueryAsync<(string Table,string Column,int? Length)>("SELECT table_name,column_name,character_maximum_length FROM information_schema.columns WHERE table_schema='public' AND ((table_name='sales' AND column_name='invoice_number') OR (table_name='purchase_receipts' AND column_name='receipt_number'))",transaction:transaction);
        var year=await connection.ExecuteScalarAsync<int>("SELECT extract(year FROM CURRENT_DATE)::integer",transaction:transaction);
        CompanyProfileRules.ValidateNumbering(settings,lengths.FirstOrDefault(l=>l.Table=="sales").Length,lengths.FirstOrDefault(l=>l.Table=="purchase_receipts").Length,year);
        await connection.ExecuteAsync("""
            UPDATE company_settings SET company_name=@CompanyName,address=@Address,mobile_number=@MobileNumber,
                email=@Email,gst_number=@GstNumber,invoice_footer=@InvoiceFooter,profile=coalesce(profile,'{}'::jsonb)||CAST(@Profile AS jsonb),updated_at=CURRENT_TIMESTAMP WHERE company_settings_id=1;
            """,
new {settings.CompanyName, settings.Address, settings.MobileNumber, settings.Email, settings.GstNumber, settings.InvoiceFooter, Profile=JsonSerializer.Serialize(settings)},
transaction);
        await connection.ExecuteAsync("""
            INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,old_values,new_values)
            VALUES (@UserId,'Company settings','UPDATE','company_settings',1,CAST(@Old AS jsonb),CAST(@New AS jsonb));
            """,
new
{
    UserId = userId,
    Old = JsonSerializer.Serialize(previous),
    New = JsonSerializer.Serialize(settings)
},
transaction);
        await transaction.CommitAsync();
    }
}
