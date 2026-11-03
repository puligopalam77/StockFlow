using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Suppliers;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public class SupplierController : ControllerBase
{
    private readonly ISupplierService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public SupplierController(ISupplierService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
    }

    [HttpGet("options")]
    public IActionResult Options()
    {
        return Ok(new
        {
            countries = StockFlow.Application.Common.SupplierRules.Countries.OrderBy(x => x.Value).Select(x => new { code = x.Key, name = x.Value }),
            supplierTypes = StockFlow.Application.Common.SupplierRules.Types
        });
    }

    [HttpGet("duplicates")]
    public async Task<IActionResult> Duplicates(string? name,
string? mobile,
string? alternate,
string? email,
string? gst,
int? excludingId,
string? country)
    {
        static string Text(string? s) => System.Text.RegularExpressions.Regex.Replace(s?.Trim().ToUpperInvariant() ?? "", @"\s+", " ");
        static string Phone(string? s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"[^0-9]", "");
        static bool Same(string a, string b) => a.Length > 0 && a == b;
        var rows = await service.GetAllAsync(false);
        return Ok(rows.Where(s => s.SupplierId != excludingId && (Same(Text(name), Text(s.SupplierName)) || Same(Text(email), Text(s.Email)) || Same(Text(gst), Text(s.GstNumber)) || new[] { mobile, alternate }.Any(p => new[] { s.MobileNumber, s.AlternateMobile }.Any(q => Same(Phone(p), Phone(q)) || country == "IN" && Phone(p).Length == 10 && Same("91" + Phone(p), Phone(q))))))
.Take(5));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(bool activeOnly = false, string? term = null)
    {
        return Ok(await service.GetAllAsync(activeOnly, term));
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
    public async Task<IActionResult> Create(CreateSupplierRequest request)
    {
        var id = await service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, await service.GetByIdAsync(id));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, UpdateSupplierRequest request)
    {
        var existing = await service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.IsActive != request.IsActive && !access.Can("SUPPLIERS", "DELETE"))
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
