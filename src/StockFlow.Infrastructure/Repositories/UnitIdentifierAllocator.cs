using Dapper;
using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Products;
using StockFlow.Infrastructure.Data;
namespace StockFlow.Infrastructure.Repositories;

public sealed class UnitIdentifierAllocator(DbConnectionFactory factory)
{
    public async Task<object> ReserveAsync(UnitIdentifierReservation request)
    {
        Validator.ValidateObject(request,new ValidationContext(request),true);
        factory.RequireLocation(request.LocationId);
        if(request.RequestId==Guid.Empty || request.BusinessProfileCode!=factory.Scope?.BusinessProfileCode)
            throw new ValidationException("The receipt shop changed or its identifier request is invalid. Reload the original receipt.");
        await using var c=factory.CreateConnection();await c.OpenAsync();
        if(!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_reserve_unit_identifiers(bigint,uuid,integer)') IS NOT NULL"))
            throw new ValidationException("Internal unit identifier allocation requires manual migration 068. You can still enter actual manufacturer serial numbers.");
        await using var tx=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,tx);
        var codes=await c.ExecuteScalarAsync<string[]>("SELECT public.sf_reserve_unit_identifiers(@VariantId,@RequestId,@Count)",request,tx);
        await tx.CommitAsync();return new {Codes=codes,Source="INTERNAL"};
    }
}
