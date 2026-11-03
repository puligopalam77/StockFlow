using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/labels/lots")]
public sealed class BarcodeLotsController(IBarcodeLotRepository lots,BusinessScope scope,PermissionContext access) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        if(!access.Can("BARCODES","VIEW"))return Forbid();
        if(scope.Consolidated||scope.LocationId<=0)return BadRequest(new{message="Choose one stock location to view barcode lots."});
        return Ok(await lots.ListAsync());
    }
    [HttpGet("receipt/{id:long}")]
    [HttpPost("receipt/{id:long}/{actionName}")]
    public async Task<IActionResult> Receipt(long id,string? actionName=null)
    {
        var action=actionName?.ToUpperInvariant();
        if(action is not null and not "PRINT" and not "REPRINT" and not "GENERATE")return BadRequest();
        if(!access.Can("BARCODES",action??"VIEW"))return Forbid();
        if(scope.Consolidated||scope.LocationId<=0)return BadRequest(new{message="Choose one stock location first."});
        var rows=await lots.ReceiptLabelsAsync(id,action,int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return rows is null?NotFound(new{message="Received lot not found in this location."}):Ok(rows);
    }
}
