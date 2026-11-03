using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class VariantRepository : IVariantRepository
{
    public async Task LogExistingBarcodePrintAsync(long variantId, string barcode, int copies, string action, int userId)
    {
        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        if (!await c.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(SELECT 1 FROM variant_barcodes b JOIN product_variants v USING(tenant_id,variant_id)
                WHERE v.variant_id=@variantId AND NOT v.is_serial_tracked AND b.is_active
                AND b.barcode_source='MANUFACTURER' AND b.barcode=@barcode)
            """, new { variantId, barcode }))
            throw new ValidationException("This manufacturer barcode is no longer assigned to the selected size / model.");
        await c.ExecuteAsync("""
            INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values)
            VALUES(@userId,'Barcodes',@action,'variant_barcodes',@variantId,CAST(@details AS jsonb))
            """, new { userId, action, variantId, details = JsonSerializer.Serialize(new { barcode, copies }) });
    }
    public async Task<IReadOnlyList<StockFlow.Application.DTOs.Purchases.StockLabelResponse>> ItemLabelsAsync(long variantId, bool generate, int userId)
    {
        factory.RequireLocation();
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        var locationId = factory.Scope!.LocationId;
        if (generate) await StockItemLabels.Generate(c, locationId, variantId, null, userId);
        if (!generate && !await StockItemLabels.Available(c)) return [];
        return await StockItemLabels.Read(c, locationId, variantId, null);
    }

    public async Task LogItemLabelPrintAsync(long variantId, long[] labelIds, string action, int userId)
    {
        var labels = await ItemLabelsAsync(variantId, false, userId);
        if (labelIds.Any(id => !labels.Any(label => label.LabelId == id)))
            throw new ValidationException("A selected label does not belong to this variant and stock location.");
        await using var c = factory.CreateConnection();
        await c.ExecuteAsync("""
            INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values)
            VALUES(@userId,'Barcodes',@action,'stock_item_labels',@variantId,CAST(@details AS jsonb))
            """, new { userId, action, variantId, details = JsonSerializer.Serialize(new { labelIds }) });
    }
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public VariantRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<IEnumerable<VariantResponse>> GetAsync(int? productId, string? term, bool activeOnly)
        => await LoadAsync(productId,term,activeOnly);

    public async Task<IEnumerable<VariantResponse>> FindIdentityConflictsAsync(string[] skus,string[] barcodes)
    {
        using var connection=factory.CreateConnection();
        var ids=(await connection.QueryAsync<long>("SELECT v.variant_id FROM product_variants v WHERE lower(btrim(v.sku))=ANY(@Skus) OR EXISTS(SELECT 1 FROM variant_barcodes b WHERE b.tenant_id=v.tenant_id AND b.variant_id=v.variant_id AND lower(btrim(b.barcode))=ANY(@Codes))",new{Skus=skus.Select(s=>s.Trim().ToLowerInvariant()).ToArray(),Codes=barcodes.Select(s=>s.Trim().ToLowerInvariant()).ToArray()})).ToArray();
        return ids.Length==0?[]:await LoadAsync(null,null,false,ids);
    }

    public Task<IEnumerable<VariantResponse>> SearchForSaleAsync(string term) => LoadAsync(null,term,false,limit:30);
    public Task<IEnumerable<VariantResponse>> SearchForReceiptAsync(string term) => LoadAsync(null,term,true,limit:30);

    private async Task<IEnumerable<VariantResponse>> LoadAsync(int? productId,string? term,bool activeOnly,long[]? filterIds=null,int? limit=null)
    {
        using var c = factory.CreateConnection();
        var hasFitments=await VehicleFitmentStorage.AvailableAsync(c);
        var legacySkuSearch=await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.catalog_sku_corrections') IS NOT NULL")
            ? " OR EXISTS(SELECT 1 FROM public.catalog_sku_corrections a WHERE a.tenant_id=v.tenant_id AND a.variant_id=v.variant_id AND a.old_sku ILIKE @Term) " : "";
        var vehicleSearch=hasFitments?" OR EXISTS(SELECT 1 FROM product_vehicle_fitments f JOIN vehicle_models m USING(tenant_id,vehicle_id) WHERE f.product_id=p.product_id AND (f.variant_id IS NULL OR f.variant_id=v.variant_id) AND concat_ws(' ',m.manufacturer,m.model,m.variant,f.notes) ILIKE @Term) ":"";
        var rows = (await c.QueryAsync<VariantResponse>("""
            SELECT to_jsonb(p)->>'parent_product_code' AS ParentProductCode,(to_jsonb(p)->'registration_defaults'->>'preferredSupplierId')::integer AS PreferredSupplierId,
                (SELECT category_name FROM categories WHERE tenant_id=p.tenant_id AND category_id=p.category_id) AS CategoryName,
                (SELECT parent.category_name FROM categories cat JOIN categories parent ON parent.tenant_id=cat.tenant_id AND parent.category_id=cat.parent_category_id WHERE cat.tenant_id=p.tenant_id AND cat.category_id=p.category_id) AS ParentCategoryName,
                (SELECT brand_name FROM brands WHERE tenant_id=p.tenant_id AND brand_id=p.brand_id) AS BrandName,
                v.variant_id AS VariantId,v.product_id AS ProductId,
                p.product_name || ' / ' || v.variant_name AS ProductName,p.model AS Model,v.variant_name AS VariantName,
                p.unit_of_measure AS UnitOfMeasure,
                p.product_image_url AS ProductImageUrl,(p.product_image_data IS NOT NULL) AS HasProductImage,
                COALESCE((to_jsonb(v)->>'use_default_purchase_price')::boolean,false) AS UseDefaultPurchasePrice,COALESCE((to_jsonb(v)->>'use_default_selling_price')::boolean,false) AS UseDefaultSellingPrice,v.sku AS Sku,v.is_default AS IsDefault,v.is_serial_tracked AS IsSerialTracked,
                COALESCE((SELECT (to_jsonb(cc)->'registration_defaults'->>'suggestBarcodeGeneration')::boolean FROM categories cc WHERE cc.tenant_id=p.tenant_id AND cc.category_id=p.category_id),true) AS SuggestBarcodeGeneration,
                v.purchase_price AS PurchasePrice,v.selling_price AS SellingPrice,v.tax_percentage AS TaxPercentage,
                v.mrp AS Mrp,v.pack_quantity AS PackQuantity,v.supplier_product_code AS SupplierProductCode,
                v.warranty_months AS WarrantyMonths,v.minimum_stock_level AS MinimumStockLevel,
                (v.is_active AND p.is_active) AS IsActive,v.attributes::text AS AttributesJson,s.current_stock AS CurrentStock,
                (SELECT barcode FROM variant_barcodes WHERE variant_id=v.variant_id AND is_active ORDER BY is_primary DESC,variant_barcode_id LIMIT 1) AS Barcode,
                (SELECT barcode_source FROM variant_barcodes WHERE variant_id=v.variant_id AND is_active ORDER BY is_primary DESC,variant_barcode_id LIMIT 1) AS BarcodeSource,
                (SELECT barcode FROM variant_barcodes WHERE variant_id=v.variant_id AND is_active AND barcode_source='MANUFACTURER' ORDER BY is_primary DESC,variant_barcode_id LIMIT 1) AS ManufacturerBarcode,
                (SELECT minimum_stock_level FROM variant_location_settings WHERE variant_id=v.variant_id AND location_id=@LocationId) AS LocationMinimumStockLevel
            FROM product_variants v JOIN products p USING(product_id) JOIN vw_variant_current_stock s USING(variant_id)
            WHERE EXISTS(SELECT 1 FROM categories cc WHERE cc.category_id=p.category_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))
              AND (CAST(@ProductId AS integer) IS NULL OR v.product_id=@ProductId)
              AND (CAST(@FilterIds AS bigint[]) IS NULL OR v.variant_id=ANY(@FilterIds))
              AND (NOT @ActiveOnly OR (v.is_active AND p.is_active))
              AND (@Term IS NULL OR p.product_name ILIKE @Term OR p.model ILIKE @Term OR to_jsonb(p)->>'parent_product_code' ILIKE @Term OR EXISTS(SELECT 1 FROM brands b WHERE b.tenant_id=p.tenant_id AND b.brand_id=p.brand_id AND b.brand_name ILIKE @Term) OR EXISTS(SELECT 1 FROM categories cat LEFT JOIN categories par ON par.tenant_id=cat.tenant_id AND par.category_id=cat.parent_category_id WHERE cat.tenant_id=p.tenant_id AND cat.category_id=p.category_id AND (cat.category_name ILIKE @Term OR par.category_name ILIKE @Term)) OR p.hsn_code ILIKE @Term OR v.variant_name ILIKE @Term OR v.sku ILIKE @Term OR v.supplier_product_code ILIKE @Term OR 
            """ + (factory.Scope?.CatalogRegistrationEnhancementsAvailable == true ? "concat_ws(' ',sf_searchable_attributes(p.category_id,v.attributes),(SELECT string_agg(i.supplier_sku,' ') FROM variant_supplier_identifiers i WHERE i.variant_id=v.variant_id))" : "v.attributes::text") + """
             ILIKE @Term OR EXISTS(SELECT 1 FROM variant_barcodes bx WHERE bx.variant_id=v.variant_id AND bx.is_active AND bx.barcode ILIKE @Term))
            ORDER BY p.product_name,v.variant_id LIMIT @Limit
            """.Replace("OR EXISTS(SELECT 1 FROM variant_barcodes bx",vehicleSearch+legacySkuSearch+" OR EXISTS(SELECT 1 FROM variant_barcodes bx"),
new
{
    ProductId = productId,
    FilterIds=filterIds,
    Term = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim() + "%",
    ActiveOnly = activeOnly,
    Limit = limit,
    LocationId = factory.Scope?.LocationId ?? 0
})).ToList();
        var codes = (await c.QueryAsync<(long VariantId, string Barcode)>("SELECT variant_id,barcode FROM variant_barcodes WHERE is_active AND variant_id=ANY(@Ids) ORDER BY is_primary DESC,variant_barcode_id",new{Ids=rows.Select(r=>r.VariantId).ToArray()})).ToLookup(x => x.VariantId, x => x.Barcode);
        var supplierCodes = factory.Scope?.CatalogRegistrationEnhancementsAvailable == true
            ? (await c.QueryAsync<(long VariantId,int SupplierId,string SupplierSku,string SupplierName,string? Details)>("SELECT i.variant_id VariantId,i.supplier_id SupplierId,i.supplier_sku SupplierSku,s.supplier_name SupplierName,(to_jsonb(i)->'purchasing_details')::text Details FROM variant_supplier_identifiers i JOIN suppliers s ON s.tenant_id=i.tenant_id AND s.supplier_id=i.supplier_id WHERE i.variant_id=ANY(@Ids)",new { Ids=rows.Select(r=>r.VariantId).ToArray() })).ToLookup(i=>i.VariantId)
            : null;
        foreach (var row in rows)
        {
            row.PrimaryBarcode = row.Barcode;
            row.Attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(row.AttributesJson)!;
            row.Barcodes = codes[row.VariantId].ToList();
            row.SupplierIdentifiers = supplierCodes is null ? null : supplierCodes[row.VariantId].Select(i=>WithSupplierDetails(i.SupplierId,i.SupplierSku,i.SupplierName,i.Details)).ToList();
        }

        var fitments=await VehicleFitmentStorage.LoadAsync(c,rows.Select(r=>r.ProductId).Distinct().ToArray());
        var fitmentsByVariant=fitments.Where(f=>f.VariantId.HasValue).ToLookup(f=>f.VariantId!.Value,f=>f.Fitment);
        foreach(var row in rows)row.VehicleFitments=fitmentsByVariant[row.VariantId].ToList();

        return rows;
    }

    public async Task<long> SaveAsync(long? id, VariantRequest r, int userId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var result = await SaveInTransactionAsync(c, t, id, r, userId);
        await t.CommitAsync();
        return result;
    }

    internal async Task<long> SaveInTransactionAsync(NpgsqlConnection c, NpgsqlTransaction t, long? id, VariantRequest r, int userId,bool initializing=false)
    {
        r.VariantName = r.VariantName?.Trim() ?? "";
        r.Sku = id.HasValue ? r.Sku??"" : r.Sku?.Trim().ToUpperInvariant()??"";
        if (!id.HasValue && r.Sku.Length > 40) throw new ValidationException("New variant SKUs must be at most 40 characters.");
        Validator.ValidateObject(r, new ValidationContext(r), true);
        if (new[]
        {
            r.PurchasePrice,
            r.SellingPrice,
            r.TaxPercentage
        }.Any(value => decimal.Round(value, 2) != value))
        {
            throw new ValidationException("Prices and tax support at most two decimal places.");
        }

        if (r.BarcodeSource == "MANUFACTURER" && string.IsNullOrWhiteSpace(r.PrimaryBarcode))
        {
            throw new ValidationException("Enter the primary manufacturer barcode.");
        }

        if (r.ApplyLocationMinimumStock)
        {
            factory.RequireLocation(r.LocationId);
        }

        if (r.VariantName.Length == 0 || r.Sku.Length == 0)
        {
            throw new ValidationException("Variant name and SKU are required.");
        }

        if (r.Attributes is null || r.Attributes.Count > 30 || r.Attributes.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Key.Length > 100 || x.Value.Length > 1000) || r.Attributes.Keys.Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Attributes.Count)
        {
            throw new ValidationException("Use distinct attribute names and one value per attribute (100 characters maximum).");
        }
        if (r.Attributes.Any(a => a.Value.Length > 100))
        {
            var listCodes = factory.Scope?.CatalogRegistrationEnhancementsAvailable == true
                ? (await c.QueryAsync<string>("SELECT f->'attribute'->>'code' FROM products p JOIN catalog_attribute_schemas s ON s.tenant_id=p.tenant_id AND s.business_profile_code=sf_current_business_profile() CROSS JOIN LATERAL jsonb_array_elements(sf_category_attribute_config(s.definition,p.category_id)) f WHERE p.product_id=@ProductId AND f->'attribute'->>'type'='TEXT_LIST'", r, t)).ToHashSet()
                : [];
            if (r.Attributes.Any(a => a.Value.Length > 100 && !listCodes.Contains(a.Key)))
                throw new ValidationException("Only configured multiple-value fields support more than 100 characters.");
        }

        if (r.Barcodes is null || r.Barcodes.Count > 20 || r.Barcodes.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 150))
        {
            throw new ValidationException("Enter at most 20 valid barcodes.");
        }

        // Keep the same lock order as every stock writer: product, variant, physical unit.
        var product = await c.QuerySingleOrDefaultAsync<StockFlow.Domain.Entities.Product>("SELECT is_active AS IsActive,has_variants AS HasVariants,is_serial_tracked AS IsSerialTracked FROM products WHERE product_id=@ProductId FOR UPDATE",
r,
t);
        if (product?.IsActive != true)
        {
            throw new ValidationException("Choose an active product.");
        }

        if (!id.HasValue && !product.HasVariants && await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_variants WHERE product_id=@ProductId)", r, t))
        {
            throw new ValidationException("Edit the DEFAULT variant, or enable Has Variants on the product before adding variations.");
        }

        if (!id.HasValue && r.IsSerialTracked != product.IsSerialTracked)
        {
            throw new ValidationException("Tracking must match the product. Change Tracking Type in Product Registration before receiving stock.");
        }

        Dictionary<string,string>? previousAttributes=null;
        if (id.HasValue)
        {
            var old = await c.QuerySingleOrDefaultAsync<VariantResponse>("SELECT attributes::text AS AttributesJson,is_active AS IsActive,sku AS Sku,pack_quantity AS PackQuantity,is_serial_tracked AS IsSerialTracked FROM product_variants WHERE variant_id=@id AND product_id=@ProductId FOR UPDATE",
new
{
    id,
    r.ProductId
},
t) ?? throw new ValidationException("Variant not found for this product.");
            previousAttributes=JsonSerializer.Deserialize<Dictionary<string,string>>(old.AttributesJson);
            if(old.Sku!=r.Sku&&!initializing)throw new ValidationException("Saved SKUs are permanent. Add a new variant for a different stock identity.");
            if(old.IsActive&&!r.IsActive&&!await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_product_edit_history(integer,bigint)') IS NOT NULL",transaction:t))
                throw new ValidationException("Apply migration 069 before deactivating variants so stock at every location can be checked.");
            if(old.PackQuantity!=r.PackQuantity && await ProductEditIntegrity.UsedAsync(c,t,r.ProductId,id))
                throw new ValidationException("Pack conversion cannot change after a SKU has stock or transaction history. Use an authorized inventory correction.");
            if (old.IsSerialTracked != r.IsSerialTracked && r.IsSerialTracked != product.IsSerialTracked)
            {
                throw new ValidationException("Tracking must match the product default.");
            }

            if (old.IsSerialTracked != r.IsSerialTracked && await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE variant_id=@id) OR EXISTS(SELECT 1 FROM product_units WHERE variant_id=@id) OR EXISTS(SELECT 1 FROM purchase_items WHERE variant_id=@id)",
new
{
    id
},
t))
            {
                throw new ValidationException("Tracking cannot change after a variant has been used. Create a new variant.");
            }
        }

        r.SupplierProductCode = string.IsNullOrWhiteSpace(r.SupplierProductCode) ? null : r.SupplierProductCode.Trim();
        if (r.Mrp > 0 && r.SellingPrice > r.Mrp)
        {
            throw new ValidationException("Selling price cannot exceed MRP.");
        }

        var parameters = new
        {
            id,
            r.ProductId,
            r.VariantName,
            r.Sku,
            r.IsSerialTracked,
            r.PurchasePrice,
            r.SellingPrice,
            r.Mrp,
            r.PackQuantity,
            r.SupplierProductCode,
            r.TaxPercentage,
            r.WarrantyMonths,
            r.MinimumStockLevel,
            r.IsActive,
            Attributes = JsonSerializer.Serialize(r.Attributes.ToDictionary(x => previousAttributes?.ContainsKey(x.Key)==true?x.Key:x.Key.Trim(), x => previousAttributes is not null&&previousAttributes.TryGetValue(x.Key,out var original)&&original==x.Value?x.Value:x.Value.Trim()))
        };
        var variantId = id ?? await c.ExecuteScalarAsync<long>("""
            INSERT INTO product_variants(product_id,variant_name,sku,is_serial_tracked,purchase_price,selling_price,mrp,pack_quantity,supplier_product_code,tax_percentage,warranty_months,minimum_stock_level,is_active,attributes)
            VALUES(@ProductId,@VariantName,@Sku,@IsSerialTracked,@PurchasePrice,@SellingPrice,@Mrp,@PackQuantity,@SupplierProductCode,@TaxPercentage,@WarrantyMonths,@MinimumStockLevel,@IsActive,CAST(@Attributes AS jsonb)) RETURNING variant_id
            """,
parameters,
t);
        if (id.HasValue)
        {
            await c.ExecuteAsync("""
            UPDATE product_variants SET variant_name=@VariantName,sku=@Sku,is_serial_tracked=@IsSerialTracked,purchase_price=@PurchasePrice,selling_price=@SellingPrice,mrp=@Mrp,pack_quantity=@PackQuantity,supplier_product_code=@SupplierProductCode,tax_percentage=@TaxPercentage,warranty_months=@WarrantyMonths,minimum_stock_level=@MinimumStockLevel,is_active=@IsActive,attributes=CAST(@Attributes AS jsonb) WHERE variant_id=@id
            """,
parameters,
t);
        }

        await c.ExecuteAsync("""
            UPDATE products p SET sku=v.sku,purchase_price=v.purchase_price,
                selling_price=v.selling_price,minimum_stock_level=v.minimum_stock_level
            FROM product_variants v WHERE v.variant_id=@variantId AND v.is_default AND p.product_id=v.product_id
            """,
new
{
    variantId
},
t);
        foreach (var code in r.Barcodes.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM variant_barcodes WHERE lower(btrim(barcode))=lower(@code) AND variant_id<>@variantId) OR EXISTS(SELECT 1 FROM product_units WHERE lower(btrim(serial_number))=lower(@code) OR lower(btrim(barcode))=lower(@code) OR lower(btrim(internal_barcode))=lower(@code) OR lower(btrim(manufacturer_barcode))=lower(@code))",
new
{
    code,
    variantId
},
t))
            {
                throw new ValidationException("Barcode conflicts with an existing variant or physical unit.");
            }

            // A BEFORE INSERT scan guard runs before ON CONFLICT. Do not attempt
            // to reinsert this variant's saved aliases during metadata-only edits.
            await c.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source) SELECT @variantId,@code,'MANUFACTURER' WHERE NOT EXISTS(SELECT 1 FROM variant_barcodes WHERE variant_id=@variantId AND lower(btrim(barcode))=lower(@code)) ON CONFLICT DO NOTHING",
new
{
    variantId,
    code
},
t);
        }

        if (r.BarcodeSource is not null)
        {
            var code = r.BarcodeSource == "INTERNAL" ? await c.QueryFirstOrDefaultAsync<string>("SELECT barcode FROM variant_barcodes WHERE variant_id=@variantId AND barcode_source='INTERNAL' ORDER BY variant_barcode_id LIMIT 1",
new
{
    variantId
},
t) ?? "SFV" + variantId.ToString("D8") : r.PrimaryBarcode!.Trim();
            var owner = await c.QuerySingleOrDefaultAsync<long?>("SELECT variant_id FROM variant_barcodes WHERE lower(btrim(barcode))=lower(@code)", new
            {
                code
            }, t);
            if (owner.HasValue && owner != variantId)
            {
                throw new ValidationException("Barcode belongs to another variant.");
            }

            if (owner.HasValue && r.BarcodeSource == "MANUFACTURER" && await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM variant_barcodes WHERE variant_id=@variantId AND lower(btrim(barcode))=lower(@code) AND barcode_source='INTERNAL')",
new
{
    variantId,
    code
},
t))
            {
                throw new ValidationException("This is an internal identifier. Choose Generate Internal Barcode.");
            }

            await c.ExecuteAsync("UPDATE variant_barcodes SET is_primary=false WHERE variant_id=@variantId", new
            {
                variantId
            }, t);
            if (owner.HasValue)
            {
                await c.ExecuteAsync("UPDATE variant_barcodes SET is_primary=true,is_active=true WHERE variant_id=@variantId AND lower(btrim(barcode))=lower(@code)",
new
{
    variantId,
    code
},
t);
            }
            else
            {
                await c.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source,is_primary) VALUES(@variantId,@code,@BarcodeSource,true)",
new
{
    variantId,
    code,
    r.BarcodeSource
},
t);
            }
        }

        if (r.ApplyLocationMinimumStock)
        {
            var locationId = factory.Scope!.LocationId;
            if (r.LocationMinimumStockLevel is null)
            {
                await c.ExecuteAsync("DELETE FROM variant_location_settings WHERE variant_id=@variantId AND location_id=@locationId",
new
{
    variantId,
    locationId
},
t);
            }
            else
            {
                await c.ExecuteAsync("""
                INSERT INTO variant_location_settings(variant_id,location_id,minimum_stock_level) VALUES(@variantId,@locationId,@LocationMinimumStockLevel)
                ON CONFLICT(tenant_id,variant_id,location_id) DO UPDATE SET minimum_stock_level=EXCLUDED.minimum_stock_level
                """,
new
{
    variantId,
    locationId,
    r.LocationMinimumStockLevel
},
t);
            }
        }

        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,'Products','VARIANT_SAVE','product_variants',@variantId,CAST(@payload AS jsonb))",
new
{
    userId,
    variantId,
    payload = JsonSerializer.Serialize(r)
},
t);
        if (r.SupplierIdentifiers is not null)
        {
            if (factory.Scope?.CatalogRegistrationEnhancementsAvailable != true) throw new ValidationException("Apply migration 052 before saving supplier-specific item codes.");
            if (!await c.ExecuteScalarAsync<bool>("SELECT sf_transfer_permission('SUPPLIERS','VIEW')",transaction:t)) throw new ValidationException("Supplier view permission is required to edit supplier-specific item codes.");
            if (r.SupplierIdentifiers.Count(i=>i.IsPreferred)>1 || r.SupplierIdentifiers.Count>20 || r.SupplierIdentifiers.Select(i=>i.SupplierId).Distinct().Count()!=r.SupplierIdentifiers.Count)
                throw new ValidationException("Use one code per supplier, with at most 20 suppliers per item.");
            foreach(var identifier in r.SupplierIdentifiers)
            {
                Validator.ValidateObject(identifier,new ValidationContext(identifier),true);
                identifier.SupplierSku=identifier.SupplierSku.Trim();
                if(identifier.SupplierSku.Length==0||identifier.SupplierSku.Any(char.IsControl))throw new ValidationException("Enter a valid supplier item code.");
                if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM suppliers s WHERE s.supplier_id=@SupplierId AND (s.is_active OR EXISTS(SELECT 1 FROM variant_supplier_identifiers i WHERE i.variant_id=@variantId AND i.supplier_id=s.supplier_id AND i.supplier_sku=@SupplierSku)))",new{identifier.SupplierId,identifier.SupplierSku,variantId},t))
                    throw new ValidationException("Choose an active supplier in this shop setup.");
                await c.ExecuteAsync("INSERT INTO variant_supplier_identifiers(variant_id,supplier_id,supplier_sku) VALUES(@variantId,@SupplierId,@SupplierSku) ON CONFLICT(tenant_id,variant_id,supplier_id) DO UPDATE SET supplier_sku=excluded.supplier_sku",new{variantId,identifier.SupplierId,identifier.SupplierSku},t);
                if(await c.ExecuteScalarAsync<bool>("SELECT to_jsonb(i) ? 'purchasing_details' FROM variant_supplier_identifiers i WHERE variant_id=@variantId AND supplier_id=@SupplierId",new{variantId,identifier.SupplierId},t))
                    await c.ExecuteAsync("UPDATE variant_supplier_identifiers SET purchasing_details=CAST(@Details AS jsonb) WHERE variant_id=@variantId AND supplier_id=@SupplierId",new{variantId,identifier.SupplierId,Details=JsonSerializer.Serialize(identifier,new JsonSerializerOptions(JsonSerializerDefaults.Web))},t);
            }
            await c.ExecuteAsync("DELETE FROM variant_supplier_identifiers WHERE variant_id=@variantId AND NOT(supplier_id=ANY(@Ids))",new{variantId,Ids=r.SupplierIdentifiers.Select(i=>i.SupplierId).ToArray()},t);
        }
        if(r.UseDefaultPurchasePrice || r.UseDefaultSellingPrice || await c.ExecuteScalarAsync<bool>("SELECT to_jsonb(v) ? 'use_default_purchase_price' FROM product_variants v WHERE variant_id=@variantId",new{variantId},t))
            await c.ExecuteAsync("UPDATE product_variants SET use_default_purchase_price=@UseDefaultPurchasePrice,use_default_selling_price=@UseDefaultSellingPrice WHERE variant_id=@variantId",new{variantId,r.UseDefaultPurchasePrice,r.UseDefaultSellingPrice},t);
        await VehicleFitmentStorage.SaveAsync(c,t,r.ProductId,variantId,r.VehicleFitments);
        return variantId;
    }

    private static SupplierItemIdentifier WithSupplierDetails(int id,string sku,string name,string? details)
    {
        var item=details is null?new SupplierItemIdentifier():JsonSerializer.Deserialize<SupplierItemIdentifier>(details,new JsonSerializerOptions(JsonSerializerDefaults.Web))??new();
        item.SupplierId=id;item.SupplierSku=sku;item.SupplierName=name;return item;
    }
    public async Task<string> GenerateBarcodeAsync(long id, int userId)
    {
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        await c.ExecuteAsync("SELECT product_id FROM products WHERE product_id=(SELECT product_id FROM product_variants WHERE variant_id=@id) FOR UPDATE",new{id},t);
        if (await c.QuerySingleOrDefaultAsync<long?>("SELECT variant_id FROM product_variants WHERE variant_id=@id FOR UPDATE", new { id }, t) is null)
        {
            throw new ValidationException("Variant not found.");
        }

        var code = await c.QueryFirstOrDefaultAsync<string>("SELECT barcode FROM variant_barcodes WHERE variant_id=@id AND barcode_source='INTERNAL' AND is_active ORDER BY variant_barcode_id",
new
{
    id
},
t);
        if (code is null)
        {
            code = "SFV" + id.ToString("D8");
            if (await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM product_units WHERE lower(serial_number)=lower(@code) OR lower(barcode)=lower(@code) OR lower(internal_barcode)=lower(@code) OR lower(manufacturer_barcode)=lower(@code))",
new
{
    code
},
t))
            {
                throw new ValidationException("Generated barcode conflicts with a unit identifier.");
            }

            await c.ExecuteAsync("INSERT INTO variant_barcodes(variant_id,barcode,barcode_source) VALUES(@id,@code,'INTERNAL')", new
            {
                id,
                code
            }, t);
            await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id) VALUES(@userId,'Products','GENERATE_LABEL','product_variants',@id)",
new
{
    userId,
    id
},
t);
        }

        await t.CommitAsync();
        return code;
    }

    public async Task<string?> SaveUnitBarcodeAsync(long unitId, UnitBarcodeRequest request, int userId)
    {
        Validator.ValidateObject(request, new ValidationContext(request), true);
        var manufacturer = string.IsNullOrWhiteSpace(request.ManufacturerBarcode) ? null : request.ManufacturerBarcode.Trim();
        await using var c = factory.CreateConnection();
        await c.OpenAsync();
        await using var t = await c.BeginTransactionAsync();
        await factory.LockBusinessProfileAsync(c, t);
        var productId = await c.QuerySingleOrDefaultAsync<int?>("SELECT product_id FROM product_units WHERE product_unit_id=@unitId", new
        {
            unitId
        }, t) ?? throw new ValidationException("Physical unit not found.");
        await c.ExecuteAsync("SELECT product_id FROM products WHERE product_id=@productId FOR UPDATE", new
        {
            productId
        }, t);
        var current = await c.QuerySingleAsync<UnitIdentifiers>("SELECT internal_barcode AS InternalBarcode,manufacturer_barcode AS ManufacturerBarcode FROM product_units WHERE product_unit_id=@unitId FOR UPDATE",
new
{
    unitId
},
t);
        manufacturer ??= current.ManufacturerBarcode;
        if (current.ManufacturerBarcode is not null && current.ManufacturerBarcode != manufacturer)
        {
            throw new ValidationException("An existing unit barcode cannot be replaced; retain its identity and use an internal label.");
        }

        var code = current.InternalBarcode ?? (request.GenerateInternal ? "SFU" + unitId.ToString("D10") : null);
        foreach (var value in new[]
        {
            manufacturer,
            code
        }.Where(x => x is not null))
        {
            if (await c.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM variant_barcodes WHERE lower(btrim(barcode))=lower(@value)) OR
                    EXISTS(SELECT 1 FROM product_units WHERE product_unit_id<>@unitId AND
                        (lower(btrim(serial_number))=lower(@value) OR lower(btrim(barcode))=lower(@value)
                        OR lower(btrim(manufacturer_barcode))=lower(@value) OR lower(btrim(internal_barcode))=lower(@value)))
                """,
