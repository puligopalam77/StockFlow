using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/screenlanguages")]
public class ScreenLanguagesController : ControllerBase
{
    private readonly IScreenLanguageRepository repository;
    private readonly BusinessScope scope;
    private readonly PermissionContext access;
    public ScreenLanguagesController(IScreenLanguageRepository repository, BusinessScope scope, PermissionContext access)
    {
        this.repository = repository;
        this.scope = scope;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!await repository.IsAvailableAsync()) return SetupRequired();
        return Ok((await repository.GetAsync()).Where(setting => setting.Screen == "application"));
    }

    [HttpPut]
    public async Task<IActionResult> Save(ScreenLanguageSetting setting)
    {
        if (!scope.CanReviewStores || !access.Can("COMPANY_SETTINGS", "EDIT"))
            return Forbid();
        if (!await repository.IsAvailableAsync()) return SetupRequired();
        if (setting.Screen != "application" || setting.Captions is null || setting.Captions.Count != 0)
            return BadRequest(new { message = "Choose one language for the whole app." });
        await repository.SaveAsync(setting);
        return NoContent();
    }

    private ObjectResult SetupRequired() => StatusCode(503, new
    {
        code = "SCREEN_LANGUAGE_SETUP_REQUIRED",
        message = "Screen language setup is incomplete. Apply database/migrations/036_screen_languages.sql after migration 035 to the database used by StockFlow, then refresh this page. Ordinary screens will continue in English."
    });
}
