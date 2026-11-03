using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/scan"), Authorize]
public class ScanController : ControllerBase
{
    private readonly IScanService service;
    // ASP.NET supplies these services when it creates this class.
    public ScanController(IScanService service)
    {
        this.service = service;
    }

    // Query form also supports manufacturer codes containing slashes, # or ?.
    [HttpGet]
    public Task<IActionResult> Query([FromQuery] string code = "")
    {
        return Lookup(code);
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Lookup(string code)
    {
        if (await service.LookupAsync(code) is { } result)
        {
            return Ok(result);
        }
        else
        {
            return NotFound(new
            {
                message = "No product or serial number found."
            });
        }
    }
}
