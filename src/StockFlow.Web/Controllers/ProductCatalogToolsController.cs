using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/products/catalog")]
public sealed class ProductCatalogToolsController(IProductService products, IVariantRepository variants,
    BusinessScope scope, PermissionContext access) : ControllerBase
{
    public sealed class ImportRequest
    {
        public long ExpectedProfileVersion { get; set; }
        [Required, MinLength(1), MaxLength(100)] public List<CreateProductRequest> Products { get; set; } = [];
        public bool Reviewed { get; set; }
    }

    private bool CanImport => access.Can("PRODUCTS", "CREATE") && access.Can("PRODUCT_VARIANTS", "VIEW") && access.Can("PRODUCT_VARIANTS", "CREATE");
    private void RequireSetup(ImportRequest request)
    {
        if (!scope.CatalogRegistrationEnhancementsAvailable) throw new ValidationException("Apply migration 052 before using catalog imports.");
        if (request.ExpectedProfileVersion != scope.BusinessProfileVersion) throw new ValidationException("Shop setup changed. Reload and preview the file again.");
        if (request.Products.Any(p => p.Variants is null || p.Variants.Count == 0 || p.Variants.Any(v => v.VariantId.HasValue)))
            throw new ValidationException("Imports create new products with new variant identities. Existing products are never overwritten.");
        if (request.Products.Any(p=>p.Variants!.Any(v=>v.SupplierIdentifiers is not null)) && !access.Can("SUPPLIERS","VIEW"))
            throw new ValidationException("Supplier view permission is required to import supplier-specific item codes.");
        var skus = request.Products.SelectMany(p => p.Variants!).Select(v => v.Sku.Trim()).ToArray();
        if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Length) throw new ValidationException("Duplicate SKU in this import file.");
        if (request.Products.Select(p => string.Join('|', p.CategoryId, p.BrandId,
            System.Text.RegularExpressions.Regex.Replace(p.ProductName.Trim(), @"\s+", " ").ToUpperInvariant(), p.Model?.Trim().ToUpperInvariant()))
            .Distinct().Count() != request.Products.Count) throw new ValidationException("Duplicate product groups in this file. Put their variants in one group.");
    }

    private async Task<List<object>> PreviewRows(ImportRequest request)
    {
        var rows = new List<object>();
        foreach (var (product, index) in request.Products.Select((product, index) => (product, index)))
        {
            try
            {
                await products.ValidateRegistrationAsync(product);
                rows.Add(new { index, product.ProductName, valid = true, message = "Ready to create" });
            }
            catch (ValidationException error) { rows.Add(new { index, product.ProductName, valid = false, message = error.Message }); }
        }
        return rows;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(ImportRequest request)
    {
        if (!CanImport) return Forbid();
        RequireSetup(request);
        return Ok(new { rows = await PreviewRows(request) });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit(ImportRequest request)
    {
        if (!CanImport) return Forbid();
        RequireSetup(request);
        if (!request.Reviewed) throw new ValidationException("Preview and review the import before confirming.");
        // Validate the complete file before any inserts. Each product and all its
        // variants then use the existing atomic registration transaction. A race or
        // interruption may leave completed products; return explicit per-product results.
        foreach (var product in request.Products) await products.ValidateRegistrationAsync(product);
        var results = new List<object>();
        foreach (var (product, index) in request.Products.Select((product, index) => (product, index)))
        {
            try { results.Add(new { index, product.ProductName, productId = await products.CreateAsync(product), saved = true, message = "Created" }); }
            catch (ValidationException error) { results.Add(new { index, product.ProductName, saved = false, message = error.Message }); break; }
            catch (System.Data.Common.DbException) { results.Add(new { index, product.ProductName, saved = false, message = "Database validation or connection failure. This product was not confirmed. Keep the completed rows, refresh, and reconcile existing SKUs before retrying the remaining products." }); break; }
        }
        return Ok(new { rows = results, requested = request.Products.Count });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!access.Can("PRODUCTS", "EXPORT") || !access.Can("PRODUCT_VARIANTS", "VIEW")) return Forbid();
        return Ok(new { products = await products.GetAllAsync(), variants = await variants.GetAsync(null, null, false) });
    }
}
