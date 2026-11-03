using System.Text.Json;
namespace StockFlow.Application.Interfaces;
public interface IShopMasterRepository
{
    Task<JsonElement> ReviewAsync();
    Task ChangeAsync(string kind,long id,long store,bool enabled,bool resolve,string reason);
}
