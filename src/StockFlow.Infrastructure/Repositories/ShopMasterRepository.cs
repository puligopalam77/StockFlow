using System.Text.Json;
using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
namespace StockFlow.Infrastructure.Repositories;
public sealed class ShopMasterRepository(DbConnectionFactory factory):IShopMasterRepository
{
    private async Task Require(Npgsql.NpgsqlConnection c)
    {
        factory.RequireLocation();
        if(!await c.ExecuteScalarAsync<bool>("SELECT sf_owner() AND sf_shop_member(sf_store()) AND sf_transfer_permission('COMPANY_SETTINGS','EDIT')"))throw new UnauthorizedAccessException("Only the tenant owner may review or change shop master sharing.");
    }
    public async Task<JsonElement> ReviewAsync()
    {
        await using var c=factory.CreateConnection();await Require(c);
        return JsonSerializer.Deserialize<JsonElement>((await c.ExecuteScalarAsync<string>("SELECT sf_shop_master_review()::text"))!);
    }
    public async Task ChangeAsync(string kind,long id,long store,bool enabled,bool resolve,string reason)
    {
        await using var c=factory.CreateConnection();await Require(c);
        await c.ExecuteAsync(resolve?"SELECT sf_resolve_master(@kind,@id,@store,@reason)":"SELECT sf_share_master(@kind,@id,@store,@enabled,@reason)",new{kind,id,store,enabled,reason});
    }
}
