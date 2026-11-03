using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/transfers")]
public class TransferController : ControllerBase
{
    private readonly ITransferRepository repository;
    private readonly IVariantRepository variants;
    // ASP.NET supplies these services when it creates this class.
    public TransferController(ITransferRepository repository, IVariantRepository variants)
    {
        this.repository = repository;
        this.variants = variants;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        return Ok(await repository.ListAsync());
    }

    [HttpGet("locations")]
    public async Task<IActionResult> Locations()
    {
        return Ok(await repository.LocationsAsync());
    }

    [HttpGet("variants")]
    public async Task<IActionResult> Variants(string? term)
    {
        return Ok((await variants.GetAsync(null, term, true)).Select(v => new { v.VariantId, v.ProductId, v.ProductName, v.Sku, v.IsSerialTracked, v.AttributeSummary, v.UnitOfMeasure }));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        if (await repository.GetAsync(id) is { } result)
        {
            return Ok(result);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("{id:long}/dispatch-units")]
    public async Task<IActionResult> DispatchUnits(long id, long lineId, string? term, int offset = 0)
    {
        return Ok(await repository.DispatchUnitsAsync(id, lineId, term, offset));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTransferRequest request)
    {
        return Ok(new
        {
            transferId = await repository.CreateAsync(request)
        });
    }

    [HttpPost("{id:long}/{actionName}")]
    public async Task<IActionResult> Act(long id, string actionName, TransferActionRequest request)
    {
        return Ok(new
        {
            transferId = await repository.ActAsync(id, actionName, request)
        });
    }
}
