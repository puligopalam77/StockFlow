using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StockFlow.Application.DTOs.Products;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository repository;
    private readonly ICategoryRepository categories;
    private readonly IBrandRepository brands;
    private readonly StockFlow.Application.Common.BusinessScope? scope;
    private readonly ICatalogSchemaRepository? schemas;
    private readonly IVariantRepository? variantRepository;
    private readonly ICompanySettingsRepository? company;
    // ASP.NET supplies these services when it creates this class.
    public ProductService(IProductRepository repository, ICategoryRepository categories, IBrandRepository brands, StockFlow.Application.Common.BusinessScope? scope = null, ICatalogSchemaRepository? schemas = null, IVariantRepository? variantRepository = null,ICompanySettingsRepository? company=null)
    {
        this.repository = repository;
        this.categories = categories;
        this.brands = brands;
        this.scope = scope;
        this.schemas = schemas;
        this.variantRepository = variantRepository;
        this.company=company;
    }

    public async Task<IEnumerable<ProductResponse>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        return (await repository.GetAllAsync(activeOnly, term)).Select(Map);
    }

    public async Task<ProductResponse?> GetByIdAsync(int id)
    {
        if (await repository.GetByIdAsync(id) is { } entity)
        {
            return Map(entity);
        }
        else
        {
            return null;
        }
    }

    public async Task<int> CreateAsync(CreateProductRequest request)
    {
        if(request.RegistrationBusinessProfile is not null && scope is not null && request.RegistrationBusinessProfile!=scope.BusinessProfileCode)
            throw new ValidationException("The active shop changed. Return to the original shop before saving this product draft.");
        var requestHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        if(request.RegistrationRequestId is { } requestId && await repository.FindRegistrationAsync(requestId,requestHash) is { } savedId)return savedId;
        await ValidateAsync(request, null);
        if (request.Variants is not null)
        {
            var entity=ToEntity(request);entity.RegistrationRequestHash=requestHash;
            return await repository.SaveRegistrationAsync(entity, request.Variants);
        }

        return await repository.CreateAsync(ToEntity(request));
    }

    public async Task<bool> UpdateAsync(int id, UpdateProductRequest request)
    {
        if(request.RegistrationBusinessProfile is not null && scope is not null && request.RegistrationBusinessProfile!=scope.BusinessProfileCode)
            throw new ValidationException("The active shop changed. Return to the original shop before saving this product draft.");
        if (await repository.GetByIdAsync(id) is not { } existing)
        {
            return false;
        }
        if (scope?.CatalogRegistrationEnhancementsAvailable == true && existing.HasVariants && !request.HasVariants && variantRepository is not null)
        {
            var savedVariants=(await variantRepository.GetAsync(id,null,false)).ToList();
            if(savedVariants.Count>1||savedVariants.Any(v=>!v.IsDefault))
                throw new ValidationException("Keep Has Variants enabled to preserve the saved stock identities.");
        }

        await ValidateAsync(request, id);
        var entity = ToEntity(request);
        entity.ProductId = id;
        entity.ExpectedEditVersion=request.ExpectedEditVersion;
        entity.IsActive = request.IsActive;
        entity.ProductStatus = request.ProductStatus ?? (existing.ProductStatus == "DISCONTINUED" && !request.IsActive ? "DISCONTINUED" : request.IsActive ? "ACTIVE" : "INACTIVE");
        if (request.ProductStatus is not null) entity.IsActive = request.ProductStatus == "ACTIVE";
        // Registration edits metadata only; commercial values belong to variants.
        if (request.TrackingType is not null)
        {
            entity.IsRegistrationEdit = true;
            entity.Sku = existing.Sku;
            entity.Barcode = existing.Barcode;
            entity.PurchasePrice = existing.PurchasePrice;
            entity.SellingPrice = existing.SellingPrice;
            entity.MinimumStockLevel = existing.MinimumStockLevel;
        }

        if (request.Variants is not null || request.VehicleFitments is not null || request.RegistrationDefaults is not null || request.ParentProductCode is not null || request.DetailedDescription is not null)
        {
            await repository.SaveRegistrationAsync(entity, request.Variants??[]);
            return true;
        }

        await repository.SaveRegistrationAsync(entity,[]);
        return true;
    }

    public Task<bool> DeleteAsync(int id)
    {
        return repository.DeleteAsync(id);
    }

    public Task ValidateRegistrationAsync(CreateProductRequest request) => ValidateAsync(request, null);

    private async Task ValidateAsync(CreateProductRequest request, int? id)
    {
        if(request.ProductImageUrl is {Length:>0} url && (!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)))throw new ValidationException("Use an HTTP or HTTPS image URL without embedded credentials.");
        if(id is null && request.Variants is null && request.ParentProductCode is {Length:>0} parentCode)
        {
            if(request.HasVariants)throw new ValidationException("Supply the exact sellable variants for this product.");
            request.Variants=[new(){VariantName="DEFAULT",Sku=parentCode.Trim().ToUpperInvariant(),PurchasePrice=request.PurchasePrice,SellingPrice=request.SellingPrice,UseDefaultPurchasePrice=request.RegistrationDefaults is not null,UseDefaultSellingPrice=request.RegistrationDefaults is not null,Mrp=request.RegistrationDefaults?.Mrp??0,PackQuantity=request.RegistrationDefaults?.PackQuantity??1,TaxPercentage=request.TaxPercentage,WarrantyMonths=request.WarrantyMonths,MinimumStockLevel=request.RegistrationDefaults?.ReorderLevel??request.MinimumStockLevel,PrimaryBarcode=request.Barcode,BarcodeSource=string.IsNullOrWhiteSpace(request.Barcode)?null:"MANUFACTURER"}];
        }
        if(request.RegistrationRequestId==Guid.Empty)throw new ValidationException("Use a valid registration request ID.");
        if((request.RegistrationRequestId.HasValue || request.RegistrationDefaults is not null || request.VehicleFitments is not null) && id is null && string.IsNullOrWhiteSpace(request.ParentProductCode))throw new ValidationException("Product Code is required for registration.");
        foreach(var entries in new[]{request.VehicleFitments}.Concat((request.Variants??[]).Where(v=>v is not null).Select(v=>v.VehicleFitments)).Where(v=>v is not null))
        {
            if(entries!.Count>30)throw new ValidationException("Use at most 30 verified vehicle fitments.");
            foreach(var entry in entries){if(entry is null)throw new ValidationException("Use verified vehicle entries.");Validator.ValidateObject(entry,new ValidationContext(entry),true);if(entry.YearFrom>entry.YearTo)throw new ValidationException("Vehicle year range is invalid.");}
        }
        var pricingDefaults=request.RegistrationDefaults;
        if(pricingDefaults is null && request.Variants?.Any(v=>v is not null && (v.UseDefaultPurchasePrice||v.UseDefaultSellingPrice))==true)
        {
            var stored=id.HasValue?(await repository.GetByIdAsync(id.Value))?.RegistrationDefaultsJson:null;
            pricingDefaults=stored is null?null:JsonSerializer.Deserialize<ProductRegistrationDefaults>(stored,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(pricingDefaults is null)throw new ValidationException("Save product pricing defaults before using inherited variant prices.");
        }
        if(pricingDefaults is { } defaults)
        {
            if(request.RegistrationDefaults?.CatalogPricesIncludeTax is { } submittedMode && company is not null && submittedMode!=((await company.GetAsync()).DefaultTaxMode=="INCLUSIVE"))throw new ValidationException("The shop tax mode changed. Reopen registration and review selling prices before saving.");
            Validator.ValidateObject(defaults,new ValidationContext(defaults),true);
            if(defaults.VariantAxes is null || defaults.VariantAxes.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=defaults.VariantAxes.Count || defaults.VariantAxes.Any(code=>!System.Text.RegularExpressions.Regex.IsMatch(code??"",@"^[a-z][a-z0-9_]{0,39}$")))throw new ValidationException("Use distinct valid variant attribute codes, up to three.");
            if(defaults.Mrp>0 && defaults.SellingPrice>defaults.Mrp)throw new ValidationException("Selling price cannot exceed MRP.");
            foreach(var variant in request.Variants??[]){if(variant.UseDefaultPurchasePrice)variant.PurchasePrice=defaults.PurchasePrice;if(variant.UseDefaultSellingPrice)variant.SellingPrice=defaults.SellingPrice;}
        }
        var category = await categories.GetByIdAsync(request.CategoryId);
        if(request.TrackingType is null && category?.RegistrationDefaultsJson is { } configuredDefaults)
            request.TrackingType=JsonSerializer.Deserialize<StockFlow.Application.DTOs.Categories.CategoryInventoryDefaults>(configuredDefaults,new JsonSerializerOptions(JsonSerializerDefaults.Web))?.TrackingMethod;
        if(request.RemoveProductImage)
        {
            if(request.ProductImageData is not null || request.ProductImages?.Count>0)
                throw new ValidationException("Remove the image or upload a replacement, rather than both in the same request.");
            request.ProductImageName=request.ProductImageContentType=null;
            if(scope?.ProductImageGalleryAvailable==true)request.ProductImages=[];
        }
        if(request.ProductImages is not null && scope?.ProductImageGalleryAvailable != true)
            throw new ValidationException("Apply migration 053 before saving multiple product images.");
        if(scope?.ProductImageGalleryAvailable == true && request.ProductImages is null && request.ProductImageData is not null)
            request.ProductImages = [new(){FileName=request.ProductImageName,ContentType=request.ProductImageContentType,Data=request.ProductImageData}];
        if(request.ProductImages is not null)
        {
            if(request.ProductImages.Count>5)throw new ValidationException("Upload at most five product images.");
            var references=new HashSet<Guid>();var legacyCount=0;
            foreach(var image in request.ProductImages)
            {
                if(image is null)throw new ValidationException("Invalid product image.");
                if(image.VariantName is not null){
                    image.VariantName=image.VariantName.Trim();
                    var names=request.Variants?.Select(v=>v.VariantName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if(id.HasValue && variantRepository is not null){names??=new(StringComparer.OrdinalIgnoreCase);names.UnionWith((await variantRepository.GetAsync(id,null,false)).Select(v=>v.VariantName));}
                    if(image.VariantName.Length>150 || !string.IsNullOrEmpty(image.VariantName)&&names?.Contains(image.VariantName)!=true)throw new ValidationException("Assign images only to variants belonging to this product.");
                    if(image.VariantName.Length==0)image.VariantName=null;
                }
                if(image.Data is null)
                {
                    if(id is null || image.IsLegacy && ++legacyCount>1 || !image.IsLegacy && (!image.ImageId.HasValue || !references.Add(image.ImageId.Value)))
                        throw new ValidationException("Choose distinct saved images belonging to this product.");
                    continue;
                }
                image.FileName=Path.GetFileName(image.FileName?.Trim()??"");image.ContentType=image.ContentType?.Trim().ToLowerInvariant();
                var data=image.Data;
                var signature=image.ContentType switch{
                    "image/png"=>data.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
                    "image/jpeg"=>data.AsSpan().StartsWith(new byte[]{255,216,255}),
                    "image/webp"=>data.Length>=12&&data.AsSpan(0,4).SequenceEqual("RIFF"u8)&&data.AsSpan(8,4).SequenceEqual("WEBP"u8), _=>false};
                if(data.Length is <1 or >3000000 || !signature || image.FileName.Length is <1 or >255)
                    throw new ValidationException("Each product image must be a valid JPG, PNG or WebP up to 3 MB.");
                image.ImageId=Guid.NewGuid();image.IsLegacy=false;image.Url=null;
            }
        }
        if (request.ProductStatus is not null && scope?.CatalogRegistrationEnhancementsAvailable != true)
            throw new ValidationException("Apply migration 052 before using product status.");
        if (id is null && request.ProductStatus == "DISCONTINUED")
            throw new ValidationException("New products may be active or inactive.");
        if (scope is not null && scope.BusinessProfileCode != StockFlow.Application.Common.BusinessProfiles.Electrical &&
            (request.TrackingType == "SERIAL" || request.TrackingType is null && request.IsSerialTracked || scope.BusinessProfileCode != StockFlow.Application.Common.BusinessProfiles.BikeAccessories && (request.WarrantyMonths > 0 || request.Variants?.Any(v => v.WarrantyMonths > 0) == true)))
            throw new ValidationException("Serial tracking and electrical warranty are not enabled for this shop profile.");
        request.ProductName = System.Text.RegularExpressions.Regex.Replace(request.ProductName?.Trim() ?? "", @"\s+", " ");
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        if (request.Variants is { } variants)
        {
            if (variants.Any(v => v is null || v.Attributes is null || v.Barcodes is null))
                throw new ValidationException("Supply valid variant configurations, attribute objects and barcode lists.");
            if (scope?.CatalogRegistrationEnhancementsAvailable == true && variantRepository is not null)
            {
                var existingVariants = (await variantRepository.FindIdentityConflictsAsync(variants.Select(v=>v.Sku??"").ToArray(),variants.SelectMany(v=>v.Barcodes.Append(v.PrimaryBarcode??"")).Where(code=>!string.IsNullOrWhiteSpace(code)).ToArray())).ToList();
                var incomingSkus = variants.Select(v => v.Sku?.Trim() ?? "").ToList();
                if (incomingSkus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != incomingSkus.Count)
                    throw new ValidationException("Each variant needs a different SKU.");
                foreach (var variant in variants)
                {
                    if (existingVariants.Any(v => v.VariantId != variant.VariantId && v.Sku.Equals(variant.Sku?.Trim(), StringComparison.OrdinalIgnoreCase)))
                        throw new ValidationException("SKU already exists. Use the saved product or choose another SKU.");
                    var codes = variant.Barcodes.Append(variant.PrimaryBarcode ?? "").Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (existingVariants.Any(v => v.VariantId != variant.VariantId && v.Barcodes.Append(v.PrimaryBarcode ?? "").Any(codes.Contains)))
                        throw new ValidationException("A barcode already belongs to another item.");
                }
            }
            var combinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var barcodeOwners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < variants.Count; index++)
            {
                var variant = variants[index];
                if (variant.Attributes.Any(a=>string.IsNullOrWhiteSpace(a.Key)||string.IsNullOrWhiteSpace(a.Value))) throw new ValidationException("Use valid attribute names and values.");
                if (id is null && request.ProductStatus == "INACTIVE") variant.IsActive = false;
                if (variant.BarcodeSource == "MANUFACTURER" && string.IsNullOrWhiteSpace(variant.PrimaryBarcode))
                    throw new ValidationException("Enter the manufacturer barcode or choose Assign later.");
                if (variant.Attributes.Count > 0 && !combinations.Add(JsonSerializer.Serialize(variant.Attributes.OrderBy(a=>a.Key,StringComparer.OrdinalIgnoreCase).Select(a=>new[]{a.Key.Trim().ToUpperInvariant(),a.Value.Trim().ToUpperInvariant()}))))
                    throw new ValidationException("Each variant must have a different attribute combination.");
                foreach (var code in variant.Barcodes.Append(variant.PrimaryBarcode ?? "").Where(c=>!string.IsNullOrWhiteSpace(c)).Select(c=>c.Trim()))
                {
                    if (barcodeOwners.TryGetValue(code,out var owner) && owner != index) throw new ValidationException("Each barcode must belong to only one variant.");
                    barcodeOwners[code]=index;
                }
            }
            if (variants.Count is < 1 or > 30 || !request.HasVariants && variants.Count != 1)
            {
                throw new ValidationException("Supply one item configuration, or up to 30 variants when Has Variants is enabled.");
            }

            if (variants.Where(v => v.VariantId.HasValue).Select(v => v.VariantId).Distinct().Count() != variants.Count(v => v.VariantId.HasValue))
            {
                throw new ValidationException("A variant can only be included once.");
            }

            foreach (var variant in variants)
            {
                if(!variant.VariantId.HasValue && !System.Text.RegularExpressions.Regex.IsMatch(variant.Sku??"",@"^[A-Za-z0-9][A-Za-z0-9_-]{2,39}$"))throw new ValidationException("New variant SKUs require 3 to 40 letters, numbers, hyphens or underscores.");
                if(variant.Mrp>0 && variant.SellingPrice>variant.Mrp)throw new ValidationException("Selling price cannot exceed MRP.");
                if (variant is null || variant.Attributes is null) throw new ValidationException("Supply attributes for each item configuration.");
                variant.ProductId = id ?? 1; // Actual ID is assigned inside the save transaction.
                variant.IsSerialTracked = request.TrackingType == "SERIAL" || request.TrackingType is null && request.IsSerialTracked;
                Validator.ValidateObject(variant, new ValidationContext(variant), true);
                if (variant.SupplierIdentifiers is { } identifiers)
                {
                    if (scope?.CatalogRegistrationEnhancementsAvailable != true || identifiers.Any(i=>i is null) || identifiers.Count>20 || identifiers.Select(i=>i.SupplierId).Distinct().Count()!=identifiers.Count)
                        throw new ValidationException("Use at most 20 distinct suppliers after applying migration 052.");
                    foreach(var identifier in identifiers) Validator.ValidateObject(identifier,new ValidationContext(identifier),true);
                    if(identifiers.Count(i=>i.IsPreferred)>1)throw new ValidationException("Choose one preferred supplier per SKU.");
                }
                if (id is null && variant.VariantId.HasValue)
                {
                    throw new ValidationException("A new product cannot use an existing variant.");
                }
            }
        }

        var originalCode=id.HasValue?(await repository.GetByIdAsync(id.Value))?.ParentProductCode:null;
        var keepCode=originalCode is not null&&string.Equals(originalCode.Trim(),request.ParentProductCode?.Trim(),StringComparison.OrdinalIgnoreCase);
        request.ParentProductCode=keepCode?originalCode:string.IsNullOrWhiteSpace(request.ParentProductCode)?null:request.ParentProductCode.Trim().ToUpperInvariant();
        if(!keepCode&&request.ParentProductCode is not null && !System.Text.RegularExpressions.Regex.IsMatch(request.ParentProductCode,@"^[A-Z0-9][A-Z0-9_-]{2,39}$"))throw new ValidationException("Use a product code of 3 to 40 letters, numbers, hyphens or underscores.");
        if(request.DetailedDescription?.Length>6000)throw new ValidationException("Detailed description must be at most 6,000 characters.");
        if(request.ParentProductCode is not null && await repository.ParentCodeExistsAsync(request.ParentProductCode,id))throw new ValidationException("Parent product code already exists in this shop.");
        if(id is null && request.Variants?.Any(v=>v.Sku?.Length>40)==true)throw new ValidationException("New variant SKUs must be at most 40 characters.");
        if (request.ProductName.All(char.IsDigit))
        {
            throw new ValidationException("Product name must include more than numbers.");
        }

        if (request.ProductImageData is null)
        {
            request.ProductImageName = request.ProductImageContentType = null;
        }
        else
        {
            request.ProductImageName = Path.GetFileName(request.ProductImageName?.Trim() ?? throw new ValidationException("Product image name is required."));
            request.ProductImageContentType = request.ProductImageContentType?.Trim().ToLowerInvariant();
            if (request.ProductImageData.Length is < 1 or > 3000000 || request.ProductImageContentType is not ("image/jpeg" or "image/png" or "image/webp"))
            {
                throw new ValidationException("Upload one JPG, PNG or WebP product image up to 3 MB.");
            }
        }

        if (decimal.Round(request.TaxPercentage, 2) != request.TaxPercentage)
        {
            throw new ValidationException("Default tax supports at most two decimal places.");
        }

        if (request.WarrantyMonths > 0 && request.WarrantyProvider is null)
        {
            request.WarrantyProvider = "MANUFACTURER";
        }

        if (request.WarrantyMonths == 0)
        {
            request.WarrantyProvider = null;
        }

        var changingCategory=id is null || (await repository.GetByIdAsync(id.Value))?.CategoryId!=request.CategoryId;
        if(category is null || changingCategory && !category.IsActive)
            throw new ValidationException("Choose an active category.");
        if(changingCategory && category.ParentCategoryId is null && (await categories.GetAllAsync(true)).Any(c=>c.ParentCategoryId==category.CategoryId))
            throw new ValidationException("Choose a subcategory when this parent has active subcategories.");
        if(changingCategory && category.ParentCategoryId is { } parentId)
        {
            var parent=await categories.GetByIdAsync(parentId);
            if(parent is not {IsActive:true,ParentCategoryId:null})
                throw new ValidationException("Choose an active parent category and one of its immediate subcategories.");
        }
        if (category.RegistrationDefaultsJson is not null)
        {
            var policy=JsonSerializer.Deserialize<StockFlow.Application.DTOs.Categories.CategoryInventoryDefaults>(category.RegistrationDefaultsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if(policy is not null)
            {
                request.TrackingType ??= policy.TrackingMethod;
                if(!policy.AllowProductOverride && (request.UnitOfMeasure!=policy.StockUnit || request.TrackingType!=policy.TrackingMethod || request.Variants?.Any(v=>v.IsSerialTracked!=(policy.TrackingMethod=="SERIAL"))==true))
                    throw new ValidationException("This category requires its configured stock unit and tracking method.");
                if(!policy.EnableWarranty && !policy.AllowProductOverride && (request.WarrantyMonths>0||request.Variants?.Any(v=>v.WarrantyMonths>0)==true))
                    throw new ValidationException("Warranty is not enabled for this category.");
                if(id is null && !policy.GenerateSkuAutomatically && request.Variants is null && string.IsNullOrWhiteSpace(request.Sku))
                    throw new ValidationException("Enter a SKU for this category.");
            }
        }

        if (schemas is not null)
        {
            var schema = await schemas.GetAsync();
            var rules = CategoryFieldInheritance.Fields(schema, request.CategoryId);
            foreach (var variant in request.Variants ?? [])
                foreach (var rule in rules.Where(r => !string.IsNullOrEmpty(r.DefaultValue)))
                    if (string.IsNullOrWhiteSpace(variant.Attributes.GetValueOrDefault(rule.Code))) variant.Attributes[rule.Code] = rule.DefaultValue!;
            var saved = id.HasValue && variantRepository is not null
                ? (await variantRepository.GetAsync(id, null, false)).ToList() : [];
            if (id.HasValue && await repository.GetByIdAsync(id.Value) is { } old && old.CategoryId != request.CategoryId &&
                (CategoryFieldInheritance.Fields(schema,old.CategoryId).Count > 0 || rules.Count > 0))
                throw new ValidationException("Configured product categories cannot change. Register a new product to preserve stock history.");
            foreach (var v in request.Variants ?? [])
            {
                var previous = saved.SingleOrDefault(s => s.VariantId == v.VariantId);
                if (previous is null) continue;
                if (rules.Where(f => f.VariantAxis).Any(f => previous.Attributes.TryGetValue(f.Code, out var value) && value != v.Attributes.GetValueOrDefault(f.Code)))
                    throw new ValidationException("Saved variant options cannot change. Add a new variant instead.");
            }
            var values = saved.Where(s => request.Variants?.Any(v => v.VariantId == s.VariantId) != true).Select(s => s.Attributes)
                .Concat(request.Variants?.Select(v => v.Attributes) ?? []).ToList();
            var identityAxes=request.RegistrationDefaults?.VariantAxes;
            if(identityAxes is null && id.HasValue && (await repository.GetByIdAsync(id.Value))?.RegistrationDefaultsJson is { } savedDefaults)identityAxes=JsonSerializer.Deserialize<ProductRegistrationDefaults>(savedDefaults,new JsonSerializerOptions(JsonSerializerDefaults.Web))?.VariantAxes;
            CatalogSchemaValidator.ValidateValues(schema, request.CategoryId, values,identityAxes);
        }

        if (request.BrandId is int brandId)
        {
            var brand = await brands.GetByIdAsync(brandId);
            var unchangedBrand=id.HasValue&&!changingCategory&&(await repository.GetByIdAsync(id.Value))?.BrandId==brandId;
            if (brand is null || !brand.IsActive&&!unchangedBrand)
            {
                throw new ValidationException("Choose an active brand.");
            }

            if(id is null && scope?.CatalogRegistrationEnhancementsAvailable==true && brand.CategoryIds.Length>0 && !request.AllowUnassociatedBrand)
            {
                var all=(await categories.GetAllAsync(true)).ToDictionary(c=>c.CategoryId);var ancestors=new HashSet<int>();var current=request.CategoryId;
                while(ancestors.Add(current) && all.TryGetValue(current,out var node) && node.ParentCategoryId is { } parent)current=parent;
                if(!brand.CategoryIds.Any(ancestors.Contains))throw new ValidationException("This brand is not associated with the selected category. Choose Show all brands to confirm the exception.");
            }
            if (!unchangedBrand&&scope?.CatalogRegistrationEnhancementsAvailable != true && !brand.CategoryIds.Contains(request.CategoryId))
            {
                throw new ValidationException("Choose a brand assigned to the selected category. Configure its categories in Brand registration first.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Sku) && await repository.SkuExistsAsync(request.Sku.Trim(), id))
        {
            throw new ValidationException("SKU already exists.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) && await repository.BarcodeExistsAsync(request.Barcode.Trim(), id))
        {
            throw new ValidationException("Barcode already exists.");
        }
    }

    private static Product ToEntity(CreateProductRequest request)
    {
        return new()
        {
            GstTaxRuleId=request.GstTaxRuleId,
            RegistrationRequestId=request.RegistrationRequestId,
            VehicleFitmentsJson=request.VehicleFitments is null?null:JsonSerializer.Serialize(request.VehicleFitments,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RegistrationDefaultsJson=request.RegistrationDefaults is null?null:JsonSerializer.Serialize(request.RegistrationDefaults,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ParentProductCode = request.ParentProductCode,
            DetailedDescription = request.DetailedDescription,
            ProductName = request.ProductName.Trim(),
            ProductStatus = request.ProductStatus ?? "ACTIVE",
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            HsnCode = string.IsNullOrWhiteSpace(request.HsnCode) ? null : request.HsnCode.Trim().ToUpperInvariant(),
            UnitOfMeasure = request.UnitOfMeasure,
            ProductImageUrl = string.IsNullOrWhiteSpace(request.ProductImageUrl) ? null : request.ProductImageUrl.Trim(),
            ProductImageName = request.ProductImageName,
            ProductImageContentType = request.ProductImageContentType,
            ProductImageData = request.ProductImageData,
            RemoveProductImage = request.RemoveProductImage,
            ProductImagesJson = request.ProductImages is null ? null : JsonSerializer.Serialize(request.ProductImages,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            HasVariants = request.HasVariants,
            WarrantyProvider = request.WarrantyProvider,
            Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim(),
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            PurchasePrice = request.PurchasePrice,
            SellingPrice = request.SellingPrice,
            TaxPercentage = request.TaxPercentage,
            WarrantyMonths = request.WarrantyMonths,
            MinimumStockLevel = request.MinimumStockLevel,
            IsSerialTracked = request.TrackingType is null ? request.IsSerialTracked : request.TrackingType == "SERIAL",
            IsActive = request.ProductStatus != "INACTIVE"
        };
    }

    private static ProductResponse Map(Product entity)
    {
        return new()
        {
            RegistrationDefaults=entity.RegistrationDefaultsJson is null?null:JsonSerializer.Deserialize<ProductRegistrationDefaults>(entity.RegistrationDefaultsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            VehicleFitments=entity.VehicleFitmentsJson is null?null:JsonSerializer.Deserialize<List<VehicleFitment>>(entity.VehicleFitmentsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ProductId = entity.ProductId,
            SkuCount = entity.SkuCount,
            ConfiguredVariantCount = entity.ConfiguredVariantCount,
            SkuPreviews = JsonSerializer.Deserialize<List<ProductSkuPreview>>(entity.SkuPreviewsJson??"[]",new JsonSerializerOptions(JsonSerializerDefaults.Web))??[],
            DefaultVariantSku = entity.DefaultVariantSku,
            HasMatchingSku = entity.HasMatchingSku,
            EditVersion = entity.EditVersion,
            HasInventoryHistory = entity.HasInventoryHistory,
            CategoryChangeLocked = entity.CategoryChangeLocked,
            HistoryVariantIds = entity.HistoryVariantIds,
            ProductStatus = entity.ProductStatus,
            ParentProductCode = entity.ParentProductCode,
            DetailedDescription = entity.DetailedDescription,
            ProductName = entity.ProductName,
            CategoryId = entity.CategoryId,
            BrandId = entity.BrandId,
            Model = entity.Model,
            Description = entity.Description,
            HsnCode = entity.HsnCode,
            UnitOfMeasure = entity.UnitOfMeasure,
            ProductImageUrl = entity.ProductImageUrl,
            ProductImageName = entity.ProductImageName,
            ProductImages = (JsonSerializer.Deserialize<List<ProductGalleryImage>>(entity.ProductImagesJson??"[]",new JsonSerializerOptions(JsonSerializerDefaults.Web))??[]).Select(image=>new ProductGalleryImage{VariantName=image.VariantName,ImageId=image.ImageId,FileName=image.FileName,ContentType=image.ContentType,Url=$"/api/products/{entity.ProductId}/images/{image.ImageId}"}).ToList(),
            HasVariants = entity.HasVariants,
            WarrantyProvider = entity.WarrantyProvider,
            Sku = entity.Sku,
            Barcode = entity.Barcode,
            PurchasePrice = entity.PurchasePrice,
            SellingPrice = entity.SellingPrice,
            TaxPercentage = entity.TaxPercentage,
            WarrantyMonths = entity.WarrantyMonths,
            MinimumStockLevel = entity.MinimumStockLevel,
            IsSerialTracked = entity.IsSerialTracked,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            CategoryName = entity.CategoryName,
            BrandName = entity.BrandName
        };
    }

    public async Task<ProductResponse?> GetByBarcodeAsync(string barcode)
    {
        if (await repository.GetByBarcodeAsync(barcode.Trim()) is { } product)
        {
            return Map(product);
        }
        else
        {
            return null;
        }
    }

    public async Task<CatalogProductDraft?> LookupCatalogAsync(string barcode, int? supplierId)
    {
        barcode = barcode?.Trim() ?? "";
        if (barcode.Length is < 1 or > 150 || barcode.Any(char.IsControl))
        {
            throw new ValidationException("Enter a valid barcode (150 characters maximum).");
        }

        return await repository.LookupCatalogAsync(barcode, supplierId);
    }

    public Task<ProductImage?> GetImageAsync(int id)
    {
        return repository.GetImageAsync(id);
    }
    public Task<ProductImage?> GetGalleryImageAsync(int productId, Guid imageId) => repository.GetGalleryImageAsync(productId,imageId);
}
