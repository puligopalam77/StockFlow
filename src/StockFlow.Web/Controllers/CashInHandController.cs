using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/cashinhand")]
public sealed class CashInHandController(ICashInHandRepository repository, BusinessScope scope, IOptions<ShopTimeOptions> time, TimeProvider clock, StockFlow.Web.Security.PermissionContext access) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(DateTime? date,string? search,int page=1,string? type=null,string? direction=null)
    {
        if(!scope.CanReviewStores)return Forbid();
        var report=await repository.GetAsync(date??time.Value.Today(clock),search,page,type,direction);
        foreach(var movement in report.Movements)
            if(movement.SourceModule is null || !access.Can(movement.SourceModule))movement.SourceUrl=null;
        return Ok(report);
    }
    [HttpPost]
    public async Task<IActionResult> Adjust(CashAdjustmentRequest request)
    {
        if(!scope.CanReviewStores)return Forbid();
        return Ok(new{adjustmentId=await repository.AdjustAsync(request)});
    }

    [HttpPost("opening-review")]
    public async Task<IActionResult> Review(CashOpeningReviewRequest request)
    {
        if(!scope.CanReviewStores)return Forbid();
        await repository.ReviewOpeningExpenseAsync(request);
        return Ok(new{request.RequestId});
    }
}
