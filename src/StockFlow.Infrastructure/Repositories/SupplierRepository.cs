using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public SupplierRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private const string SelectSql = "SELECT t.supplier_id AS SupplierId,t.supplier_code AS SupplierCode,t.supplier_type AS SupplierType,t.alternate_mobile AS AlternateMobile,t.country_code AS CountryCode,t.state AS State,t.city AS City,t.postal_code AS PostalCode,t.payment_terms_days AS PaymentTermsDays,t.notes AS Notes, t.supplier_name AS SupplierName, t.contact_person AS ContactPerson, t.mobile_number AS MobileNumber, t.email AS Email, t.address AS Address, t.gst_number AS GstNumber, t.is_active AS IsActive, t.created_at AS CreatedAt, t.updated_at AS UpdatedAt FROM suppliers t";
    public async Task<IEnumerable<Supplier>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        using var connection = factory.CreateConnection();
        return await connection.QueryAsync<Supplier>(SelectSql + " WHERE (NOT @ActiveOnly OR t.is_active) AND (@Term IS NULL OR t.supplier_name ILIKE '%' || @Term || '%' OR t.supplier_code ILIKE '%' || @Term || '%' OR t.city ILIKE '%' || @Term || '%' OR t.alternate_mobile ILIKE '%' || @Term || '%' OR t.contact_person ILIKE '%' || @Term || '%' OR t.mobile_number ILIKE '%' || @Term || '%' OR t.email ILIKE '%' || @Term || '%' OR t.address ILIKE '%' || @Term || '%' OR t.gst_number ILIKE '%' || @Term || '%') ORDER BY t.supplier_name, t.supplier_id;",
new
{
    ActiveOnly = activeOnly,
    Term = string.IsNullOrWhiteSpace(term) ? null : term.Trim()
});
    }

    public async Task<Supplier?> GetByIdAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Supplier>(SelectSql + " WHERE t.supplier_id = @Id;", new
        {
            Id = id
        });
    }

    public async Task<int> CreateAsync(Supplier entity)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>("INSERT INTO suppliers (supplier_name, contact_person, mobile_number, email, address, gst_number,supplier_type,alternate_mobile,country_code,state,city,postal_code,payment_terms_days,notes, is_active) VALUES (@SupplierName, @ContactPerson, @MobileNumber, @Email, @Address, @GstNumber,@SupplierType,@AlternateMobile,@CountryCode,@State,@City,@PostalCode,@PaymentTermsDays,@Notes, TRUE) RETURNING supplier_id;",
entity);
    }

    public async Task<bool> UpdateAsync(Supplier entity)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE suppliers SET supplier_name = @SupplierName, contact_person = @ContactPerson, mobile_number = @MobileNumber, email = @Email, address = @Address, gst_number = @GstNumber,supplier_type=@SupplierType,alternate_mobile=@AlternateMobile,country_code=@CountryCode,state=@State,city=@City,postal_code=@PostalCode,payment_terms_days=@PaymentTermsDays,notes=@Notes, is_active = @IsActive, updated_at = CURRENT_TIMESTAMP WHERE supplier_id = @SupplierId;",
entity) > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE suppliers SET is_active = FALSE, updated_at = CURRENT_TIMESTAMP WHERE supplier_id = @Id;", new
        {
            Id = id
        }) > 0;
    }
}
