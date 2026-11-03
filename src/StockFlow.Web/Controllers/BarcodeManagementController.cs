using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/labels/management/lots")]
public sealed class BarcodeManagementController(IBarcodeManagementRepository repository,BusinessScope scope,PermissionContext access) : ControllerBase
{
    private bool Location=>scope.LocationId>0&&!scope.Consolidated;
    private int UserId=>int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> List()
    {
        if(!access.Can("BARCODES","VIEW"))return Forbid();
        if(!Location)return BadRequest(new{message="Choose one stock location first."});
        return Ok(await repository.ListAsync());
    }
    [HttpGet("{kind}/{id:long}")]
    public async Task<IActionResult> Read(string kind,long id)
    {
        if(!access.Can("BARCODES","VIEW"))return Forbid();
        if(!Location)return BadRequest(new{message="Choose one stock location first."});
        var result=await repository.ReadAsync(kind,id);return result is null?NotFound():Ok(result);
    }
    public sealed class PrepareRequest { [Required,MinLength(1),MaxLength(500)] public string[] Keys {get;set;}=[]; }
    [HttpPost("{kind}/{id:long}/prepare")]
    public async Task<IActionResult> Prepare(string kind,long id,PrepareRequest request)
    {
        if(!access.Can("BARCODES","VIEW"))return Forbid();
        if(!Location)return BadRequest(new{message="Choose one stock location first."});
        var before=await repository.ReadAsync(kind,id);if(before is null)return NotFound();
        if(before.Rows.Any(r=>request.Keys.Contains(r.Key)&&string.IsNullOrWhiteSpace(r.Barcode))&&!access.Can("BARCODES","GENERATE"))return Forbid();
        var result=await repository.PrepareAsync(kind,id,request.Keys,UserId);return result is null?NotFound():Ok(result);
    }
    [HttpPost("{kind}/{id:long}/print")]
    [HttpPost("{kind}/{id:long}/reprint")]
    public async Task<IActionResult> Print(string kind,long id,BarcodePrintRequest request)
    {
        var action=Request.Path.Value!.EndsWith("/reprint")?"REPRINT":"PRINT";
        if(!access.Can("BARCODES",action))return Forbid();
        if(!Location)return BadRequest(new{message="Choose one stock location first."});
        if(request.Action!=action)return BadRequest(new{message="Print action does not match this request."});
        var result=await repository.PrintAsync(kind,id,request,UserId);return result is null?NotFound():Ok(result);
    }
}
