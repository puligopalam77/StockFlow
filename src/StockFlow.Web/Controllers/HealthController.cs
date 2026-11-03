using Microsoft.AspNetCore.Mvc;

namespace StockFlow.Web.Controllers;

[ApiController]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            application = "StockFlow",
            status = "Running"
        });
    }
}
