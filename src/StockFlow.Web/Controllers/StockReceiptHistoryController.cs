using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/stock-receipts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StockReceiptHistoryController : ControllerBase
{
    private readonly IStockReceiptHistoryRepository repository;
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public StockReceiptHistoryController(IStockReceiptHistoryRepository repository, PermissionContext access)
    {
        this.repository = repository;
        this.access = access;
    }

    private static readonly string[] Types = ["PURCHASE", "CUSTOMER_RETURN", "ADJUSTMENT_IN", "OPENING_STOCK"];
    private static readonly int[] PageSizes = [10, 25, 50, 100];
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] StockReceiptSearch search)
    {
        Demand();
        Validate(search);
        return Ok(await repository.SearchAsync(search));
    }

    [HttpGet("items")]
    public async Task<IActionResult> Items([FromQuery] StockReceiptSearch search)
    {
        Demand();
        Validate(search);
        return Ok(await repository.SearchItemsAsync(search));
    }

    [HttpGet("suppliers")]
    public async Task<IActionResult> Suppliers()
    {
        Demand();
        return Ok(await repository.GetSuppliersAsync());
    }

    [HttpGet("{source}/{id:long}")]
    public async Task<IActionResult> Detail(string source, long id)
    {
        Demand();
        source = source.Trim().ToUpperInvariant();
        if (id <= 0 || source is not ("PURCHASE" or "OPENING_STOCK" or "CUSTOMER_RETURN" or "STOCK_COUNT" or "ADJUSTMENT"))
        {
            throw new ValidationException("Choose a valid stock receipt.");
        }

        if (await repository.GetDetailAsync(source, id) is { } result)
        {
            return Ok(result);
        }
        else
        {
            return NotFound();
        }
    }

    private void Demand()
    {
        if (!access.Can("STOCK_INFLOW", "VIEW"))
        {
            throw new UnauthorizedAccessException("Stock receipt history access is required.");
        }
    }

    private static void Validate(StockReceiptSearch search)
    {
        search.Term = search.Term?.Trim() ?? "";
        search.Type = search.Type?.Trim().ToUpperInvariant() ?? "";
        if (search.Term.Length > 200 || search.Term.Any(char.IsControl))
        {
            throw new ValidationException("Search text must be at most 200 characters without control characters.");
        }

        if (search.Type.Length > 0 && !Types.Contains(search.Type))
        {
            throw new ValidationException("Choose a valid stock receipt type.");
        }

        if (search.SupplierId <= 0)
        {
            search.SupplierId = null;
        }

        if (search.From > search.To)
        {
            throw new ValidationException("From date cannot be after to date.");
        }

        if (search.Page < 1 || !PageSizes.Contains(search.PageSize))
        {
            throw new ValidationException("Choose a valid page and rows-per-page value.");
        }

        search.From = search.From?.Date;
        search.To = search.To?.Date;
    }
}
