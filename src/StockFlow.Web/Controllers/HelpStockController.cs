using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[Authorize, ApiController, Route("api/help/stock")]
public class HelpStockController(IInventoryRepository inventory, PermissionContext access, BusinessScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery, StringLength(150)] string? term, bool lowStockOnly = false)
    {
        if (!access.Can("INVENTORY")) return Forbid();
        if (scope.TenantId <= 0 || scope.UserId <= 0) return Unauthorized();
        if (!scope.CanReviewStores && (scope.Consolidated || scope.LocationId <= 0)) return Forbid();
        term = term?.Trim();
        if (!lowStockOnly && (string.IsNullOrEmpty(term) || term.Length < 2))
            return BadRequest(new { message = "Enter at least 2 characters of a product name, item code or barcode." });

        // Use exactly the same scoped repository as Stock on hand. No model sees these records.
        var balances = (await inventory.GetBalancesAsync(term, lowStockOnly, false)).ToList();
        var rows = balances.Take(50).Select(row => new
        {
            row.ProductId, row.VariantId, row.ProductName, row.Sku, row.Barcode,
            row.CurrentStock, row.IsSerialTracked,
            availableQuantity = row.IsSerialTracked ? row.AvailableUnits : row.CurrentStock,
            row.IsLowStock, row.HasDiscrepancy
        });
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            checkedAt = DateTimeOffset.UtcNow,
            scopeName = scope.LocationName, scope.Consolidated,
            truncated = balances.Count > 50,
            items = rows,
            source = new { label = "Stock on hand", url = "/Inventory" }
        });
    }
}
