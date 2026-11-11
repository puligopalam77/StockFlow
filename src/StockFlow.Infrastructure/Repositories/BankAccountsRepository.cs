using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
namespace StockFlow.Infrastructure.Repositories;

public sealed class BankAccountsRepository(IOptions<BankAccountsOptions> options,BusinessScope scope):IBankAccountsRepository
{
    private async Task<NpgsqlConnection> Open()
    {
        var settings=options.Value;
        if(!settings.Enabled)throw new ValidationException("Bank Accounts is not enabled.");
        if(scope.TenantId<=0||scope.UserId<=0)throw new UnauthorizedAccessException();
        // This development build cannot use StockFlow Remote, even if a feature flag
        // is accidentally enabled. Production connection support needs release review.
        var cs=new NpgsqlConnectionStringBuilder(settings.DevelopmentConnection);
        if(cs.Host!="127.0.0.1"||cs.Port!=55447||cs.Database?.StartsWith("sf_bank_dev_",StringComparison.Ordinal)!=true)
            throw new ValidationException("Bank development requires the dedicated local database.");
        cs.Options=$"-c role=stockflow_runtime -c app.tenant_id={scope.TenantId} -c app.user_id={scope.UserId} -c app.store_id={scope.StoreId} -c app.location_id={scope.LocationId} -c app.business_profile_code={Profile()}";
        var c=new NpgsqlConnection(cs.ConnectionString);
        try{
            await c.OpenAsync();
            if(!await c.ExecuteScalarAsync<bool>("SELECT inet_server_addr()='127.0.0.1'::inet AND inet_server_port()=55447 AND current_database() LIKE 'sf_bank_dev_%' AND current_setting('cluster_name') LIKE 'stockflow_bank_review_%'"))
                throw new ValidationException("Bank development database identity did not match.");
            return c;
        }catch{await c.DisposeAsync();throw;}
    }
    private string Profile()=>BusinessProfiles.IsSupported(scope.BusinessProfileCode)?scope.BusinessProfileCode:throw new UnauthorizedAccessException();
    public async Task<JsonElement> ReadAsync(Guid? accountId,DateTime date,int page,string? search,long? shopId=null)
    {
        if(page<1||page>100000||date.Year<2000||(search?.Length??0)>150||shopId is <=0)throw new ValidationException("Choose a valid date, search, shop and page.");
        await using var c=await Open();await using var t=await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await c.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:t);
        var json=await c.ExecuteScalarAsync<string>("SELECT sf_bank_read(@accountId,@date::date,@page,@search,@shopId)::text",new{accountId,date=date.Date,page,search=search?.Trim()??"",shopId},t);
        await t.CommitAsync();return JsonSerializer.Deserialize<JsonElement>(json!);
    }
    public async Task<JsonElement> WriteAsync(string action,Guid accountId,Guid requestId,JsonElement body)
    {
        if(requestId==Guid.Empty||accountId==Guid.Empty||body.ValueKind!=JsonValueKind.Object)throw new ValidationException("A valid request and account are required.");
        if(action is ("open" or "verify" or "reverse") && !options.Value.PostingEnabled)throw new ValidationException("Bank verification posting is disabled.");
        await using var c=await Open();
        try{
            var json=await c.ExecuteScalarAsync<string>("SELECT sf_bank_write(@action,@accountId,@requestId,@body::jsonb)::text",new{action,accountId,requestId,body=body.GetRawText()});
            return JsonSerializer.Deserialize<JsonElement>(json!);
        }catch(PostgresException ex)when(ex.SqlState is "23514" or "23505" or "23503"){
            throw new ValidationException(ex.SqlState=="23505"?"That bank event, request or default already exists. Refresh before retrying.":ex.SqlState=="23503"?"The selected account or source is unavailable in this shop.":ex.MessageText);
        }
    }
}
