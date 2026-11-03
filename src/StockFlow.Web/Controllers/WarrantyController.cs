using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Warranty;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/warranty"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WarrantyController : ControllerBase
{
    private readonly IWarrantyService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public WarrantyController(IWarrantyService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
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

    [HttpGet("serial/{serial}")]
    public async Task<IActionResult> Serial(string serial)
    {
        if (await service.GetBySerialAsync(serial) is { } item)
        {
            return Ok(item);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("unit/{id:long}")]
    public async Task<IActionResult> Unit(long id)
    {
        if (await service.GetByUnitAsync(id) is { } item)
        {
            return Ok(item);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("invoice/{invoice}")]
    public async Task<IActionResult> Invoice(string invoice)
    {
        return Ok(await service.GetByInvoiceAsync(invoice));
    }

    [HttpGet("expiring")]
    public async Task<IActionResult> Expiring(int days = 30, int offset = 0)
    {
        return Ok(await service.GetExpiringAsync(days, offset));
    }

    [HttpGet("claims")]
    public async Task<IActionResult> Claims(long? warrantyId, string? status, int offset = 0)
    {
        return Ok(await service.GetClaimsAsync(warrantyId, status, offset));
    }

    [HttpGet("{id:long}/history")]
    public async Task<IActionResult> History(long id, int offset = 0)
    {
        return Ok(await service.GetHistoryAsync(id, offset));
    }

    [HttpPost("register/{saleItemId:long}"), Authorize]
    public async Task<IActionResult> Register(long saleItemId)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        return Ok(new
        {
            warrantyId = await service.RegisterAsync(saleItemId, user)
        });
    }

    [HttpPost("claims"), Authorize]
    public async Task<IActionResult> Create(CreateWarrantyClaimRequest request)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        return Ok(new
        {
            claimId = await service.CreateClaimAsync(request, user)
        });
    }

    [HttpPut("claims/{id:long}"), Authorize]
    public async Task<IActionResult> Resolve(long id, ResolveWarrantyClaimRequest request)
    {
        var action = request.ClaimStatus?.Trim().ToUpperInvariant() switch
        {
            "APPROVED" => "APPROVE",
            "REPLACED" or "CLOSED" or "REJECTED" => "RESOLVE",
            _ => "UPDATE"
        };
        if (!access.Can("WARRANTY_CLAIMS", action))
        {
            return Forbid();
        }

        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        if (await service.ResolveClaimAsync(id, request, user))
        {
            return Ok(new
            {
                message = "Claim updated."
            });
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPut("{id:long}/coverage"), Authorize]
    public async Task<IActionResult> Coverage(long id, WarrantyCoverageRequest request)
    {
        if (!TryUser(out var user))
        {
            return Unauthorized();
        }

        if (await service.SetCoverageAsync(id, request, user))
        {
            return Ok(new
            {
                message = "Coverage updated."
            });
        }
        else
        {
            return NotFound();
        }
    }

    private bool TryUser(out int id)
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id) && id > 0;
    }
}
