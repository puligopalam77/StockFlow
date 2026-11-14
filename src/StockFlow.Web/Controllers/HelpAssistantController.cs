using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StockFlow.Application.Common;
using StockFlow.Web.Help;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[Authorize, ApiController, Route("api/help/assistant")]
public sealed class HelpAssistantController(RetailAssistantService assistant, HelpHealthService health, StockBotGuidance guidance,
    BusinessScope scope, PermissionContext access) : ControllerBase
{
    [HttpGet]
    public IActionResult Context([FromQuery] string? screen)
    {
        Response.Headers.CacheControl = "no-store";
        if (scope.TenantId <= 0 || scope.UserId <= 0)
            return Unauthorized();
        return Ok(new
        {
            scope = assistant.ScopeName,
            contextStamp = CreateContextStamp(),
            role = scope.IsOwner ? "Main Owner" : access.User?.RoleName ?? "Staff",
            canCompareShops = assistant.CanCompare,
            suggestions = assistant.Suggestions(screen is { Length: <= 150 } ? screen : null),
            articles = HelpCatalog.ForUser(access)
        });
    }

    private string CreateContextStamp()
    {
        // This fingerprint changes when the signed-in account's scope or grants
        // change. The browser uses it to discard answers from an older context.
        // All values come from authenticated server state, never the question.
        var context = new
        {
            scope.TenantId,
            scope.UserId,
            scope.StoreId,
            scope.LocationId,
            scope.Consolidated,
            scope.IsOwner,
            scope.CanReviewStores,
            scope.BusinessProfileCode,
            scope.BusinessProfileVersion,
            access.User?.SecurityVersion,
            access.User?.Permissions
        };
        var contextJson = JsonSerializer.Serialize(context);
        var contextBytes = Encoding.UTF8.GetBytes(contextJson);
        return Convert.ToHexString(SHA256.HashData(contextBytes));
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellation)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await health.CheckAsync(cancellation));
    }

    [HttpGet("guidance")]
    public IActionResult Guidance([FromQuery] string? screen)
    {
        Response.Headers.CacheControl = "no-store";
        if (screen is null || screen.Length > 100)
            return BadRequest(new
            {
                message = "Choose an approved screen identifier."
            });
        return Ok(guidance.Metadata(screen));
    }
}
