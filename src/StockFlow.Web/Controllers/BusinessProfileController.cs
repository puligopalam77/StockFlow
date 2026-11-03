using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/business-profile")]
public class BusinessProfileController(IBusinessProfileRepository repository, BusinessScope scope, PermissionContext access) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!scope.CanReviewStores) return Forbid();
        var impact = await repository.ImpactAsync();
        return Ok(new
        {
            code = scope.BusinessProfileCode, name = BusinessProfiles.Name(scope.BusinessProfileCode),
            version = scope.BusinessProfileVersion, available = scope.BusinessProfileConfigurationAvailable,
            catalogSwitchingAvailable = scope.ProfileCatalogSwitchingAvailable,
            dataVisibilityAvailable = scope.ProfileDataVisibilityAvailable,
            canEdit = scope.CanReviewStores && access.Can("COMPANY_SETTINGS", "EDIT"), impact,
            profiles = BusinessProfiles.Codes.Where(code => !scope.ProfileCatalogSwitchingAvailable || BusinessProfiles.IsDemoProfile(code) || code == scope.BusinessProfileCode).Select(code => new
            {
                code, name = BusinessProfiles.Name(code), categories = BusinessProfiles.StarterCategories(code),
                serialTracking = BusinessProfiles.SupportsSerials(code), warrantyAndRepairs = BusinessProfiles.SupportsWarranty(code)
            })
        });
    }

    [HttpPut]
    public async Task<IActionResult> Save(BusinessProfileChange request)
    {
        if (!scope.CanReviewStores || !access.Can("COMPANY_SETTINGS", "EDIT")) return Forbid();
        if (!scope.BusinessProfileConfigurationAvailable)
            return StatusCode(503, new { message = "Apply database migration 047 manually before changing the shop profile." });
        if (!BusinessProfiles.IsSupported(request.Code) || !request.Acknowledged || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            return BadRequest(new { message = "Choose a supported profile, enter a reason, and acknowledge the change." });
        if (request.ExpectedVersion != scope.BusinessProfileVersion)
            return Conflict(new { message = "The shop profile changed in another session. Reload and review it again." });
        var impact = await repository.ImpactAsync();
        var retainedCatalogSwitch = scope.ProfileCatalogSwitchingAvailable && BusinessProfiles.IsDemoProfile(scope.BusinessProfileCode) && BusinessProfiles.IsDemoProfile(request.Code);
        if (request.Code != scope.BusinessProfileCode && !retainedCatalogSwitch && (impact.ProductCount > 0 || impact.TransactionCount > 0))
            return BadRequest(new { message = "Apply migration 050 manually to enable Electrical / Bike Accessories setup changes while retaining the existing records. No data has been changed." });
        var version = await repository.ChangeAsync(request);
        return Ok(new { code = request.Code, version });
    }
}
