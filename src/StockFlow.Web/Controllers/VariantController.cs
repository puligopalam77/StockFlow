using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/products/variants")]
public class VariantController : ControllerBase
{
    private readonly StockFlow.Application.Common.BusinessScope scope;
    private readonly IVariantRepository repository;
    private readonly StockFlow.Web.Security.PermissionContext access;
    private readonly ICatalogSchemaRepository schemas;
    private readonly IProductRepository products;
    private readonly ICategoryRepository categories;
    // ASP.NET supplies these services when it creates this class.
    public VariantController(IVariantRepository repository, StockFlow.Web.Security.PermissionContext access, StockFlow.Application.Common.BusinessScope scope, ICatalogSchemaRepository schemas, IProductRepository products, ICategoryRepository categories)
    {
        this.repository = repository;
        this.access = access;
        this.scope = scope;
        this.schemas = schemas;
        this.products = products;
        this.categories = categories;
    }

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{id:long}/item-labels")]
    [HttpPost("{id:long}/item-labels/generate")]
    public async Task<IActionResult> ItemLabels(long id)
    {
        var generate = HttpContext.Request.Method == "POST";
        if (!access.Can("BARCODES", generate ? "GENERATE" : "VIEW")) return Forbid();
        if (scope.LocationId <= 0 || scope.Consolidated) return BadRequest(new { message = "Choose one stock location first." });
        return Ok(await repository.ItemLabelsAsync(id, generate, UserId));
    }

    [HttpPost("{id:long}/print")]
    [HttpPost("{id:long}/reprint")]
    public async Task<IActionResult> Labels(long id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ItemLabelPrintRequest? request = null)
    {
        var action = Request.Path.Value!.EndsWith("/reprint", StringComparison.OrdinalIgnoreCase) ? "REPRINT" : "PRINT";
        if (!access.Can("BARCODES", action)) return Forbid();
        if (scope.LocationId <= 0 || scope.Consolidated) return BadRequest(new { message = "Choose one stock location first." });
        if (request?.ExistingBarcode is { } existing)
        {
            if (request.LabelIds is not null || string.IsNullOrWhiteSpace(existing) || existing.Length > 150 || request.Copies is < 1 or > 500)
                return BadRequest(new { message = "Choose one saved barcode and 1 to 500 copies." });
            await repository.LogExistingBarcodePrintAsync(id, existing, request.Copies, action, UserId);
        }
        if (request?.LabelIds is { } ids)
        {
            if (ids.Length is < 1 or > 500 || ids.Distinct().Count() != ids.Length) return BadRequest(new { message = "Select 1 to 500 distinct item labels." });
            await repository.LogItemLabelPrintAsync(id, ids, action, UserId);
        }
        var item = (await repository.GetAsync(null, null, false)).SingleOrDefault(v => v.VariantId == id);
        if (item is null)
        {
            return NotFound();
        }
        else
        {
            return Ok(new
            {
                item.Barcode,
                item.ProductName,
                item.SellingPrice
            });
        }
    }

    public sealed class ItemLabelPrintRequest
    {
        public long[]? LabelIds { get; set; }
        public string? ExistingBarcode { get; set; }
        public int Copies { get; set; } = 1;
    }

    [HttpGet]
    public async Task<IActionResult> Get(int? productId, string? term, bool activeOnly = true, bool billing = false, bool receiving = false)
    {
        if (billing || receiving)
        {
            if (scope.LocationId <= 0 || scope.Consolidated) return BadRequest(new { message = "Choose one stock location first." });
            if (string.IsNullOrWhiteSpace(term)) return Ok(Array.Empty<VariantResponse>());
            if (term.Length > 200) return BadRequest(new { message = "Search using at most 200 characters." });
            return Ok(receiving ? await repository.SearchForReceiptAsync(term.Trim()) : await repository.SearchForSaleAsync(term.Trim()));
        }
        return Ok(await repository.GetAsync(productId, term, activeOnly));
    }

