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

    private const string SelectSql = "SELECT t.supplier_id AS SupplierId,t.supplier_code AS SupplierCode,t.supplier_type AS SupplierType,t.alternate_mobile AS AlternateMobile,t.country_code AS CountryCode,t.state AS State,t.city AS City,t.postal_code AS PostalCode,t.payment_terms_days AS PaymentTermsDays,t.notes AS Notes, t.supplier_name AS SupplierName, t.contact_person AS ContactPerson, t.mobile_number AS MobileNumber, t.email AS Email, t.address AS Address,to_jsonb(t)->>'district' AS District, to_jsonb(t)->>'address_line1' AS AddressLine1,to_jsonb(t)->>'address_line2' AS AddressLine2,coalesce((to_jsonb(t)->>'use_structured_address')::boolean,false) AS UseStructuredAddress, t.gst_number AS GstNumber, t.is_active AS IsActive, t.created_at AS CreatedAt, t.updated_at AS UpdatedAt FROM suppliers t";
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

    // Both reads share one snapshot and the same connection's authoritative RLS scope.
    public async Task<StockFlow.Application.DTOs.Suppliers.SupplierPage<Supplier>> GetPageAsync(string? term, bool? active, int offset, int limit)
    {
        if (offset < 0 || !new[] { 10, 25, 50, 100 }.Contains(limit))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Choose a valid supplier page.");
        using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        const string filter = """
             WHERE (@Active IS NULL OR t.is_active=@Active)
             AND (@Term IS NULL OR t.supplier_name ILIKE @Term ESCAPE '\'
              OR t.supplier_code ILIKE @Term ESCAPE '\'
              OR t.mobile_number ILIKE @Term ESCAPE '\'
              OR t.gst_number ILIKE @Term ESCAPE '\')
            """;
        var normalized = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        var parameters = new { Active = active, Term = normalized, Offset = offset, Limit = limit };
        var total = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM suppliers t" + filter, parameters, transaction);
        var items = (await connection.QueryAsync<Supplier>(SelectSql + filter + " ORDER BY lower(t.supplier_name),t.supplier_id OFFSET @Offset LIMIT @Limit", parameters, transaction)).ToList();
        await transaction.CommitAsync();
        return new(items, total);
    }

    public async Task<bool> SetActiveAsync(int id, bool active)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE suppliers SET is_active=@Active,updated_at=CURRENT_TIMESTAMP WHERE supplier_id=@Id", new { Id = id, Active = active }) > 0;
    }

    public async Task<int> CreateAsync(Supplier entity)
    {
        using var connection = factory.CreateConnection();
        var structured = await StructuredColumns(connection, entity);
        var sql = "INSERT INTO suppliers (supplier_name, contact_person, mobile_number, email, address, gst_number,supplier_type,alternate_mobile,country_code,state,city,postal_code,payment_terms_days,notes, is_active) VALUES (@SupplierName, @ContactPerson, @MobileNumber, @Email, @Address, @GstNumber,@SupplierType,@AlternateMobile,@CountryCode,@State,@City,@PostalCode,@PaymentTermsDays,@Notes, TRUE) RETURNING supplier_id;";
        if (structured) sql = sql.Replace("notes, is_active)", "notes,address_line1,address_line2,use_structured_address, is_active)").Replace("@Notes, TRUE)", "@Notes,@AddressLine1,@AddressLine2,@UseStructuredAddress, TRUE)");
        if (await DistrictColumn(connection, entity)) sql = sql.Replace("notes,", "notes,district,").Replace("@Notes,", "@Notes,@District,");
        return await connection.ExecuteScalarAsync<int>(sql, entity);
    }

    public async Task<bool> UpdateAsync(Supplier entity)
    {
        using var connection = factory.CreateConnection();
        var structured = await StructuredColumns(connection, entity);
        var sql = "UPDATE suppliers SET supplier_name = @SupplierName, contact_person = @ContactPerson, mobile_number = @MobileNumber, email = @Email, address = @Address, gst_number = @GstNumber,supplier_type=@SupplierType,alternate_mobile=@AlternateMobile,country_code=@CountryCode,state=@State,city=@City,postal_code=@PostalCode,payment_terms_days=@PaymentTermsDays,notes=@Notes, is_active = @IsActive, updated_at = CURRENT_TIMESTAMP WHERE supplier_id = @SupplierId;";
        if (structured) sql = sql.Replace("notes=@Notes,", "notes=@Notes,address_line1=@AddressLine1,address_line2=@AddressLine2,use_structured_address=@UseStructuredAddress,");
        if (await DistrictColumn(connection, entity)) sql = sql.Replace("notes=@Notes,", "notes=@Notes,district=@District,");
        return await connection.ExecuteAsync(sql, entity) > 0;
    }

    private static async Task<bool> DistrictColumn(System.Data.IDbConnection connection, Supplier entity)
    {
        var ready=await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='suppliers' AND column_name='district')");
        if(!ready && entity.District is not null) throw new System.ComponentModel.DataAnnotations.ValidationException("Supplier district setup is pending. Ask your administrator to complete address setup, then retry.");
        return ready;
    }
    private static async Task<bool> StructuredColumns(System.Data.IDbConnection connection, Supplier entity)
    {
        // Reads and ordinary registration remain compatible while the additive migration is pending.
        var ready = await connection.ExecuteScalarAsync<bool>("SELECT count(*)=3 FROM information_schema.columns WHERE table_schema='public' AND table_name='suppliers' AND column_name IN ('address_line1','address_line2','use_structured_address');");
        if (!ready && entity.UseStructuredAddress && entity.Address is not null)
            throw new System.ComponentModel.DataAnnotations.ValidationException("Supplier address setup is pending. Ask your administrator to complete setup, then retry; your entered details are retained.");
        return ready;
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
