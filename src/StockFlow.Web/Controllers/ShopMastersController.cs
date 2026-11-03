using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;
namespace StockFlow.Web.Controllers;
[ApiController,Authorize,Route("api/shopmasters"),TypeFilter(typeof(Filters.ExpenseExceptionFilter))]
public sealed class ShopMastersController(IShopMasterRepository repository):ControllerBase
{
    public record Change(string Kind,long Id,long StoreId,bool Enabled,bool Resolve,string Reason);
    [HttpGet]public async Task<IActionResult> Review()=>Ok(await repository.ReviewAsync());
    [HttpPost]public async Task<IActionResult> Write(Change r){await repository.ChangeAsync(r.Kind,r.Id,r.StoreId,r.Enabled,r.Resolve,r.Reason);return Ok(new{saved=true});}
}
