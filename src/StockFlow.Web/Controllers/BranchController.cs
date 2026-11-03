using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/branches")]
public class BranchController : ControllerBase
{
    private readonly IBranchRepository repository;
    private readonly BusinessScope scope;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public BranchController(IBranchRepository repository,
BusinessScope scope,
StockFlow.Web.Security.PermissionContext access)
    {
        this.repository = repository;
        this.scope = scope;
        this.access = access;
    }

    [HttpGet("context")]
    public async Task<IActionResult> ContextInfo()
    {
        return Ok(new
        {
            scope.TenantId,
            businessProfileCode = scope.BusinessProfileCode,
            businessProfileName = BusinessProfiles.Name(scope.BusinessProfileCode),
            businessProfilePersisted = scope.BusinessProfilePersisted,
            businessProfileVersion = scope.BusinessProfileVersion,
            businessProfileConfigurationAvailable = scope.BusinessProfileConfigurationAvailable,
            scope.IsOwner,
            canReviewStores = scope.CanReviewStores,
            locations = (await repository.LocationsAsync()).Where(l => scope.CanReviewStores || l.LocationId == scope.LocationId),
            locationId = User.FindFirstValue("LocationId"),
            storeId = User.FindFirstValue("StoreId")
        });
    }

    public class SwitchRequest
    {
        public long LocationId
        {
            get; set;
        }
        public long StoreId
        {
            get; set;
        }
    }

    [HttpPost("context")]
    public async Task<IActionResult> Switch(SwitchRequest request)
    {
        if (!scope.CanReviewStores)
        {
            return StatusCode(403,
new
{
    message = "Your store is assigned automatically. Only the owner or an administrator can switch store views."
});
        }

        var locations = (await repository.LocationsAsync()).ToList();
        var chosen = locations.SingleOrDefault(l => l.LocationId == request.LocationId);
        if (request.LocationId < 0 || request.StoreId < 0 || request.LocationId > 0 && chosen is null || request.LocationId == 0 && request.StoreId > 0 && !locations.Any(l => l.StoreId == request.StoreId))
        {
            return Forbid();
        }

        var auth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(User.Claims.Where(c => c.Type is not ("LocationId" or "StoreId")), CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new("LocationId", request.LocationId.ToString()));
        identity.AddClaim(new("StoreId", (chosen?.StoreId ?? (chosen is null ? request.StoreId : 0)).ToString()));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), auth.Properties);
        return NoContent();
    }

    [HttpGet("stores")]
    public async Task<IActionResult> Stores()
    {
        return Ok(await repository.StoresAsync());
    }

    [HttpPost("stores")]
    public async Task<IActionResult> CreateStore(StoreRequest request)
    {
        return Ok(new
        {
            storeId = await repository.CreateStoreAsync(request)
        });
    }

    [HttpPost("locations")]
    public async Task<IActionResult> CreateLocation(LocationRequest request)
    {
        return Ok(new
        {
            locationId = await repository.CreateLocationAsync(request)
        });
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> Access(int id)
    {
        return Ok(await repository.AccessAsync(id));
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> Access(int id, StoreAccessRequest request)
    {
        await repository.SetAccessAsync(id, request);
        return NoContent();
    }

    [HttpGet("comparison")]
    public async Task<IActionResult> Comparison(string? term)
    {
        return Ok(await repository.ComparisonAsync(term));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        return Ok(await repository.SummaryAsync(access.Can("SALES")));
    }
}
