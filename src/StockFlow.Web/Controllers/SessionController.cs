using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[Authorize, ApiExplorerSettings(IgnoreApi = true)]
[Route("account/session")]
public sealed class SessionController : Controller
{
    private readonly IdleSessionService sessions;
    private readonly ISharedSessionStore store;
    private readonly TimeProvider clock;
    // ASP.NET supplies these services when it creates this class.
    public SessionController(IdleSessionService sessions, ISharedSessionStore store, TimeProvider clock)
    {
        this.sessions = sessions;
        this.store = store;
        this.clock = clock;
    }

    public sealed record ActivityRequest(string SessionId, long AgeMilliseconds = 0, string? Reason = null);
    [HttpPost("activity")]
    public async Task<IActionResult> Activity([FromBody] ActivityRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        if (request.SessionId != User.FindFirst(IdleSessionService.ClaimType)?.Value)
        {
            return Conflict();
        }

        if (request.AgeMilliseconds < 0 || request.AgeMilliseconds >= sessions.IdleTimeout.TotalMilliseconds)
        {
            return BadRequest();
        }

        var status = await sessions.ValidateAsync(User, request.AgeMilliseconds);
        if (status.Active)
        {
            var ticket = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var properties = ticket.Properties ?? new AuthenticationProperties();
            properties.IssuedUtc = clock.GetUtcNow();
            properties.ExpiresUtc = clock.GetUtcNow().AddMilliseconds(status.RemainingMilliseconds);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, User, properties);
            return Json(status);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Unauthorized();
    }

    [HttpPost("expire")]
    public async Task<IActionResult> Expire([FromBody] ActivityRequest request)
    {
        if (request.SessionId != User.FindFirst(IdleSessionService.ClaimType)?.Value)
        {
            return Conflict();
        }

        HttpContext.Items["StockFlow.SessionEndReason"] = request.Reason == "absolute" ? "ABSOLUTE_TIMEOUT" : "IDLE_TIMEOUT";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("devices")]
    public async Task<IActionResult> Devices()
    {
        Response.Headers.CacheControl = "no-store";
        var current = User.FindFirst(IdleSessionService.ClaimType)?.Value;
        return Json((await store.ListAsync(IdleSessionService.Owner(User))).Select(session => new { session.SessionId, session.Device, session.IpAddress, session.CreatedAt, session.LastActivityAt, session.AbsoluteExpiresAt, isCurrent = session.SessionId == current }));
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] ActivityRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out _))
        {
            return BadRequest();
        }

        if (!await store.RevokeAsync(IdleSessionService.Owner(User), request.SessionId, "USER_REVOKED"))
        {
            return NotFound();
        }

        if (request.SessionId == User.FindFirst(IdleSessionService.ClaimType)?.Value)
        {
            HttpContext.Items["StockFlow.SessionEndReason"] = "USER_REVOKED";
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return NoContent();
    }
}
