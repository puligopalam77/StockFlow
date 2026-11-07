using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public CustomerRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private const string SelectSql = "SELECT t.customer_id AS CustomerId, t.customer_name AS CustomerName, t.mobile_number AS MobileNumber, t.email AS Email, t.address AS Address,to_jsonb(t)->>'address_details' AS AddressDetailsJson, t.gst_number AS GstNumber, t.is_active AS IsActive, t.created_at AS CreatedAt, t.updated_at AS UpdatedAt FROM customers t";
    private const string InsertSql = "INSERT INTO customers (customer_name, mobile_number, email, address, gst_number, is_active) VALUES (@CustomerName, @MobileNumber, @Email, @Address, @GstNumber, TRUE) RETURNING customer_id;";
    public async Task<IEnumerable<Customer>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        using var connection = factory.CreateConnection();
        return await connection.QueryAsync<Customer>(SelectSql + " WHERE (NOT @ActiveOnly OR t.is_active) AND (@Term IS NULL OR t.customer_name ILIKE '%' || @Term || '%' OR t.mobile_number ILIKE '%' || @Term || '%' OR t.email ILIKE '%' || @Term || '%' OR t.address ILIKE '%' || @Term || '%' OR t.gst_number ILIKE '%' || @Term || '%') ORDER BY t.customer_name, t.customer_id;",
new
{
    ActiveOnly = activeOnly,
    Term = string.IsNullOrWhiteSpace(term) ? null : term.Trim()
});
    }

    public async Task<Customer?> GetByIdAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Customer>(SelectSql + " WHERE t.customer_id = @Id;", new
        {
            Id = id
        });
    }

    public async Task<int> CreateAsync(Customer entity, bool preventIdentifierDuplicates = false)
    {
        await using var connection = factory.CreateConnection();
        var sql=await AddressColumns(connection,entity)?InsertSql.Replace("address,", "address,address_details,").Replace("@Address,", "@Address,CAST(@AddressDetailsJson AS jsonb),"):InsertSql;
        if (!preventIdentifierDuplicates) return await connection.ExecuteScalarAsync<int>(sql, entity);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        if (preventIdentifierDuplicates)
        {
            var identifiers = new List<string>();
            if (!string.IsNullOrWhiteSpace(entity.MobileNumber)) identifiers.Add("phone:" + entity.MobileNumber.Trim());
            if (!string.IsNullOrWhiteSpace(entity.GstNumber)) identifiers.Add("gst:" + entity.GstNumber.Trim().ToUpperInvariant());
            foreach (var identifier in identifiers.Order(StringComparer.Ordinal))
                await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('customer-quick-add:' || COALESCE(current_setting('app.tenant_id',true),'') || ':' || COALESCE(current_setting('app.business_profile_code',true),'') || ':' || @identifier,0))", new { identifier }, transaction);
            if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM customers WHERE (@MobileNumber IS NOT NULL AND btrim(mobile_number)=@MobileNumber) OR (@GstNumber IS NOT NULL AND upper(btrim(gst_number))=upper(@GstNumber)))", entity, transaction))
                throw new System.ComponentModel.DataAnnotations.ValidationException("A customer with this mobile number or GSTIN already exists. Select the existing customer instead.");
        }
        var id = await connection.ExecuteScalarAsync<int>(sql, entity, transaction);
        await transaction.CommitAsync();
        return id;
    }

    public async Task<bool> UpdateAsync(Customer entity)
    {
        using var connection = factory.CreateConnection();
        var sql="UPDATE customers SET customer_name = @CustomerName, mobile_number = @MobileNumber, email = @Email, address = @Address, gst_number = @GstNumber, is_active = @IsActive, updated_at = CURRENT_TIMESTAMP WHERE customer_id = @CustomerId;";
        if(await AddressColumns(connection,entity))sql=sql.Replace("address = @Address,", "address = @Address,address_details=CAST(@AddressDetailsJson AS jsonb),");
        return await connection.ExecuteAsync(sql,entity)>0;
    }

    private static async Task<bool> AddressColumns(System.Data.IDbConnection connection,Customer entity)
    {
        var ready=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='customers' AND column_name='address_details')");
        if(!ready && entity.AddressDetailsJson is not null)throw new System.ComponentModel.DataAnnotations.ValidationException("Customer address setup is pending. Ask your administrator to complete setup, then retry; entered details are retained.");
        return ready;
    }
    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE customers SET is_active = FALSE, updated_at = CURRENT_TIMESTAMP WHERE customer_id = @Id;", new
        {
            Id = id
        }) > 0;
    }
}
