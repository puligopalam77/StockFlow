using Dapper;
using Npgsql;

namespace StockFlow.Web.Notifications;

// Test-message quotas remain independent of the retired email login flow.
public sealed class NotificationTestService(IConfiguration configuration)
{
    public async Task<bool> ReserveAsync(long tenant)
    {
        await using var connection=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(
            configuration.GetConnectionString("NotificationWorker") ?? configuration.GetConnectionString("DefaultConnection"))
            {Options="-c role=stockflow_notification_worker -c statement_timeout=10000",MaxPoolSize=2}.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>("SELECT sf_notification_test_reserve(@tenant)",new{tenant});
    }
}
