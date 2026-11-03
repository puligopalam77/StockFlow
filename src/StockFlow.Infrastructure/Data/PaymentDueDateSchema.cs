using Dapper;
using Npgsql;

namespace StockFlow.Infrastructure.Data;

internal static class PaymentDueDateSchema
{
    public static async Task<string> ResolveReadSqlAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        var installed = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM information_schema.columns
                WHERE table_schema='public' AND table_name='sales' AND column_name='payment_due_date')
            """, transaction: transaction);

        // Before migration 037, due dates are unknown. Never infer overdue dates.
        return installed ? sql : sql.Replace("s.payment_due_date", "NULL::date", StringComparison.Ordinal);
    }
}
