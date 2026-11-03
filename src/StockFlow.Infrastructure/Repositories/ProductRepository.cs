using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;
using System.Text.Json;

namespace StockFlow.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    public async Task<bool> ParentCodeExistsAsync(string code,int? excludingId)
    {
        using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM products p WHERE upper(btrim(p.parent_product_code))=upper(btrim(@code)) AND (@excludingId IS NULL OR p.product_id<>@excludingId))",new{code,excludingId});
    }
    public async Task<int?> FindRegistrationAsync(Guid requestId,string requestHash)
    {
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='products' AND column_name='registration_request_id')"))throw new System.ComponentModel.DataAnnotations.ValidationException("Apply migration 066 before saving product registrations with request IDs.");
        var saved=await c.QuerySingleOrDefaultAsync<(int ProductId,string Hash)?>("SELECT product_id AS ProductId,registration_request_hash AS Hash FROM products p WHERE registration_request_id=@requestId AND EXISTS(SELECT 1 FROM categories c WHERE c.tenant_id=p.tenant_id AND c.category_id=p.category_id AND c.business_profile_code=sf_current_business_profile())",new{requestId});
        if(saved is null)return null;
        if(saved.Value.Hash!=requestHash)throw new System.ComponentModel.DataAnnotations.ValidationException("This registration request was already saved with different details. Open a new registration.");
        return saved.Value.ProductId;
    }
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public ProductRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<int> SaveRegistrationAsync(Product entity, List<StockFlow.Application.DTOs.Products.RegistrationVariantRequest> variants)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        if(entity.RegistrationRequestId is { } requestId)
        {
            await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('product-registration:'||@TenantId::text||':'||@RequestId::text,0))",new{TenantId=factory.Scope?.TenantId??0,RequestId=requestId},t);
            var saved=await c.QuerySingleOrDefaultAsync<(int ProductId,string Hash)?>("SELECT product_id AS ProductId,registration_request_hash AS Hash FROM products WHERE registration_request_id=@requestId",new{requestId},t);
            if(saved is { } previous){if(previous.Hash!=entity.RegistrationRequestHash)throw new System.ComponentModel.DataAnnotations.ValidationException("This request was already saved with different details.");return previous.ProductId;}
        }
        var isNew = entity.ProductId == 0;
        if(!isNew)await ProductEditIntegrity.CheckAsync(c,t,entity);
        if (isNew)
        {
            entity.ProductId = await c.ExecuteScalarAsync<int>("INSERT INTO products (parent_product_code,registration_request_id,registration_request_hash,product_name, category_id, brand_id, model, description, hsn_code, unit_of_measure, product_image_url,product_image_name,product_image_content_type,product_image_data, has_variants, warranty_provider, tax_percentage, warranty_months, is_serial_tracked, is_active) VALUES (@ParentProductCode,@RegistrationRequestId,@RegistrationRequestHash,@ProductName,@CategoryId,@BrandId,@Model,@Description,@HsnCode,@UnitOfMeasure,@ProductImageUrl,@ProductImageName,@ProductImageContentType,@ProductImageData,@HasVariants,@WarrantyProvider,@TaxPercentage,@WarrantyMonths,@IsSerialTracked,TRUE) RETURNING product_id",
entity,
t);
        }
        else
        {
            if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM products WHERE product_id=@ProductId)", entity, t))
            {
                throw new System.ComponentModel.DataAnnotations.ValidationException("Product not found.");
            }

            await c.ExecuteAsync("UPDATE products SET product_name=@ProductName,category_id=@CategoryId,brand_id=@BrandId,model=@Model,description=@Description,hsn_code=@HsnCode,unit_of_measure=@UnitOfMeasure,product_image_url=@ProductImageUrl,product_image_name=CASE WHEN @RemoveProductImage THEN NULL ELSE COALESCE(@ProductImageName,product_image_name) END,product_image_content_type=CASE WHEN @RemoveProductImage THEN NULL ELSE COALESCE(@ProductImageContentType,product_image_content_type) END,product_image_data=CASE WHEN @RemoveProductImage THEN NULL ELSE COALESCE(@ProductImageData,product_image_data) END,has_variants=@HasVariants,warranty_provider=@WarrantyProvider,tax_percentage=@TaxPercentage,warranty_months=@WarrantyMonths,is_serial_tracked=@IsSerialTracked,is_active=@IsActive,updated_at=CURRENT_TIMESTAMP WHERE product_id=@ProductId",
entity,
t);
        }

        if(entity.ParentProductCode is not null || entity.DetailedDescription is not null)
            await c.ExecuteAsync("UPDATE products SET parent_product_code=coalesce(@ParentProductCode,parent_product_code),detailed_description=@DetailedDescription WHERE product_id=@ProductId",entity,t);
        if(entity.RegistrationRequestId is not null)await c.ExecuteAsync("UPDATE products SET registration_request_id=@RegistrationRequestId,registration_request_hash=@RegistrationRequestHash WHERE product_id=@ProductId",entity,t);
        if(entity.RegistrationDefaultsJson is not null)await c.ExecuteAsync("UPDATE products SET registration_defaults=CAST(@RegistrationDefaultsJson AS jsonb) WHERE product_id=@ProductId",entity,t);
        var configured = (await c.QueryAsync<(long VariantId, bool IsDefault)>("SELECT variant_id AS VariantId,is_default AS IsDefault FROM product_variants WHERE product_id=@ProductId ORDER BY variant_id FOR UPDATE",
entity,
t)).ToList();
        if (!entity.HasVariants && configured.Any(v => !v.IsDefault))
        {
            throw new System.ComponentModel.DataAnnotations.ValidationException("This product has saved variants. Keep Has Variants enabled to preserve their identity and history.");
        }

        var repo = new VariantRepository(factory);
        var first = true;
        foreach (var variant in variants.OrderBy(v => v.VariantId ?? long.MaxValue))
        {
            variant.ProductId = entity.ProductId;
            var id = variant.VariantId;
            if ((!entity.HasVariants || isNew && first) && id is null)
            {
                id = configured.SingleOrDefault(v => v.IsDefault).VariantId is > 0 and var defaultId ? defaultId : null;
            }

            if (id.HasValue && !configured.Any(v => v.VariantId == id))
            {
                throw new System.ComponentModel.DataAnnotations.ValidationException("Variant does not belong to this product.");
            }

            await repo.SaveInTransactionAsync(c, t, id, variant, factory.Scope?.UserId ?? 1,initializing:isNew);
            first = false;
        }

        if (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true)
            await c.ExecuteAsync("UPDATE products SET product_status=@ProductStatus,is_active=(@ProductStatus='ACTIVE') WHERE product_id=@ProductId", entity, t);
        if(entity.RegistrationDefaultsJson is not null)await c.ExecuteAsync("UPDATE product_variants v SET purchase_price=CASE WHEN v.use_default_purchase_price THEN (p.registration_defaults->>'purchasePrice')::numeric ELSE v.purchase_price END,selling_price=CASE WHEN v.use_default_selling_price THEN (p.registration_defaults->>'sellingPrice')::numeric ELSE v.selling_price END FROM products p WHERE p.product_id=v.product_id AND p.product_id=@ProductId AND (v.use_default_purchase_price OR v.use_default_selling_price)",entity,t);
        if(entity.VehicleFitmentsJson is not null)await VehicleFitmentStorage.SaveAsync(c,t,entity.ProductId,null,JsonSerializer.Deserialize<List<StockFlow.Application.DTOs.Products.VehicleFitment>>(entity.VehicleFitmentsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await SaveImagesAsync(c,t,entity);
        if(entity.GstTaxRuleId is {} ruleId)await GstRepository.AssignRegistrationRule(c,t,entity.ProductId,ruleId);
        await t.CommitAsync();
        return entity.ProductId;
    }

    private const string SelectSql = "SELECT (to_jsonb(t)->'registration_defaults')::text AS RegistrationDefaultsJson,t.product_id AS ProductId,to_jsonb(t)->>'parent_product_code' AS ParentProductCode,to_jsonb(t)->>'detailed_description' AS DetailedDescription, COALESCE(to_jsonb(t)->>'product_status',CASE WHEN t.is_active THEN 'ACTIVE' ELSE 'INACTIVE' END) AS ProductStatus, t.product_name AS ProductName, t.category_id AS CategoryId, t.brand_id AS BrandId, t.model AS Model, t.description AS Description, t.hsn_code AS HsnCode, t.unit_of_measure AS UnitOfMeasure, t.product_image_url AS ProductImageUrl,t.product_image_name AS ProductImageName, (SELECT COALESCE(jsonb_agg(image - 'data'),'[]'::jsonb)::text FROM jsonb_array_elements(COALESCE(to_jsonb(t)->'product_images','[]'::jsonb)) image) AS ProductImagesJson, t.has_variants AS HasVariants, t.warranty_provider AS WarrantyProvider, t.sku AS Sku, t.barcode AS Barcode, t.purchase_price AS PurchasePrice, t.selling_price AS SellingPrice, t.tax_percentage AS TaxPercentage, t.warranty_months AS WarrantyMonths, t.minimum_stock_level AS MinimumStockLevel, t.is_serial_tracked AS IsSerialTracked, t.is_active AS IsActive, t.created_at AS CreatedAt, t.updated_at AS UpdatedAt, c.category_name AS CategoryName, b.brand_name AS BrandName FROM products t JOIN categories c ON c.category_id=t.category_id AND COALESCE(to_jsonb(c)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL') LEFT JOIN brands b ON b.brand_id=t.brand_id";
    public async Task<IEnumerable<Product>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        using var connection = factory.CreateConnection();
        var attributeSearch = factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? "sf_searchable_attributes(t.category_id,v.attributes)" : "v.attributes::text";
        var correctionSearch = await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.catalog_sku_corrections') IS NOT NULL")
            ? " OR EXISTS(SELECT 1 FROM public.catalog_sku_corrections a WHERE a.tenant_id=t.tenant_id AND a.product_id=t.product_id AND a.old_sku ILIKE '%' || @Term || '%')" : "";
        const string skuSummary = "SELECT list_stats.sku_count AS SkuCount,list_stats.default_sku AS DefaultVariantSku,CASE WHEN t.has_variants AND list_stats.record_count=0 THEN NULL WHEN t.has_variants THEN list_stats.record_count WHEN list_stats.nondefault_count=0 THEN 0 WHEN list_stats.default_count=0 THEN list_stats.nondefault_count ELSE NULL END AS ConfiguredVariantCount,list_preview.value AS SkuPreviewsJson,(@Term IS NOT NULL AND EXISTS(SELECT 1 FROM product_variants sv WHERE sv.tenant_id=t.tenant_id AND sv.product_id=t.product_id AND sv.sku ILIKE '%' || @Term || '%')) AS HasMatchingSku,";
        const string listJoins = """
             LEFT JOIN LATERAL (
                SELECT count(*)::integer AS record_count,
                    count(*) FILTER(WHERE NOT sv.is_default)::integer AS nondefault_count,
                    count(*) FILTER(WHERE sv.is_default)::integer AS default_count,
                    count(*) FILTER(WHERE NULLIF(btrim(sv.sku),'') IS NOT NULL)::integer AS sku_count,
                    max(sv.sku) FILTER(WHERE sv.is_default AND NULLIF(btrim(sv.sku),'') IS NOT NULL) AS default_sku
                FROM product_variants sv WHERE sv.tenant_id=t.tenant_id AND sv.product_id=t.product_id
             ) list_stats ON true
             LEFT JOIN LATERAL (
                SELECT COALESCE(jsonb_agg(jsonb_build_object('variantId',preview.variant_id,'sku',preview.sku,'variantName',preview.variant_name) ORDER BY preview.rank,preview.variant_id),'[]'::jsonb)::text AS value
                FROM (
                    SELECT pv.variant_id,pv.sku,pv.variant_name,
                        CASE WHEN @Term IS NOT NULL AND pv.sku ILIKE '%' || @Term || '%' THEN 0 ELSE 1 END AS rank
                    FROM product_variants pv WHERE pv.tenant_id=t.tenant_id AND pv.product_id=t.product_id AND NULLIF(btrim(pv.sku),'') IS NOT NULL
                    ORDER BY rank,pv.variant_id LIMIT 1
                ) preview
             ) list_preview ON true
            """;
        return await connection.QueryAsync<Product>(skuSummary + SelectSql[7..] + listJoins + " WHERE (NOT @ActiveOnly OR t.is_active) AND (@Term IS NULL OR t.product_name ILIKE '%' || @Term || '%' OR c.category_name ILIKE '%' || @Term || '%' OR b.brand_name ILIKE '%' || @Term || '%' OR t.model ILIKE '%' || @Term || '%' OR to_jsonb(t)->>'parent_product_code' ILIKE '%' || @Term || '%' OR t.hsn_code ILIKE '%' || @Term || '%' OR t.sku ILIKE '%' || @Term || '%' OR t.barcode ILIKE '%' || @Term || '%' OR EXISTS(SELECT 1 FROM variant_barcodes bx JOIN product_variants bv USING(tenant_id,variant_id) WHERE bv.tenant_id=t.tenant_id AND bv.product_id=t.product_id AND bx.is_active AND bx.barcode ILIKE '%' || @Term || '%') OR EXISTS(SELECT 1 FROM product_units u WHERE u.tenant_id=t.tenant_id AND u.product_id=t.product_id AND u.location_id=sf_location() AND (u.serial_number ILIKE '%' || @Term || '%' OR u.internal_barcode ILIKE '%' || @Term || '%' OR u.manufacturer_barcode ILIKE '%' || @Term || '%')) OR EXISTS(SELECT 1 FROM product_variants v WHERE v.tenant_id=t.tenant_id AND v.product_id=t.product_id AND (v.sku ILIKE '%' || @Term || '%' OR " + attributeSearch + " ILIKE '%' || @Term || '%'))" + correctionSearch + ") ORDER BY t.product_name, t.product_id;",
new
{
    ActiveOnly = activeOnly,
    Term = string.IsNullOrWhiteSpace(term) ? null : term.Trim()
});
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var snapshot=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var product=await connection.QuerySingleOrDefaultAsync<Product>(SelectSql + " WHERE t.product_id = @Id;", new
        {
            Id = id
        },snapshot);
        if(product is not null){
            product.EditVersion=await ProductEditIntegrity.VersionAsync(connection,snapshot,id);
            product.HasInventoryHistory=await ProductEditIntegrity.UsedAsync(connection,snapshot,id);
            if(product.HasInventoryHistory){
                var historyAvailable=await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_product_edit_history(integer,bigint)') IS NOT NULL",transaction:snapshot);
                product.HistoryVariantIds=(await connection.QueryAsync<long>("SELECT variant_id FROM product_variants WHERE product_id=@id"+(historyAvailable?" AND public.sf_product_edit_history(@id,variant_id)":""),new{id},snapshot)).ToArray();
            }
            product.CategoryChangeLocked=await connection.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_product_category_edit_locked(integer)') IS NOT NULL",transaction:snapshot)
                ? await connection.ExecuteScalarAsync<bool>("SELECT public.sf_product_category_edit_locked(@id)",new{id},snapshot)
                : product.ParentProductCode is not null;
            product.VehicleFitmentsJson=JsonSerializer.Serialize((await VehicleFitmentStorage.LoadAsync(connection,[id])).Where(r=>r.VariantId is null).Select(r=>r.Fitment),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        await snapshot.CommitAsync();
        return product;
    }

    public async Task<int> CreateAsync(Product entity)
    {
        if(entity.ProductImagesJson is not null)
            return await SaveRegistrationAsync(entity,[new(){Sku=entity.Sku??"",PrimaryBarcode=entity.Barcode,BarcodeSource=string.IsNullOrWhiteSpace(entity.Barcode)?"INTERNAL":"MANUFACTURER",VariantName="DEFAULT",PurchasePrice=entity.PurchasePrice,SellingPrice=entity.SellingPrice,TaxPercentage=entity.TaxPercentage,IsSerialTracked=entity.IsSerialTracked,WarrantyMonths=entity.WarrantyMonths,MinimumStockLevel=entity.MinimumStockLevel}]);
        using var connection = factory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>("INSERT INTO products (product_name, category_id, brand_id, model, description, hsn_code, unit_of_measure, product_image_url,product_image_name,product_image_content_type,product_image_data, has_variants, warranty_provider, sku, barcode, purchase_price, selling_price, tax_percentage, warranty_months, minimum_stock_level, is_serial_tracked, is_active) VALUES (@ProductName, @CategoryId, @BrandId, @Model, @Description, @HsnCode, @UnitOfMeasure, @ProductImageUrl,@ProductImageName,@ProductImageContentType,@ProductImageData, @HasVariants, @WarrantyProvider, @Sku, @Barcode, @PurchasePrice, @SellingPrice, @TaxPercentage, @WarrantyMonths, @MinimumStockLevel, @IsSerialTracked, TRUE) RETURNING product_id;",
entity);
    }

    public async Task<bool> UpdateAsync(Product entity)
    {
        if(entity.ProductImagesJson is not null)
        {
            await SaveRegistrationAsync(entity,[]);
            return true;
        }
        using var connection = factory.CreateConnection();
        // Do not write commercial mirrors during metadata edits; another user
        // may have updated a variant since the registration form was opened.
        var commercial = entity.IsRegistrationEdit ? "" : "sku=@Sku,barcode=@Barcode,purchase_price=@PurchasePrice,selling_price=@SellingPrice,minimum_stock_level=@MinimumStockLevel,";
        return await connection.ExecuteAsync("UPDATE products SET " + (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? "product_status=@ProductStatus," : "") + commercial + "product_name=@ProductName,category_id=@CategoryId,brand_id=@BrandId,model=@Model,description=@Description,hsn_code=@HsnCode,unit_of_measure=@UnitOfMeasure,product_image_url=@ProductImageUrl,product_image_name=CASE WHEN @RemoveProductImage THEN NULL ELSE COALESCE(@ProductImageName,product_image_name) END,product_image_content_type=CASE WHEN @RemoveProductImage THEN NULL ELSE COALESCE(@ProductImageContentType,product_image_content_type) END,product_image_data=CASE WHEN @RemoveProductImage THEN NULL ELSE COALESCE(@ProductImageData,product_image_data) END,has_variants=@HasVariants,warranty_provider=@WarrantyProvider,tax_percentage=@TaxPercentage,warranty_months=@WarrantyMonths,is_serial_tracked=@IsSerialTracked,is_active=@IsActive,updated_at=CURRENT_TIMESTAMP WHERE product_id=@ProductId;",
entity) > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteAsync("UPDATE products SET is_active = FALSE, updated_at = CURRENT_TIMESTAMP WHERE product_id = @Id;", new
        {
            Id = id
        }) > 0;
    }
    private async Task SaveImagesAsync(System.Data.IDbConnection connection,System.Data.IDbTransaction transaction,Product entity)
    {
        if(entity.ProductImagesJson is null)return;
        if(factory.Scope?.ProductImageGalleryAvailable!=true)throw new System.ComponentModel.DataAnnotations.ValidationException("Apply migration 053 before saving multiple product images.");
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var currentJson=await connection.ExecuteScalarAsync<string>("SELECT product_images::text FROM products WHERE product_id=@ProductId FOR UPDATE",entity,transaction);
        var current=JsonSerializer.Deserialize<List<StockFlow.Application.DTOs.Products.ProductGalleryImage>>(currentJson??"[]",options)??[];
        var requested=JsonSerializer.Deserialize<List<StockFlow.Application.DTOs.Products.ProductGalleryImage>>(entity.ProductImagesJson,options)??[];
        var result=new List<StockFlow.Application.DTOs.Products.ProductGalleryImage>();
        foreach(var image in requested)
        {
            if(image.Data is not null){result.Add(image);continue;}
            if(image.IsLegacy)
            {
                var legacy=await connection.QuerySingleOrDefaultAsync<StockFlow.Application.DTOs.Products.ProductImage>("SELECT product_image_name AS FileName,product_image_content_type AS ContentType,product_image_data AS Data FROM products WHERE product_id=@ProductId AND product_image_data IS NOT NULL",entity,transaction);
                if(legacy is null || current.Count>0)throw new System.ComponentModel.DataAnnotations.ValidationException("The original product image changed. Reopen the product.");
                result.Add(new(){ImageId=Guid.NewGuid(),FileName=legacy.FileName,ContentType=legacy.ContentType,Data=legacy.Data,VariantName=image.VariantName});
            }
            else {var stored=current.SingleOrDefault(saved=>saved.ImageId==image.ImageId)??throw new System.ComponentModel.DataAnnotations.ValidationException("Saved image does not belong to this product. Reopen the product.");stored.VariantName=image.VariantName;result.Add(stored);}
        }
        var primary=result.FirstOrDefault();
        await connection.ExecuteAsync("UPDATE products SET product_images=CAST(@Images AS jsonb),product_image_name=@FileName,product_image_content_type=@ContentType,product_image_data=@Data WHERE product_id=@ProductId",new{entity.ProductId,Images=JsonSerializer.Serialize(result,options),primary?.FileName,primary?.ContentType,primary?.Data},transaction);
    }

    public async Task<StockFlow.Application.DTOs.Products.ProductImage?> GetGalleryImageAsync(int productId,Guid imageId)
    {
        if(factory.Scope?.ProductImageGalleryAvailable!=true)return null;
        using var connection=factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<StockFlow.Application.DTOs.Products.ProductImage>("SELECT image->>'fileName' AS FileName,image->>'contentType' AS ContentType,decode(image->>'data','base64') AS Data FROM products p CROSS JOIN LATERAL jsonb_array_elements(p.product_images) image WHERE p.product_id=@productId AND image->>'imageId'=@imageId",new{productId,imageId=imageId.ToString()});
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Product>(SelectSql + " WHERE t.barcode = @Barcode AND t.is_active;", new
        {
            Barcode = barcode
        });
    }

    public Task<bool> SkuExistsAsync(string sku, int? excludingId = null)
    {
        return ExistsAsync("t.sku = @Value", sku, excludingId);
    }

    public Task<bool> BarcodeExistsAsync(string barcode, int? excludingId = null)
    {
        return ExistsAsync("t.barcode = @Value", barcode, excludingId);
    }

    private async Task<bool> ExistsAsync(string predicate, string value, int? excludingId)
    {
        using var connection = factory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>($"SELECT EXISTS(SELECT 1 FROM products t WHERE {predicate} AND (@ExcludingId IS NULL OR t.product_id <> @ExcludingId));",
new
{
    Value = value,
    ExcludingId = excludingId
});
    }

    public async Task<StockFlow.Application.DTOs.Products.CatalogProductDraft?> LookupCatalogAsync(string barcode, int? supplierId)
    {
        using var connection = factory.CreateConnection();
        var result = await connection.QuerySingleOrDefaultAsync<StockFlow.Application.DTOs.Products.CatalogProductDraft>("""
            SELECT barcode AS Barcode,source_name AS SourceName,supplier_id AS SupplierId,product_name AS ProductName,
                category_id AS CategoryId,brand_id AS BrandId,model AS Model,hsn_code AS HsnCode,unit_of_measure AS UnitOfMeasure,
                tracking_type AS TrackingType,purchase_price AS PurchasePrice,selling_price AS SellingPrice,mrp AS Mrp,
                tax_percentage AS TaxPercentage,warranty_months AS WarrantyMonths,warranty_provider AS WarrantyProvider,
                supplier_product_code AS SupplierProductCode,attributes::text AS AttributesJson
            FROM supplier_product_catalog
            WHERE is_active AND lower(btrim(barcode))=lower(btrim(@barcode))
              AND EXISTS(SELECT 1 FROM categories cc WHERE cc.category_id=supplier_product_catalog.category_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))
              AND (supplier_id=@supplierId OR supplier_id IS NULL)
            ORDER BY (supplier_id=@supplierId) DESC,catalog_product_id LIMIT 1
            """,
new
{
    barcode,
    supplierId
});
        if (result is not null)
        {
            result.Attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(result.AttributesJson) ?? [];
        }

        return result;
    }

    public async Task<StockFlow.Application.DTOs.Products.ProductImage?> GetImageAsync(int id)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<StockFlow.Application.DTOs.Products.ProductImage>("SELECT product_image_name AS FileName,product_image_content_type AS ContentType,product_image_data AS Data FROM products WHERE product_id=@id AND product_image_data IS NOT NULL",
new
{
    id
});
    }
}
