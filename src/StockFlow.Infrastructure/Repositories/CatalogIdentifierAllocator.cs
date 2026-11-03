using Dapper;
using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Products;
using StockFlow.Infrastructure.Data;
namespace StockFlow.Infrastructure.Repositories;

// Reservations commit independently of the later product save. Abandoned or failed
// registrations leave gaps, and retries return exactly the same reserved values.
public sealed class CatalogIdentifierAllocator(DbConnectionFactory factory)
{
    public async Task<object> SuggestPrefixAsync(string? name)
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_suggest_sku_prefix(text)') IS NOT NULL")) return new {Available=false,Prefix=(string?)null};
        return new {Available=true,Prefix=await c.ExecuteScalarAsync<string>("SELECT sf_suggest_sku_prefix(@name)",new{name=name??""})};
    }
    public async Task<object> ReserveAsync(IdentifierReservation request)
    {
        if(request.BusinessProfileCode!=factory.Scope?.BusinessProfileCode)
            throw new ValidationException("The active shop changed. Return to the original shop before continuing this product draft.");
        if(request.RequestId==Guid.Empty) throw new ValidationException("Use a valid registration request ID.");
        await using var c=factory.CreateConnection(); await c.OpenAsync();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_reserve_product_code(integer,uuid,text)') IS NOT NULL"))
            throw new ValidationException("Sequential SKU generation requires manual migration 067 and reviewed category prefixes.");
        await using var tx=await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c,tx);
        object result;
        if(request.ParentCode is null || request.ReserveParent)
            result=new {Code=await c.ExecuteScalarAsync<string>("SELECT public.sf_reserve_product_code(@CategoryId,@RequestId,@ParentCode)",request,tx)};
        else
        {
            if(request.OperationId==Guid.Empty) throw new ValidationException("Use a valid variant reservation operation ID.");
            result=new {Codes=await c.ExecuteScalarAsync<string[]>("SELECT public.sf_reserve_variant_codes(@ParentCode,@RequestId,@OperationId,@Count,@ProductId)",request,tx)};
        }
        await tx.CommitAsync();return result;
    }
}
