using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace StockFlow.Web.Controllers;

[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[IgnoreAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ErrorLogController : Controller
{
    [Route("/error-log")]
    public IActionResult Error()
    {
        var error = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (error is null)
        {
            return NotFound();
        }

        Response.StatusCode = 500;
        const string message = "An error occurred while processing your request. Please try again or contact your administrator with the request ID.";
        if (error.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return new ObjectResult(new
            {
                message,
                requestId = HttpContext.TraceIdentifier
            })
            {
                StatusCode = 500
            };
        }

        return View("~/Views/ErrorLog/Error.cshtml", HttpContext.TraceIdentifier);
    }
}
