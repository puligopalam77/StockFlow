using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/categories/schema")]
public sealed class CatalogSchemaController(ICatalogSchemaRepository repository, PermissionContext access) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await repository.GetAsync());

    [HttpPut]
    public async Task<IActionResult> Save(CatalogSchema schema)
    {
        if (!access.Can("CATEGORIES", "EDIT")) return Forbid();
        await repository.SaveAsync(schema);
        return Ok(await repository.GetAsync());
    }
}
