using Microsoft.AspNetCore.Authorization;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductController : ControllerBase
{
    private readonly IProductService service;
    private readonly StockFlow.Web.Security.PermissionContext access;
    private readonly IVariantRepository variants;
    // ASP.NET supplies these services when it creates this class.
    public ProductController(IProductService service,
StockFlow.Web.Security.PermissionContext access,
IVariantRepository variants)
    {
        this.service = service;
        this.access = access;
        this.variants = variants;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(bool activeOnly = false, string? term = null, int? categoryId = null,
        bool includeDescendants = true, [FromServices] ICategoryService? categories = null)
    {
        if (categoryId is null) return Ok(await service.GetAllAsync(activeOnly, term));
        if (categoryId <= 0) return BadRequest(new { message = "Invalid category filter." });
        if (!access.Can("CATEGORIES", "VIEW")) return Forbid();
        if (categories is null || await categories.GetByIdAsync(categoryId.Value) is null) return NotFound();
        var ids = (await categories.GetAllAsync()).Where(c => c.CategoryId == categoryId.Value
            || includeDescendants && c.ParentCategoryId == categoryId.Value).Select(c => c.CategoryId).ToHashSet();
        return Ok((await service.GetAllAsync(activeOnly, term)).Where(p => ids.Contains(p.CategoryId)));
    }

    [HttpPost("identifier-reservations")]
    public async Task<IActionResult> ReserveIdentifiers(IdentifierReservation request,
        [FromServices] StockFlow.Infrastructure.Data.DbConnectionFactory factory)
    {
        if(!access.Can("PRODUCTS",request.ProductId.HasValue?"EDIT":"CREATE") || !access.Can("PRODUCT_VARIANTS","CREATE")) return Forbid();
        return Ok(await new StockFlow.Infrastructure.Repositories.CatalogIdentifierAllocator(factory).ReserveAsync(request));
    }

    [HttpPut("{id:int}/identifier-reservations")]
    public Task<IActionResult> ReserveAdditionalVariantIdentifiers(int id,IdentifierReservation request,
        [FromServices] StockFlow.Infrastructure.Data.DbConnectionFactory factory)
    {
        request.ProductId=id;
        return ReserveIdentifiers(request,factory);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (await service.GetByIdAsync(id) is { } entity)
        {
            return Ok(entity);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(string? term, bool activeOnly = true)
    {
        return Ok((await service.GetAllAsync(activeOnly, term)).Take(20));
    }

    // Read-only, paged SKU details; no pricing, stock or allocation side effects.
    [HttpGet("{id:int}/skus")]
    public async Task<IActionResult> Skus(int id, string? term, int offset,
        [FromServices] StockFlow.Infrastructure.Data.DbConnectionFactory factory)
    {
        if (!access.Can("PRODUCTS", "VIEW")) return Forbid();
        if (offset < 0) return BadRequest(new { message = "Invalid SKU page." });
        await using var connection = factory.CreateConnection();
        var rows = (await connection.QueryAsync("""
            SELECT v.variant_id AS "variantId",v.variant_name AS "variantName",
                v.sku AS "sku",v.is_active AS "isActive"
            FROM product_variants v JOIN products p USING(tenant_id,product_id)
            JOIN categories c USING(tenant_id,category_id)
            WHERE p.tenant_id=public.sf_tenant() AND p.product_id=@Id
                AND c.business_profile_code=public.sf_visible_business_profile()
                AND NULLIF(btrim(v.sku),'') IS NOT NULL
            ORDER BY CASE WHEN @Term IS NOT NULL AND v.sku ILIKE '%' || @Term || '%' THEN 0 ELSE 1 END,v.variant_id
            LIMIT 101 OFFSET @Offset
            """, new { Id = id, Term = string.IsNullOrWhiteSpace(term) ? null : term.Trim(), Offset = offset })).ToList();
        return Ok(new { items = rows.Take(100), hasMore = rows.Count > 100 });
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Create(CreateProductRequest request)
    {
        if (!await CanSaveVariants(request))
        {
            return Forbid();
        }

        var id = await service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new
        {
            id
        }, await service.GetByIdAsync(id));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, UpdateProductRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.ExpectedEditVersion))return Conflict(new {message="Reload the product before saving so concurrent changes can be checked."});
        if (!await CanSaveVariants(request, id))
        {
            return Forbid();
        }

        var existing = await service.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }
        if(request.RegistrationDefaults is not null&&!access.Can("PRODUCT_VARIANTS","EDIT")&&System.Text.Json.JsonSerializer.Serialize(request.RegistrationDefaults)!=System.Text.Json.JsonSerializer.Serialize(existing.RegistrationDefaults))return Forbid();
        if(request.RegistrationDefaults is not null&&request.RegistrationDefaults.PreferredSupplierId!=existing.RegistrationDefaults?.PreferredSupplierId&&!access.Can("SUPPLIERS","VIEW"))return Forbid();

        if (existing.IsActive != request.IsActive && !access.Can("PRODUCTS", "DELETE"))
        {
            return Forbid();
        }
        if (request.ProductStatus is not null && request.ProductStatus != existing.ProductStatus && !access.Can("PRODUCTS", "DELETE")) return Forbid();

        try
        {
            return await service.UpdateAsync(id, request) ? NoContent() : NotFound();
        }
        catch(System.Data.DBConcurrencyException error){return Conflict(new {message=error.Message});}
    }

    private async Task<bool> CanSaveVariants(CreateProductRequest request, int? productId = null)
    {
        if(productId is null&&request.RegistrationDefaults?.PreferredSupplierId is not null&&!access.Can("SUPPLIERS","VIEW"))return false;
        if (request.Variants is null)
        {
            return request.ParentProductCode is null || productId.HasValue || access.Can("PRODUCT_VARIANTS","CREATE") && access.Can("PRODUCT_VARIANTS","VIEW");
        }
        if (request.Variants.Any(v => v is null)) return false;
        if (request.Variants.Any(v => v.SupplierIdentifiers is not null) && !access.Can("SUPPLIERS", "VIEW")) return false;

        if (!access.Can("PRODUCT_VARIANTS") || request.Variants.Any(v => !access.Can("PRODUCT_VARIANTS", v.VariantId.HasValue ? "EDIT" : "CREATE")))
        {
            return false;
        }

        if (productId.HasValue && !request.HasVariants && request.Variants.Any(v => !v.VariantId.HasValue) && !access.Can("PRODUCT_VARIANTS", "EDIT"))
        {
            return false;
        }

        var existing = productId.HasValue ? (await variants.GetAsync(productId, null, false)).ToDictionary(v => v.VariantId) : [];
        if(!access.Can("BARCODES","GENERATE")&&request.Variants.Any(v=>v.BarcodeSource=="INTERNAL"&&(!existing.TryGetValue(v.VariantId??0,out var old)||old.BarcodeSource!="INTERNAL"||v.PrimaryBarcode!=old.PrimaryBarcode)))return false;
        return request.Variants.All(v => (existing.TryGetValue(v.VariantId ?? 0, out var old) ? old.IsActive == v.IsActive : true) || access.Can("PRODUCT_VARIANTS", "DELETE"));
    }

    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (await service.DeleteAsync(id))
        {
            return NoContent();
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode)
    {
        if (await service.GetByBarcodeAsync(barcode) is { } product)
        {
            return Ok(product);
        }
        else
        {
            return NotFound();
        }
    }

    [HttpGet("catalog-lookup")]
    public async Task<IActionResult> CatalogLookup([FromQuery] string barcode, [FromQuery] int? supplierId)
    {
        if (await service.LookupCatalogAsync(barcode, supplierId) is { } draft)
        {
            return Ok(draft);
        }
        else
        {
            return NotFound(new
            {
                message = "No supplier or manufacturer catalogue match was found. Verify and register this product manually."
            });
        }
    }

    [HttpGet("{id:int}/image")]
    public async Task<IActionResult> Image(int id)
    {
        if (await service.GetImageAsync(id) is { } file)
        {
            return File(file.Data, file.ContentType, file.FileName);
        }
        else
        {
            return NotFound();
        }
    }
    [HttpGet("{id:int}/images/{imageId:guid}")]
    public async Task<IActionResult> GalleryImage(int id, Guid imageId)
    {
        if(await service.GetGalleryImageAsync(id,imageId) is { } file)
            return File(file.Data,file.ContentType,file.FileName);
        return NotFound();
    }
}
