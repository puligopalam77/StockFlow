using Microsoft.Extensions.Configuration;
using Npgsql;
using Dapper;
using StockFlow.Application.Common;
using System.ComponentModel.DataAnnotations;

namespace StockFlow.Infrastructure.Data;

public class DbConnectionFactory
{
    private readonly string _connectionString;
    public bool RequiresExplicitRetailVariant => Scope is not null && Scope.BusinessProfileCode != BusinessProfiles.Electrical;
    public BusinessScope? Scope
    {
        get;
    }

    public DbConnectionFactory(IConfiguration configuration, BusinessScope? scope = null)
    {
        Scope = scope;
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection string is missing.");
    }

    public NpgsqlConnection CreateConnection()
    {
        if (Scope is null)
        {
            return new NpgsqlConnection(_connectionString); // Explicit standalone migration/test clients only.
        }

        var cs = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Options = $"-c role=stockflow_runtime -c app.tenant_id={Scope.TenantId} -c app.user_id={Scope.UserId} -c app.store_id={Scope.StoreId} -c app.location_id={Scope.LocationId} -c app.business_profile_code={ValidatedProfile()}",
            MaxPoolSize = 10
        };
        return new NpgsqlConnection(cs.ConnectionString);
    }

    public void RequireLocation(long? submitted = null)
    {
        if (Scope is null)
        {
            return;
        }

        if (Scope.TenantId <= 0 || Scope.UserId <= 0 || Scope.LocationId <= 0 || Scope.Consolidated)
        {
            throw new ValidationException("Choose an authorized stock location before posting a transaction.");
        }

        if (submitted.HasValue && submitted != Scope.LocationId)
        {
            throw new ValidationException("The active location changed. Return to the original location before retrying this transaction.");
        }
    }

    // Take the setup lock before product/document row locks in posting transactions.
    // The migration uses the same lock when changing the tenant's selection.
    public async Task LockBusinessProfileAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        if (Scope?.ProfileDataVisibilityAvailable != true) return;
        await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('catalog-registration:'||@TenantId::text,0))", Scope, transaction);
        var selected = await connection.ExecuteScalarAsync<string?>("SELECT public.sf_visible_business_profile()", transaction: transaction);
        if (selected != Scope.BusinessProfileCode)
            throw new ValidationException("Shop setup changed. Reload before posting this transaction.");
    }

    private string ValidatedProfile() => BusinessProfiles.IsSupported(Scope!.BusinessProfileCode)
        ? Scope.BusinessProfileCode : throw new ValidationException("Choose a supported business profile.");

    // Cookie validation runs before business middleware. Pass only verified cookie/login claims.
    public NpgsqlConnection CreateSecurityConnection(long tenantId, int userId)
    {
        if (tenantId < 0 || userId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        }

        var cs = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            Options = $"-c role=stockflow_runtime -c app.tenant_id={tenantId} -c app.user_id={userId}",
            MaxPoolSize = 10
        };
        return new NpgsqlConnection(cs.ConnectionString);
    }
}
