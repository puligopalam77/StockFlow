using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Customers;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomerController : ControllerBase
{
    private readonly ICustomerService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public CustomerController(ICustomerService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
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
    public async Task<IActionResult> Search(string? term, bool activeOnly = true, string? mobileNumber = null, string? gstNumber = null)
    {
        // Exact quick-add matches, including inactive records; normal picker searches
        // keep their existing behavior. The repository connection retains shop RLS.
        if (mobileNumber is not null || gstNumber is not null)
        {
            var phone = mobileNumber?.Trim() ?? "";
            var gst = gstNumber?.Trim().ToUpperInvariant() ?? "";
            if ((phone.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(phone, StockFlow.Application.Common.FieldPatterns.Phone)) ||
                (gst.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(gst, StockFlow.Application.Common.FieldPatterns.Gst)))
                return BadRequest(new { message = "Enter a valid mobile number or GSTIN to check existing customers." });
            var matches = new List<CustomerResponse>();
            if (phone.Length > 0) matches.AddRange((await service.GetAllAsync(false, phone)).Where(c => c.MobileNumber?.Trim() == phone));
            if (gst.Length > 0) matches.AddRange((await service.GetAllAsync(false, gst)).Where(c => string.Equals(c.GstNumber?.Trim(), gst, StringComparison.OrdinalIgnoreCase)));
            return Ok(matches.DistinctBy(c => c.CustomerId).Take(20));
        }
        return Ok((await service.GetAllAsync(activeOnly, term)).Take(20));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Create(CreateCustomerRequest request, [FromQuery] bool preventIdentifierDuplicates = false, [FromQuery] bool requireMobile = false)
    {
        if(requireMobile && string.IsNullOrWhiteSpace(request.MobileNumber))
            return BadRequest(new {message="Mobile number is required when adding a customer during checkout."});
        if(requireMobile){
            request.GstNumber=StockFlow.Application.Common.GstRules.Clean(request.GstNumber)?.ToUpperInvariant();
            StockFlow.Application.Common.GstRules.Gstin(request.GstNumber);
        }
        var id = await service.CreateAsync(request, preventIdentifierDuplicates);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, await service.GetByIdAsync(id));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest request)
    {
        var existing = await service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!access.Can("CUSTOMERS", "DELETE") && existing.IsActive != request.IsActive)
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
