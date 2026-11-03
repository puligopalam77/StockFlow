using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.Infrastructure.Repositories;

public partial class SecurityRepository
{
    private static async Task<StoreAccessRequest> ReadStoreAccess(NpgsqlConnection c, int id, NpgsqlTransaction? t = null)
    {
        return new()
        {
            StoreIds = (await c.QueryAsync<long>("SELECT store_id FROM user_store_access WHERE user_id=@id AND is_active", new
            {
                id
            }, t)).ToList(),
            DefaultStoreId = await c.QuerySingleOrDefaultAsync<long?>("SELECT store_id FROM user_store_access WHERE user_id=@id AND is_active AND is_default", new
            {
                id
            }, t),
            WarehouseLocationIds = (await c.QueryAsync<long>("SELECT location_id FROM user_location_access WHERE user_id=@id", new
            {
                id
            }, t)).ToList()
        };
    }

    private static async Task SaveStoreAccess(NpgsqlConnection c, NpgsqlTransaction t, int id, StoreAccessRequest r, int actor)
    {
        if (r is null || r.StoreIds is null || r.WarehouseLocationIds is null || r.StoreIds.Count > 500 || r.WarehouseLocationIds.Count > 500 || r.StoreIds.Distinct().Count() != r.StoreIds.Count || r.WarehouseLocationIds.Distinct().Count() != r.WarehouseLocationIds.Count || r.DefaultStoreId.HasValue && !r.StoreIds.Contains(r.DefaultStoreId.Value) || r.StoreIds.Count > 0 && !r.DefaultStoreId.HasValue)
        {
            throw new ValidationException("Choose distinct stores and a default from the assigned stores.");
        }

        var old = await ReadStoreAccess(c, id, t);
        bool changed = !old.StoreIds.Order().SequenceEqual(r.StoreIds.Order()) || !old.WarehouseLocationIds.Order().SequenceEqual(r.WarehouseLocationIds.Order()) || old.DefaultStoreId != r.DefaultStoreId;
        var owner = await c.ExecuteScalarAsync<bool>("SELECT is_tenant_owner FROM users WHERE user_id=@id", new
        {
            id
        }, t);
        if (!owner && r.StoreIds.Count + r.WarehouseLocationIds.Count == 0)
        {
            throw new ValidationException("Assign at least one store or central warehouse.");
        }

        if (!changed)
        {
            return;
        }

        await Require(c, t, actor, "STORES", "MANAGE_ACCESS");
        if (await c.ExecuteScalarAsync<int>("SELECT count(*)::int FROM stores WHERE store_id=ANY(@ids) AND is_active", new
        {
            ids = r.StoreIds.ToArray()
        }, t) != r.StoreIds.Count)
        {
            throw new ValidationException("Choose active stores within your authorized business access.");
        }

        if (await c.ExecuteScalarAsync<int>("SELECT count(*)::int FROM store_locations WHERE location_id=ANY(@ids) AND store_id IS NULL AND location_type='WAREHOUSE' AND is_active",
new
{
    ids = r.WarehouseLocationIds.ToArray()
},
t) != r.WarehouseLocationIds.Count)
        {
            throw new ValidationException("Choose active central warehouses within your business access.");
        }

        await c.ExecuteAsync("DELETE FROM user_store_access WHERE user_id=@id; DELETE FROM user_location_access WHERE user_id=@id", new
        {
            id
        }, t);
        foreach (var store in r.StoreIds)
        {
            await c.ExecuteAsync("INSERT INTO user_store_access(user_id,store_id,is_default) VALUES(@id,@store,@isDefault)",
new
{
    id,
    store,
    isDefault = store == r.DefaultStoreId
},
t);
        }

        foreach (var location in r.WarehouseLocationIds)
        {
            await c.ExecuteAsync("INSERT INTO user_location_access(user_id,location_id) VALUES(@id,@location)", new
            {
                id,
                location
            }, t);
        }

        await Audit(c, t, actor, "STORE_ACCESS_CHANGED", id, old, r);
    }
}
