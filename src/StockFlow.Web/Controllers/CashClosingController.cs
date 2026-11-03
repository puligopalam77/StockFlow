using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/cashclosing")]
public class CashClosingController : ControllerBase
{
    private readonly ICashClosingRepository repository;
    private readonly BusinessScope scope;
    private readonly ShopTimeOptions time;
    private readonly TimeProvider clock;

    public CashClosingController(ICashClosingRepository repository, BusinessScope scope, IOptions<ShopTimeOptions> time, TimeProvider clock)
    {
        this.repository = repository;
        this.scope = scope;
        this.time = time.Value;
        this.clock = clock;
    }

    [HttpGet]
    public async Task<IActionResult> Get(DateTime? date)
    {
        if (!scope.CanReviewStores)
            return StatusCode(403, new
            {
                message = "Only the shop owner or administrator can review cash closing."
            });
        if (scope.LocationId <= 0 || scope.Consolidated)
            return BadRequest(new
            {
                message = "Select one shop location first."
            });
        return Ok(await repository.GetAsync(date ?? time.Today(clock)));
    }

    [HttpPost]
    public async Task<IActionResult> Save(CashClosingRequest request)
    {
        if (!scope.CanReviewStores)
            return StatusCode(403, new
            {
                message = "Only the shop owner or administrator can save cash closing."
            });
        CashClosingRules.Validate(request, time.Today(clock));
        return Ok(await repository.SaveAsync(request));
    }
}
