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

    public async Task SaveAsync(CompanySettings settings, int userId)
    {
        settings.CompanyName = settings.CompanyName?.Trim() ?? "";
        Validator.ValidateObject(settings, new ValidationContext(settings), true);
        CompanyProfileRules.Validate(settings);
        if (userId <= 0)
        {
            throw new ValidationException("A valid user is required.");
        }

        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        if (!await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='company_settings' AND column_name='profile')"))
            throw new ValidationException("Install database migration 038 before saving the new shop settings.");
        await using var transaction = await connection.BeginTransactionAsync();
        if (settings.DefaultSalespersonId is {} salesperson && !await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM users WHERE user_id=@Id AND is_active)", new {Id=salesperson}, transaction))
            throw new ValidationException("Choose an active salesperson from this business.");
        var previous = await connection.QuerySingleAsync<CompanySettings>(ReadSql + " FOR UPDATE;", transaction: transaction);
        await connection.ExecuteAsync("""
            UPDATE company_settings SET company_name=@CompanyName,address=@Address,mobile_number=@MobileNumber,
                email=@Email,gst_number=@GstNumber,invoice_footer=@InvoiceFooter,profile=CAST(@Profile AS jsonb),updated_at=CURRENT_TIMESTAMP WHERE company_settings_id=1;
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
