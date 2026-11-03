using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Service;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/service"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ServiceController : ControllerBase
{
    private readonly IServiceService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public ServiceController(IServiceService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ServiceSearch search)
    {
        return Ok(await service.GetAllAsync(search));
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] ServiceSearch search)
    {
        search.PendingOnly = true;
        return Ok(await service.GetAllAsync(search));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        if (await service.GetByIdAsync(id) is { } item)
        {
            return Ok(item);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("units/{unitId:long}")]
    public async Task<IActionResult> Unit(long unitId)
    {
        if (await service.GetUnitContextAsync(unitId) is { } item)
        {
            return Ok(item);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("technicians")]
    public async Task<IActionResult> Technicians()
    {
        return Ok(await service.GetTechniciansAsync());
    }

    [HttpGet("{id:long}/parts")]
    public async Task<IActionResult> Parts(long id, int offset = 0)
    {
        return Ok(await service.GetPartsAsync(id, offset));
    }

    [HttpGet("{id:long}/history")]
    public async Task<IActionResult> History(long id, int offset = 0)
    {
        return Ok(await service.GetHistoryAsync(id, offset));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Create(CreateServiceRequest request)
    {
        if (request.AssignedTo.HasValue && !access.Can("SERVICE", "ASSIGN"))
        {
            return Forbid();
        }

        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        var id = await service.CreateAsync(request, user);
        return CreatedAtAction(nameof(Get), new
        {
            id
        }, new
        {
            serviceRequestId = id
        });
    }

    [HttpPut("{id:long}"), Authorize]
    public async Task<IActionResult> Update(long id, UpdateServiceRequest request)
    {
        if (!access.Can("SERVICE", "UPDATE"))
        {
            return Forbid();
        }

        var existing = await service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (request.AssignedTo != existing.AssignedTo && !access.Can("SERVICE", "ASSIGN"))
        {
            return Forbid();
        }

        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        await service.UpdateAsync(id, request, user);
        return NoContent();
    }

    [HttpPut("{id:long}/status"), Authorize]
    public async Task<IActionResult> Status(long id, UpdateServiceStatusRequest request)
    {
        if (!access.Can("SERVICE",
string.Equals(request.Status?.Trim(), "DELIVERED", StringComparison.OrdinalIgnoreCase) ? "DELIVER" : "UPDATE"))
        {
            return Forbid();
        }

        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        await service.UpdateStatusAsync(id, request, user);
        return NoContent();
    }

    [HttpPost("{id:long}/parts"), Authorize]
    public async Task<IActionResult> AddPart(long id, AddServicePartRequest request)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        return Ok(new
        {
            servicePartId = await service.AddPartAsync(id, request, user)
        });
    }

    private bool TryUser(out int id)
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id > 0;
    }
}
