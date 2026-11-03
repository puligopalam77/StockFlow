using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class BusinessProfileRepository(DbConnectionFactory factory) : IBusinessProfileRepository
{
    public async Task<BusinessProfileImpact> ImpactAsync()
    {
        await using var connection = factory.CreateConnection();
        if (factory.Scope?.BusinessProfileConfigurationAvailable == true)
            return await connection.QuerySingleAsync<BusinessProfileImpact>("SELECT * FROM public.sf_business_profile_impact()");
        return await connection.QuerySingleAsync<BusinessProfileImpact>("""
            SELECT (SELECT count(*)::int FROM products) AS ProductCount,
                (SELECT count(*)::int FROM categories) AS CategoryCount,
                ((SELECT count(*) FROM purchases)+(SELECT count(*) FROM sales)
                 +(SELECT count(*) FROM inventory_transactions)+(SELECT count(*) FROM service_requests))::int AS TransactionCount
            """);
    }

    public async Task<long> ChangeAsync(BusinessProfileChange request)
    {
        await using var connection = factory.CreateConnection();
        // Narrow SECURITY DEFINER function validates the actor, tenant, rights,
        // version and existing catalog before touching tenant metadata.
        return await connection.ExecuteScalarAsync<long>("SELECT public.sf_change_business_profile(@Code,@ExpectedVersion,@Reason,@CreateStarterCategories)", request);
    }
}
