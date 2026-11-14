using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Categories;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoryController : ControllerBase
{
    private readonly ICategoryService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public CategoryController(ICategoryService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(bool activeOnly = false, string? term = null, bool listSearch = false)
    {
        // Other category pickers/search consumers retain their existing semantics.
        // Management needs the complete scoped hierarchy for filters and counts.
        if (!listSearch) return Ok(await service.GetAllAsync(activeOnly, term));
        var categories = (await service.GetAllAsync()).ToList();
        var search = term?.Trim();
        var matches = categories.Where(c => (!activeOnly || c.IsEffectivelyActive)
            && (string.IsNullOrEmpty(search)
                || c.CategoryName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || c.CategoryCode?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            .Select(c => c.CategoryId).ToHashSet();
        var parents = categories.Where(c => matches.Contains(c.CategoryId) && c.ParentCategoryId.HasValue)
            .Select(c => c.ParentCategoryId!.Value).ToHashSet();
        return Ok(categories.Where(c => matches.Contains(c.CategoryId)
            || parents.Contains(c.CategoryId)
            || c.ParentCategoryId is int parent && matches.Contains(parent) && (!activeOnly || c.IsEffectivelyActive)));
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

    [HttpGet("{id:int}/products")]
    public async Task<IActionResult> Products(int id, [FromServices] IProductService products)
    {
        if (!access.Can("PRODUCTS")) return Forbid();
        if (await service.GetByIdAsync(id) is null) return NotFound();
        var ids = (await service.GetAllAsync()).Where(c => c.CategoryId == id || c.ParentCategoryId == id).Select(c => c.CategoryId).ToHashSet();
        ids.Add(id);
        return Ok((await products.GetAllAsync()).Where(p => ids.Contains(p.CategoryId))
            .Select(p => new { p.ProductId, p.ProductName, p.CategoryId, p.ParentProductCode, p.IsActive }));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string? term, bool activeOnly = true)
    {
        return Ok((await service.GetAllAsync(activeOnly, term)).Take(20));
    }

    [HttpGet("sku-prefix-suggestion")]
    public async Task<IActionResult> SuggestSkuPrefix(string? name,[FromServices] StockFlow.Infrastructure.Data.DbConnectionFactory factory)
    {
        if(!access.Can("CATEGORIES","CREATE")&&!access.Can("CATEGORIES","EDIT"))return Forbid();
        return Ok(await new StockFlow.Infrastructure.Repositories.CatalogIdentifierAllocator(factory).SuggestPrefixAsync(name));
    }

    [HttpGet("tree")]
    public async Task<IActionResult> Tree(bool activeOnly = false, string? term = null)
    {
        var categories = (await service.GetAllAsync(activeOnly, term)).ToList();
        var children = categories.ToLookup(c => c.ParentCategoryId);
        var ids = categories.Select(c => c.CategoryId).ToHashSet();
        foreach (var category in categories) category.Children = children[category.CategoryId].ToList();
        return Ok(categories.Where(c => c.ParentCategoryId is not int parent || !ids.Contains(parent)));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Create(CreateCategoryRequest request)
    {
        if ((!request.IsActive && !access.Can("CATEGORIES","DELETE")) || request.Configuration is not null && !access.Can("CATEGORIES","EDIT")) return Forbid();
        var id = await service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, await service.GetByIdAsync(id));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, UpdateCategoryRequest request)
    {
        var existing = await service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.IsActive != request.IsActive && !access.Can("CATEGORIES", "DELETE"))
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

    public sealed record StatusRequest(bool IsActive);
    [HttpPost("{id:int}/status"), Authorize]
    public async Task<IActionResult> Status(int id, StatusRequest request)
    {
        if (!access.Can("CATEGORIES", "DELETE")) return Forbid();
        return await service.SetStatusAsync(id, request.IsActive) ? NoContent() : NotFound();
    }
}
