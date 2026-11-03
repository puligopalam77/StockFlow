using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;
[ApiController,Authorize,Route("api/purchases/{id:long}/changes")]
public sealed class PurchaseChangesController(IPurchaseChangeRepository repository,PermissionContext access):ControllerBase
{
 [HttpGet] public async Task<IActionResult> History(long id) => Ok(await repository.HistoryAsync(id));
 [HttpPost] public async Task<IActionResult> Change(long id,PurchaseChangeRequest request)
 {
  if(!access.CanExplicit("PURCHASES","EDIT") || request.Kind=="FINANCIAL"&&!access.Can("PURCHASES","APPROVE"))return Forbid();
  if(!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var userId)||userId<=0)return Unauthorized();
  return Ok(new{changeId=await repository.ChangeAsync(id,request,userId)});
 }
}
