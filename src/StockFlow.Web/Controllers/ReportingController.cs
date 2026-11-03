using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/reports"), Authorize]
public class ReportingController : ControllerBase
{
    private readonly IReportingRepository repository;
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public ReportingController(IReportingRepository repository, PermissionContext access)
    {
        this.repository = repository;
        this.access = access;
    }

    [HttpGet("activity")]
    public async Task<IActionResult> Activity(DateTime from, DateTime to)
    {
        if (from == default || to == default || from.Date > to.Date || to.Date == DateTime.MaxValue.Date || (to.Date - from.Date).TotalDays > 3660)
        {
            return BadRequest(new
            {
                message = "Choose valid start and end dates (maximum ten years)."
            });
        }

        if (!access.Can("INVENTORY") && !access.Can("SALES"))
        {
            return StatusCode(403, new
            {
                message = "Inventory or sales report access is required."
            });
        }

        var report = await repository.ActivityAsync(from, to, access.Can("INVENTORY"), access.Can("SALES"));
        var days = (to.Date-from.Date).Days+1;
        if (access.Can("SALES") && from.Date > DateTime.MinValue.AddDays(days))
            report.PreviousFinancial = await repository.FinancialAsync(from.Date.AddDays(-days), from.Date.AddDays(-1));
        return Ok(report);
    }
}
