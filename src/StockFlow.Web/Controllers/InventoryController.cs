using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/inventory"), Authorize]
public class InventoryController : ControllerBase
{
    private readonly IInventoryRepository repository;
    private readonly StockFlow.Application.Services.RetailVariantSelection selection;
    // ASP.NET supplies these services when it creates this class.
    private readonly StockFlow.Web.Security.PermissionContext access;
    public InventoryController(IInventoryRepository repository, StockFlow.Application.Services.RetailVariantSelection selection, StockFlow.Web.Security.PermissionContext access)
    {
        this.repository = repository;
        this.selection = selection;
        this.access = access;
    }

    [HttpGet]
    public async Task<IActionResult> Balances(string? term, bool lowStockOnly = false, bool includeInactive = false,bool paged=false,string? stockStatus=null,int page=1,int pageSize=10)
    {
        if(paged){
            if(!lowStockOnly||includeInactive)throw new ValidationException("Paged shortage monitoring requires active low-stock items.");
            return Ok(await repository.GetLowStockPageAsync(term,stockStatus,page,pageSize));
        }
        return Ok(await repository.GetBalancesAsync(term, lowStockOnly, includeInactive));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(int? productId, DateTime? from, DateTime? to, int offset = 0, long? variantId = null)
    {
        if (offset < 0 || productId <= 0 || from > to)
        {
            throw new ValidationException("Choose valid product, date range and offset filters.");
        }

        return Ok(await repository.GetHistoryAsync(productId, from, to, offset, variantId));
    }

    [HttpGet("units")]
    public async Task<IActionResult> Units(int? productId, string? term, bool availableOnly = true, int offset = 0, long? variantId = null)
    {
        if (offset < 0 || productId <= 0)
        {
            throw new ValidationException("Choose a valid product and offset.");
        }

        return Ok(await repository.GetUnitsAsync(productId, term, availableOnly, offset, variantId));
    }

    [HttpPost("adjustments"), Authorize]
    public async Task<IActionResult> Adjust(StockAdjustmentRequest request)
    {
        if ((request.PurchaseId.HasValue || request.PurchaseItemId.HasValue) && (!access.CanExplicit("PURCHASES", "EDIT") || !access.Can("PURCHASES", "APPROVE"))) return Forbid();
        await selection.ValidateAsync([(request.ProductId,request.VariantId,(long?)null)]);
        InventoryRules.Validate(request);
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        return Ok(new
        {
            adjustmentId = await repository.AdjustAsync(request, userId)
        });
    }
}
