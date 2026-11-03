using Dapper;
using Npgsql;
using StockFlow.Application.Common;

namespace StockFlow.Infrastructure.Repositories;

public static class BillingPolicyStore
{
    public static async Task<bool> AvailableAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
        => await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_billing_policy()') IS NOT NULL", transaction: transaction);

    public static async Task<BillingDiscountPolicy> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        if (!await AvailableAsync(connection, transaction))
            return new() { DiscountLimitPercent = await connection.ExecuteScalarAsync<decimal>("SELECT coalesce((SELECT discount_limit_percent FROM billing_policy),10)", transaction: transaction) };
        return (await connection.QuerySingleAsync<BillingDiscountPolicy>("SELECT * FROM public.sf_billing_policy()", transaction: transaction));
    }
}
