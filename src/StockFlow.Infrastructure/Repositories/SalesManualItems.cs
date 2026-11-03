using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.DTOs.Sales;

namespace StockFlow.Infrastructure.Repositories;

public partial class SalesRepository
{
    private sealed record ManualResolution(VariantResponse Variant, string TaxJson);

    private static async Task<ManualResolution> ResolveManualAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CreateSaleItemRequest item, bool inclusive)
    {
        if (!await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_pos_manual_tax(bigint)') IS NOT NULL",transaction:transaction))
            throw new ValidationException("Quick Add Item requires database migration 074.");
        string? json;
        if(item.ManualItem!.TaxRuleId==0)
        {
            if(!await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_pos_manual_no_gst()') IS NOT NULL",transaction:transaction))
                throw new ValidationException("Optional GST for Quick Add Item requires database migration 075.");
            try{json=await connection.ExecuteScalarAsync<string>("SELECT public.sf_pos_manual_no_gst()::text",transaction:transaction);}
            catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.CheckViolation){throw new ValidationException(ex.MessageText);}
        }
        else json=await connection.QuerySingleOrDefaultAsync<string>("SELECT value::text FROM public.sf_pos_manual_tax(@Id) value",new{Id=item.ManualItem.TaxRuleId},transaction);
        if(json is null) throw new ValidationException("Choose a currently reviewed goods tax classification for this shop. Services, cess, reverse charge and zero-rated issuance need a separately supported workflow.");
        using var document=JsonDocument.Parse(json);
        var tax=document.RootElement;
        if(tax.TryGetProperty("pricesIncludeTax",out var pricing) && pricing.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && pricing.GetBoolean()!=inclusive)
            throw new ValidationException("The selected classification uses a different tax-inclusive pricing mode. Choose a rule matching this bill.");
        return new(new VariantResponse{SellingPrice=item.SellingPrice,Mrp=item.ManualItem.Mrp ?? 0,TaxPercentage=tax.GetProperty("taxRate").GetDecimal()},json);
    }

    private static void CheckMrp(SaleLineQuote line, CreateSaleItemRequest item)
    {
        if(line.Mrp is {} mrp && line.LineTotal>item.Quantity*mrp)
            throw new ValidationException("The customer-payable line amount including tax exceeds MRP. Check the selling price and tax mode.");
    }
}
