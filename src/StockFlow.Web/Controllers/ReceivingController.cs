using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/receiving")]
public class ReceivingController : ControllerBase
{
    private readonly IStockIntakeRepository repository;
    private readonly BusinessScope scope;
    private readonly StockFlow.Web.Security.PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public ReceivingController(IStockIntakeRepository repository, BusinessScope scope, StockFlow.Web.Security.PermissionContext access)
    {
        this.repository = repository;
        this.scope = scope;
        this.access = access;
    }

    [HttpPost]
    public Task<IActionResult> Receive(StockIntakeRequest request)
    {
        return Post(request, false);
    }

    [HttpPost("opening")]
    public Task<IActionResult> Opening(StockIntakeRequest request)
    {
        return Post(request, true);
    }

    public sealed class ImportKeyRequest
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(2000000)]
        public string Canonical { get; set; } = "";
        public long LocationId { get; set; }
    }

    [HttpPost("opening/import-key")]
    public IActionResult OpeningImportKey(ImportKeyRequest request)
    {
        if (!access.Can("STOCK_ADJUSTMENT", "ADJUST")) return Forbid();
        if (!scope.CatalogRegistrationEnhancementsAvailable || scope.Consolidated || scope.LocationId<=0 || request.LocationId!=scope.LocationId)
            return BadRequest(new { message="Choose the same active stock location and apply migration 052 before importing." });
        var key=$"{scope.TenantId}:{scope.LocationId}:{scope.BusinessProfileCode}:opening-csv:{request.Canonical}";
        var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Ok(new { requestId=new Guid(hash.AsSpan(0,16)) });
    }

    [HttpGet("{id:long}/labels")]
    public async Task<IActionResult> Labels(long id)
    {
        return await ReadLabels(id, null);
    }

    [HttpPost("{id:long}/labels/generate")]
    public async Task<IActionResult> GenerateItemLabels(long id)
    {
        if (!access.Can("BARCODES", "GENERATE")) return Forbid();
        if (scope.LocationId <= 0 || scope.Consolidated) return BadRequest(new { message = "Choose one stock location first." });
        await repository.GenerateItemLabelsAsync(id, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return await ReadLabels(id, null);
    }

    [HttpPost("{id:long}/labels/print")]
    public async Task<IActionResult> PrintLabels(long id)
    {
        return await ReadLabels(id, "PRINT");
    }

    [HttpPost("{id:long}/labels/reprint")]
    public async Task<IActionResult> ReprintLabels(long id)
    {
        return await ReadLabels(id, "REPRINT");
    }

    private async Task<IActionResult> ReadLabels(long id, string? action)
    {
        if (scope.LocationId <= 0 || scope.Consolidated)
        {
            return BadRequest(new
            {
                message = "Select one stock location to prepare labels."
            });
        }

        var labels = await repository.LabelsAsync(id, action, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (labels is null)
        {
            return NotFound(new
            {
                message = "Receipt not found in this location."
            });
        }
        else
        {
            return Ok(labels);
        }
    }

    private async Task<IActionResult> Post(StockIntakeRequest request, bool opening)
    {
        if (scope.LocationId <= 0 || scope.Consolidated || request.LocationId.HasValue && request.LocationId != scope.LocationId)
        {
            return BadRequest(new
            {
                message = "This receipt must use your current assigned stock location."
            });
        }

        StockIntakeRules.Validate(request, opening);
        return Ok(await repository.PostAsync(request, opening, int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)));
    }
}
