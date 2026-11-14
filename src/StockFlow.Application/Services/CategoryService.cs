using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Categories;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Products;

namespace StockFlow.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository repository;
    private readonly IProductRepository products;
    private readonly ICatalogSchemaRepository? schemas;
    private readonly StockFlow.Application.Common.BusinessScope? scope;
    // ASP.NET supplies these services when it creates this class.
    public CategoryService(ICategoryRepository repository, IProductRepository products, ICatalogSchemaRepository? schemas = null, StockFlow.Application.Common.BusinessScope? scope = null)
    {
        this.repository = repository;
        this.products = products;
        this.schemas = schemas;
        this.scope = scope;
    }

    public async Task<IEnumerable<CategoryResponse>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        var all = (await repository.GetAllAsync()).ToDictionary(c => c.CategoryId);
        var children=all.Values.Where(c=>c.ParentCategoryId.HasValue).GroupBy(c=>c.ParentCategoryId!.Value).ToDictionary(g=>g.Key,g=>g.ToList());
        return all.Values.Select(c => MapWithPath(c, all, children)).Where(c => (!activeOnly || c.IsEffectivelyActive) && (string.IsNullOrWhiteSpace(term) || (c.CategoryPath + " " + c.CategoryCode + " " + c.Description).Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)))
.OrderBy(c => c.DisplayOrder).ThenBy(c => c.CategoryPath);
    }

    public async Task<CategoryResponse?> GetByIdAsync(int id)
    {
        // Direct historical lookups retain archived IDs; normal selection/list APIs omit retired groupings.
        var category=await repository.GetByIdAsync(id);
        if(category is null)return null;
        var all=(await repository.GetAllAsync()).ToDictionary(c=>c.CategoryId);
        all[id]=category;
        return MapWithPath(category,all);
    }

    public async Task<int> CreateAsync(CreateCategoryRequest request)
    {
        await ValidateAsync(request, null);
        return await repository.CreateAsync(ToEntity(request));
    }

    public async Task<bool> UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var existing = await repository.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        if(!string.IsNullOrEmpty(existing.SkuPrefix) && request.SkuPrefix is not null && !string.Equals(existing.SkuPrefix,request.SkuPrefix.Trim(),StringComparison.OrdinalIgnoreCase) && request.ExpectedSkuPrefix is null)
            throw new ValidationException("Reload the category before changing its SKU prefix.");
        if(request.ExpectedSkuPrefix is not null && !string.Equals(existing.SkuPrefix,request.ExpectedSkuPrefix,StringComparison.Ordinal))
            throw new ValidationException("Category prefix changed. Reload the category before saving.");
        await ValidateAsync(request, id);
        if (existing.ParentCategoryId != request.ParentCategoryId)
            throw new ValidationException("Parent relationships are read-only. Use a reviewed category migration to move existing categories.");

        if(existing.SkuPrefixUsed && request.SkuPrefix is not null && !string.Equals(existing.SkuPrefix,request.SkuPrefix,StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("SKU prefix is permanent after issuing a product code.");
        var entity = ToEntity(request);
        entity.CategoryId = id;
        entity.ExpectedSkuPrefix=existing.SkuPrefix;
        entity.CheckSkuPrefix=true;
        if(!existing.IsActive && request.IsActive && existing.ParentCategoryId is int parentId && await repository.GetByIdAsync(parentId) is not { IsActive: true, ParentCategoryId: null })
            throw new ValidationException("Activate the parent category first before activating this subcategory.");
        entity.IsActive = request.IsActive;
        return await repository.UpdateAsync(entity);
    }

    public Task<bool> DeleteAsync(int id) => SetStatusAsync(id, false);

    public async Task<bool> SetStatusAsync(int id, bool active)
    {
        var category=await repository.GetByIdAsync(id);
        if(category is null)return false;
        if(active && category.ParentCategoryId is int parentId && await repository.GetByIdAsync(parentId) is not { IsActive: true, ParentCategoryId: null })
            throw new ValidationException("Activate the parent category first before activating this subcategory.");
        category.IsActive=active;
        return await repository.UpdateAsync(category);
    }

    private async Task ValidateAsync(CreateCategoryRequest request, int? id)
    {
        request.CategoryName = Regex.Replace((request.CategoryName ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ");
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        request.SkuPrefix=string.IsNullOrWhiteSpace(request.SkuPrefix)?null:request.SkuPrefix.Trim().ToUpperInvariant();
        request.CategoryCode=string.IsNullOrWhiteSpace(request.CategoryCode)?null:request.CategoryCode.Trim().ToUpperInvariant();
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        if(request.CategoryCode is not null||request.DisplayOrder is not null){
            if(schemas is null||(await schemas.GetAsync())?.CategoryDetailsAvailable!=true)throw new ValidationException("Apply manual migration 057 to edit category codes and display order.");
            if(request.CategoryCode is not null&&(await repository.GetAllAsync()).Any(c=>c.CategoryId!=id&&string.Equals(c.CategoryCode,request.CategoryCode,StringComparison.OrdinalIgnoreCase)))throw new ValidationException("Category code already exists. Choose another code.");
        }
        if (request.Configuration is { } configuration)
        {
            var schema = schemas is null ? null : await schemas.GetAsync();
            if (schema?.RegistrationAvailable != true) throw new ValidationException("Apply manual migration 056 to configure category fields, variants and inventory defaults.");
            if (schema.Version != configuration.SchemaVersion) throw new ValidationException("Category settings changed. Reload before saving.");
            if (configuration.Attributes is null || configuration.Fields is null || configuration.Inventory is null ||
                configuration.Attributes.Count > 30 || configuration.Fields.Count > 30 || configuration.Attributes.Any(a=>a is null) || configuration.Attributes.Select(a=>a.Code).Distinct().Count()!=configuration.Attributes.Count)
                throw new ValidationException("Use distinct fields and at most 30 reusable attributes.");
            var inventory=configuration.Inventory;
            if (inventory.StockUnit is not ("PIECE" or "PAIR" or "SET" or "BOX") || inventory.TrackingMethod is not ("QUANTITY" or "SERIAL") || inventory.ReorderLevel is < 0 or > 999999999 || inventory.EnableExpiryTracking)
                throw new ValidationException("Choose a supported stock unit, standard/serial tracking and valid reorder level. Batch/expiry tracking is not enabled.");
            var hierarchy=(await repository.GetAllAsync()).ToList();
            var categoryProfile = scope?.BusinessProfileCode ?? hierarchy.FirstOrDefault()?.BusinessProfileCode;
            // Empty profiles are checked by the SQL guard against the actual tenant profile.
            if (categoryProfile is not null and not "ELECTRICAL" && (inventory.TrackingMethod=="SERIAL" || inventory.EnableWarranty))
                throw new ValidationException("Serial tracking and warranty are available in the Electrical setup.");
            foreach(var attribute in configuration.Attributes)
            {
                schema.Attributes.RemoveAll(a=>a.Code==attribute.Code);schema.Attributes.Add(attribute);
            }
            var targetId=id??int.MaxValue;
            schema.Categories.RemoveAll(c=>c.CategoryId==targetId);
            schema.Categories.Add(new(){CategoryId=targetId,Fields=configuration.Fields});
            CatalogSchemaValidator.ValidateDefinition(schema,schema.Categories.Select(c=>c.CategoryId).ToHashSet());
            hierarchy.RemoveAll(c=>c.CategoryId==targetId);
            hierarchy.Add(new(){CategoryId=targetId,CategoryName=request.CategoryName,ParentCategoryId=request.ParentCategoryId,BusinessProfileCode=categoryProfile??"ELECTRICAL",InheritsParentFields=id is null|| (await repository.GetByIdAsync(id.Value))?.InheritsParentFields!=false});
            CategoryFieldInheritance.Resolve(schema,hierarchy);
            var effective=CategoryFieldInheritance.Fields(schema,targetId);
            if(inventory.EnableVehicleCompatibility&&!effective.Any(f=>f.Code=="vehicle_compatibility") || inventory.RequireManufacturerPartNumber&&!effective.Any(f=>f.Code=="manufacturer_part_number"&&f.Required))
                throw new ValidationException("Add the required vehicle compatibility/manufacturer part field before enabling that inventory rule.");
        }
        if (!request.CategoryName.Any(char.IsLetterOrDigit))
        {
            throw new ValidationException("Enter a meaningful category name.");
        }

        var all = (await repository.GetAllAsync()).ToDictionary(c => c.CategoryId);
        if(request.SkuPrefix is not null && all.Values.Any(c => c.CategoryId != id && string.Equals(c.SkuPrefix,request.SkuPrefix,StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("SKU prefix already exists. Choose another prefix.");
        if (all.Values.Any(c => c.CategoryId != id && c.ParentCategoryId == request.ParentCategoryId && string.Equals(Regex.Replace(c.CategoryName.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " "), request.CategoryName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException(request.ParentCategoryId is null
                ? "A parent category with this name already exists."
                : "A subcategory with this name already exists under the selected parent.");
        }

        if (request.ParentCategoryId is int parent && (!all.TryGetValue(parent, out var p) || p.ParentCategoryId is not null || (!p.IsActive && (id is null || (await repository.GetByIdAsync(id.Value))?.ParentCategoryId != parent))))
        {
            throw new ValidationException("Choose an active top-level parent category from this shop.");
        }

        var candidate = ToEntity(request);
        candidate.CategoryId = id ?? -1;
        all[candidate.CategoryId] = candidate;
        foreach (var category in all.Values)
        {
            var visited = new HashSet<int>();
            var current = category;
            while (true)
            {
                if (!visited.Add(current.CategoryId))
                {
                    throw new ValidationException("A category cannot be its own parent or create a circular hierarchy.");
                }

                if (visited.Count > 2)
                {
                    throw new ValidationException("Only Parent Category and Subcategory are supported. A subcategory cannot have children.");
                }

                if (current.ParentCategoryId is not int parentId || !all.TryGetValue(parentId, out current!))
                {
                    break;
                }
            }
        }
    }

    private static Category ToEntity(CreateCategoryRequest request)
    {
        return new()
        {
            CategoryName = request.CategoryName.Trim(),
            SkuPrefix = request.SkuPrefix,
            CategoryCode=request.CategoryCode??string.Empty,
            DisplayOrder=request.DisplayOrder,
            ParentCategoryId = request.ParentCategoryId,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = request.IsActive,
            ConfigurationJson = request.Configuration is null ? null : JsonSerializer.Serialize(request.Configuration,new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }

    private static CategoryResponse Map(Category entity)
    {
        return new()
        {
            CategoryId = entity.CategoryId,
            SkuPrefix = entity.SkuPrefix,
            SkuPrefixUsed = entity.SkuPrefixUsed,
            DisplayOrder=entity.DisplayOrder??0,
            RegistrationDefaults = entity.RegistrationDefaultsJson is null ? null : JsonSerializer.Deserialize<CategoryInventoryDefaults>(entity.RegistrationDefaultsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ProductCount = entity.ProductCount,
            InheritsParentFields = entity.InheritsParentFields,
            BusinessProfileCode = entity.BusinessProfileCode,
            CategoryCode = entity.CategoryCode,
            ParentCategoryId = entity.ParentCategoryId,
            CategoryName = entity.CategoryName,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }

    private static CategoryResponse MapWithPath(Category entity, Dictionary<int, Category> all, Dictionary<int,List<Category>>? children=null)
    {
        var response = Map(entity);
        response.IsEffectivelyActive=entity.IsActive;
        var names = new List<string>
        {
            entity.CategoryName
        };
        var seen = new HashSet<int>
        {
            entity.CategoryId
        };
        var parent = entity.ParentCategoryId;
        if (parent is int p && all.TryGetValue(p, out var immediate))
        {
            response.ParentCategoryName = immediate.CategoryName;
        }

        while (parent is int id && seen.Add(id) && all.TryGetValue(id, out var ancestor))
        {
            response.IsEffectivelyActive &= ancestor.IsActive;
            names.Insert(0, ancestor.CategoryName);
            parent = ancestor.ParentCategoryId;
        }

        if(parent is not null)response.IsEffectivelyActive=false;
        response.CategoryPath = string.Join(" / ", names);
        response.CategoryLevel = names.Count;
        children??=all.Values.Where(c=>c.ParentCategoryId.HasValue).GroupBy(c=>c.ParentCategoryId!.Value).ToDictionary(g=>g.Key,g=>g.ToList());
        response.IsLeaf = !children.ContainsKey(entity.CategoryId);
        var descendants=new HashSet<int>();var pending=new Stack<Category>();pending.Push(entity);
        while(pending.TryPop(out var current)){if(!descendants.Add(current.CategoryId))continue;response.SubtreeProductCount+=current.ProductCount;if(children.TryGetValue(current.CategoryId,out var direct))foreach(var child in direct)pending.Push(child);}
        return response;
    }
}