new
{
    unitId,
    value
},
t))
            {
                throw new ValidationException("This is a shared or conflicting barcode. Store model barcodes on the variant; unit barcodes must identify one physical item.");
            }
        }

        await c.ExecuteAsync("UPDATE product_units SET manufacturer_barcode=@manufacturer,internal_barcode=@code,updated_at=CURRENT_TIMESTAMP WHERE product_unit_id=@unitId",
new
{
    unitId,
    manufacturer,
    code
},
t);
        await c.ExecuteAsync("INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,new_values) VALUES(@userId,'Products','UNIT_BARCODE','product_units',@unitId,CAST(@payload AS jsonb))",
new
{
    userId,
    unitId,
    payload = JsonSerializer.Serialize(new
    {
        manufacturer,
        code
    })
},
t);
        await t.CommitAsync();
        return code ?? manufacturer;
    }

    private sealed class UnitIdentifiers
    {
        public string? InternalBarcode
        {
            get; set;
        }
        public string? ManufacturerBarcode
        {
            get; set;
        }
    }

    internal static async Task<VariantResponse> ResolveAsync(NpgsqlConnection c,
NpgsqlTransaction? t,
int productId,
long? variantId,
long? unitId = null,
bool activeOnly = true,
bool requireExplicitVariant = false)
    {
        var result = await c.QuerySingleOrDefaultAsync<VariantResponse>("""
            SELECT v.variant_id AS VariantId,v.product_id AS ProductId,v.variant_name AS VariantName,v.is_serial_tracked AS IsSerialTracked,
                v.tax_percentage AS TaxPercentage,v.warranty_months AS WarrantyMonths,v.selling_price AS SellingPrice,v.mrp AS Mrp,p.model AS Model
            FROM product_variants v JOIN products p USING(product_id)
            WHERE v.product_id=@productId AND (NOT @activeOnly OR (v.is_active AND p.is_active))
              AND EXISTS(SELECT 1 FROM categories cc WHERE cc.category_id=p.category_id AND COALESCE(to_jsonb(cc)->>'business_profile_code','ELECTRICAL')=COALESCE(NULLIF(current_setting('app.business_profile_code',true),''),'ELECTRICAL'))
              AND ((NOT @requireExplicitVariant AND NULLIF(to_jsonb(p)->>'parent_product_code','') IS NULL) OR NOT COALESCE((to_jsonb(p)->>'has_variants')::boolean,false) OR CAST(@variantId AS bigint) IS NOT NULL OR CAST(@unitId AS bigint) IS NOT NULL)
              AND (CASE WHEN CAST(@variantId AS bigint) IS NOT NULL THEN v.variant_id=@variantId
                        WHEN CAST(@unitId AS bigint) IS NOT NULL THEN v.variant_id=(SELECT variant_id FROM product_units WHERE product_unit_id=@unitId)
                        ELSE v.is_default END)
            FOR SHARE OF v
            """,
new
{
    productId,
    variantId,
    unitId,
    activeOnly,
    requireExplicitVariant
},
t);
        return result ?? throw new ValidationException("Select an active variant belonging to the product.");
    }
}
