using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/purchases"), Authorize]
public class PurchaseController : ControllerBase
{
    private readonly IPurchaseService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public PurchaseController(IPurchaseService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await service.GetAllAsync());
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        if (await service.GetByIdAsync(id) is { } result)
        {
            return Ok(result);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("{id:long}/invoice-attachment")]
    public async Task<IActionResult> Attachment(long id)
    {
        if (await service.GetAttachmentAsync(id) is { } file)
        {
            return File(file.Data, file.ContentType, file.FileName);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePurchaseRequest request)
    {
        if (request.CompleteImmediately && !access.Can("PURCHASES", "APPROVE"))
        {
            return Forbid();
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        var id = await service.CreateAsync(request, userId);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, new
        {
            purchaseId = id
        });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> UpdateDraft(long id, CreatePurchaseRequest request)
    {
        if (!access.CanExplicit("PURCHASES", "EDIT")) return Forbid();
        if (request.CompleteImmediately && !access.Can("PURCHASES", "APPROVE"))
        {
            return Forbid();
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        if (await service.UpdateDraftAsync(id, request, userId))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }

    [HttpPost("{id:long}/complete")]
    public async Task<IActionResult> Complete(long id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        if (await service.CompleteAsync(id, userId))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }
}
