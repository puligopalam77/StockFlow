using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Brands;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController]
[Route("api/brands")]
[Authorize]
public class BrandController : ControllerBase
{
    private readonly IBrandService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    private readonly ICategoryService categories;
    private readonly StockFlow.Application.Common.BusinessScope? scope;
    // ASP.NET supplies these services when it creates this class.
    public BrandController(IBrandService service,
StockFlow.Web.Security.PermissionContext access,
ICategoryService categories, StockFlow.Application.Common.BusinessScope? scope=null)
    {
        this.service = service;
        this.access = access;
        this.categories = categories;
        this.scope=scope;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories()
    {
        return Ok((await categories.GetAllAsync()).Select(c => new { c.CategoryId, c.CategoryName, c.CategoryPath,c.ParentCategoryId,c.CategoryLevel,c.IsActive }));
    }
    [HttpGet("registration-options")]
    public async Task<IActionResult> RegistrationOptions()=>Ok(new{detailsAvailable=await service.RegistrationDetailsAvailableAsync()});
    [HttpGet("{id:int}/logo")]
    public async Task<IActionResult> Logo(int id){var logo=await service.GetLogoAsync(id);if(logo is null)return NotFound();Response.Headers.CacheControl="private, no-store";return File(logo.Validate(),logo.ContentType);}

    [HttpGet]
    public async Task<IActionResult> GetAll(bool activeOnly = false, string? term = null, int? categoryId = null)
    {
        var brands = await service.GetAllAsync(activeOnly, term);
        return Ok(categoryId.HasValue ? brands.Where(b => scope?.CatalogRegistrationEnhancementsAvailable==true ? b.CategoryIds.Length==0||b.ApplicableCategoryIds.Contains(categoryId.Value) : b.CategoryIds.Contains(categoryId.Value)) : brands);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (await service.GetByIdAsync(id) is { } entity)
        {
            return Ok(entity);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string? term, bool activeOnly = true)
    {
        return Ok((await service.GetAllAsync(activeOnly, term)).Take(20));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Create(CreateBrandRequest request)
    {
        if(!request.IsActive&&!access.Can("BRANDS","DELETE"))return Forbid();
        var id = await service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, await service.GetByIdAsync(id));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, UpdateBrandRequest request)
    {
        var existing = await service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.IsActive != request.IsActive && !access.Can("BRANDS", "DELETE"))
        {
            return Forbid();
        }

        if (await service.UpdateAsync(id, request))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (await service.DeleteAsync(id))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }
}
