using System.Text.Json;
namespace StockFlow.Application.Interfaces;
public interface IBankAccountsRepository
{
    Task<JsonElement> ReadAsync(Guid? accountId,DateTime date,int page,string? search);
    Task<JsonElement> WriteAsync(string action,Guid accountId,Guid requestId,JsonElement body);
}
