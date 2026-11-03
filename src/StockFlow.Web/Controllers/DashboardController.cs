using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Dashboard;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Route("api/dashboard"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public DashboardController(IDashboardService service, StockFlow.Web.Security.PermissionContext access)
    {
        this.service = service;
        this.access = access;
    }

    bool Admin => access.Can("SUPPLIERS");

    bool Sales => access.Can("SALES");

    [HttpGet("inventory-summary")]
    public async Task<IActionResult> Inventory()
    {
        var data = await service.GetInventorySummaryAsync(Admin);
        access.Filter(data);
        return Ok(data);
    }

    [HttpGet("business-summary")]
    public async Task<IActionResult> Business()
    {
        var data = await service.GetBusinessSummaryAsync(Sales);
        access.Filter(data);
        return Ok(data);
    }

    [HttpGet("inflow-outflow-trend")]
    public async Task<IActionResult> Trend(string period = "6months")
    {
        var data = (await service.GetTrendAsync(period)).ToList();
        foreach (var m in data)
        {
            if (!access.Can("STOCK_INFLOW"))
            {
                m.Inflow = 0;
            }

            if (!access.Can("STOCK_OUTFLOW"))
            {
                m.Outflow = 0;
            }
        }

        return Ok(data);
    }

    [HttpGet("category-stock")]
    public async Task<IActionResult> Categories()
    {
        return Ok(await service.GetCategoryStockAsync());
    }

    [HttpGet("recent-inflow")]
    public async Task<IActionResult> Inflow()
    {
        return Ok(await service.GetMovementsAsync(new()
        {
            Incoming = true
        }, Admin, 5));
    }

    [HttpGet("recent-outflow")]
    public async Task<IActionResult> Outflow()
    {
        return Ok(await service.GetMovementsAsync(new(), Admin, 5));
    }

    [HttpGet("recent-sales"), Authorize]
    public async Task<IActionResult> RecentSales()
    {
        return Ok(await service.GetRecentSalesAsync());
    }

    [HttpGet("recent-services")]
    public async Task<IActionResult> RecentServices()
    {
        return Ok(await service.GetRecentServicesAsync());
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock(int offset = 0)
    {
        return Ok(await service.GetLowStockAsync(offset));
    }

    [HttpGet("movements")]
    public async Task<IActionResult> Movements([FromQuery] DashboardMovementSearch search)
    {
        if (!access.Can(search.Incoming ? "STOCK_INFLOW" : "STOCK_OUTFLOW"))
        {
            return Forbid();
        }

        return Ok(await service.GetMovementsAsync(search, Admin));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string term)
    {
        return Ok((await service.SearchAsync(term)).Where(r => access.CanVisit(r.Url)));
    }

    [HttpGet("users"), Authorize]
    public async Task<IActionResult> Users()
    {
        return Ok(await service.GetUsersAsync());
    }
}
