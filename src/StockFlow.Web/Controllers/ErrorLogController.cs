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

        var unavailable = DatabaseUnavailable(error.Error);
        var status = unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
        Response.StatusCode = status;
        var message = unavailable
            ? "StockFlow cannot reach its database. Please check that the database server and its network connection are available, then try again."
            : "An error occurred while processing your request. Please try again or contact your administrator with the request ID.";
        if (error.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return new ObjectResult(new
            {
                message,
                requestId = HttpContext.TraceIdentifier
            })
            {
                StatusCode = status
            };
        }

        ViewData["ServiceUnavailable"] = unavailable;
        ViewData["ErrorMessage"] = message;
        return View("~/Views/ErrorLog/Error.cshtml", HttpContext.TraceIdentifier);
    }

    private static bool DatabaseUnavailable(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            // PostgreSQL statement/constraint errors are not connection outages.
            // Data Protection can wrap a connection failure in CryptographicException.
            if (current is Npgsql.NpgsqlException and not Npgsql.PostgresException) return true;
        }
        return false;
    }
}