    [HttpPost]
    public async Task<IActionResult> Create(VariantRequest request)
    {
        if (scope.BusinessProfileCode != StockFlow.Application.Common.BusinessProfiles.Electrical && (request.IsSerialTracked || request.WarrantyMonths > 0))
            return BadRequest(new { message = "Serial tracking and warranty are not enabled for this shop profile." });
        await ValidateCategoryAsync(null, request);
        return Ok(new
        {
            variantId = await repository.SaveAsync(null, request, UserId)
        });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, VariantRequest request)
    {
        if (scope.BusinessProfileCode != StockFlow.Application.Common.BusinessProfiles.Electrical && (request.IsSerialTracked || request.WarrantyMonths > 0))
            return BadRequest(new { message = "Serial tracking and warranty are not enabled for this shop profile." });
        var existing = (await repository.GetAsync(request.ProductId, null, false)).SingleOrDefault(v => v.VariantId == id);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.IsActive != request.IsActive && !access.Can("PRODUCT_VARIANTS", "DELETE"))
        {
            return Forbid();
        }

        await ValidateCategoryAsync(id, request);

        return Ok(new
        {
            variantId = await repository.SaveAsync(id, request, UserId)
        });
    }

    private async Task ValidateCategoryAsync(long? id, VariantRequest request)
    {
        if (request.Attributes is null) throw new System.ComponentModel.DataAnnotations.ValidationException("Supply item attribute values.");
        var product = await products.GetByIdAsync(request.ProductId);
        if (product is null) throw new System.ComponentModel.DataAnnotations.ValidationException("Choose an existing product.");
        var category=await categories.GetByIdAsync(product.CategoryId);
        if(category?.RegistrationDefaultsJson is { } policyJson){
            var policy=System.Text.Json.JsonSerializer.Deserialize<StockFlow.Application.DTOs.Categories.CategoryInventoryDefaults>(policyJson,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if(policy?.AllowProductOverride==false&&request.IsSerialTracked!=(policy.TrackingMethod=="SERIAL"))throw new System.ComponentModel.DataAnnotations.ValidationException("This category requires its configured tracking method.");
            if(policy?.EnableWarranty==false&&request.WarrantyMonths>0)throw new System.ComponentModel.DataAnnotations.ValidationException("Warranty is not enabled for this category.");
        }
        var schema = await schemas.GetAsync();
        var rows = (await repository.GetAsync(request.ProductId, null, false)).ToList();
        var previous = rows.SingleOrDefault(v => v.VariantId == id);
        var rules = StockFlow.Application.Services.CategoryFieldInheritance.Fields(schema, product.CategoryId);
        if (previous is not null && rules.Where(f => f.VariantAxis).Any(f => previous.Attributes.TryGetValue(f.Code, out var old) && old != request.Attributes.GetValueOrDefault(f.Code)))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Saved variant options cannot change. Add a new variant instead.");
        StockFlow.Application.Services.CatalogSchemaValidator.ValidateValues(schema, product.CategoryId,
            rows.Where(v => v.VariantId != id).Select(v => v.Attributes).Append(request.Attributes).ToList());
    }

    [HttpPost("{id:long}/barcode")]
    public async Task<IActionResult> Barcode(long id)
    {
        if (!access.Can("BARCODES", "GENERATE"))
        {
            return Forbid();
        }

        return Ok(new
        {
            barcode = await repository.GenerateBarcodeAsync(id, UserId)
        });
    }

    [HttpPost("units/{unitId:long}/barcode")]
    public async Task<IActionResult> UnitBarcode(long unitId, UnitBarcodeRequest request)
    {
        if (!access.Can("BARCODES", "GENERATE"))
        {
            return Forbid();
        }

        return Ok(new
        {
            barcode = await repository.SaveUnitBarcodeAsync(unitId, request, UserId)
        });
    }
}
