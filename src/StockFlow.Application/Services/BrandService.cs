using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Brands;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StockFlow.Application.Services;

public class BrandService : IBrandService
{
    private readonly IBrandRepository repository;
    private readonly IProductRepository products;
    private readonly ICategoryRepository categories;
    private readonly StockFlow.Application.Common.BusinessScope? scope;
    // ASP.NET supplies these services when it creates this class.
    public BrandService(IBrandRepository repository, IProductRepository products, ICategoryRepository categories, StockFlow.Application.Common.BusinessScope? scope = null)
    {
        this.repository = repository;
        this.products = products;
        this.categories = categories;
        this.scope = scope;
    }

    public async Task<IEnumerable<BrandResponse>> GetAllAsync(bool activeOnly = false, string? term = null)
    {
        var allCategories=(await categories.GetAllAsync()).ToList();
        return (await repository.GetAllAsync(activeOnly, term)).Select(brand=>MapWithCategories(brand,allCategories));
    }
    public Task<bool> RegistrationDetailsAvailableAsync()=>repository.RegistrationDetailsAvailableAsync();
    public async Task<BrandLogo?> GetLogoAsync(int id){var brand=await repository.GetByIdAsync(id);return brand?.LogoJson is null?null:JsonSerializer.Deserialize<BrandLogo>(brand.LogoJson,new JsonSerializerOptions(JsonSerializerDefaults.Web));}

    public async Task<BrandResponse?> GetByIdAsync(int id)
    {
        if (await repository.GetByIdAsync(id) is { } entity)
        {
            return MapWithCategories(entity,(await categories.GetAllAsync()).ToList());
        }
        else
        {
            return null;
        }
    }

    public async Task<int> CreateAsync(CreateBrandRequest request)
    {
        await ValidateAsync(request, null);
        return await repository.CreateAsync(ToEntity(request));
    }

    public async Task<bool> UpdateAsync(int id, UpdateBrandRequest request)
    {
        if (await repository.GetByIdAsync(id) is not { } existing)
        {
            return false;
        }

        request.OfficialWebsite??=existing.OfficialWebsite;
        await ValidateAsync(request, id);
        if (!request.IsActive)
        {
            await CheckDeactivation(id);
        }

        var entity = ToEntity(request);
        entity.CategoryIds = request.CategoryIds ?? existing.CategoryIds;
        entity.BrandId = id;
        entity.IsActive = request.IsActive;
        return await repository.UpdateAsync(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await CheckDeactivation(id);
        return await repository.DeleteAsync(id);
    }

    private async Task CheckDeactivation(int id)
    {
        if ((await products.GetAllAsync(true)).Any(p => p.BrandId == id))
        {
            throw new ValidationException("This brand contains active products. Reassign or deactivate those products first.");
        }
    }

    private async Task ValidateAsync(CreateBrandRequest request, int? id)
    {
        request.BrandName = Regex.Replace(request.BrandName?.Trim() ?? "",@"\s+"," ");
        request.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        request.BrandCode=string.IsNullOrWhiteSpace(request.BrandCode)?null:request.BrandCode.Trim().ToUpperInvariant();
        request.OfficialWebsite=string.IsNullOrWhiteSpace(request.OfficialWebsite)?null:request.OfficialWebsite.Trim();
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);
        if(request.BrandCode is not null||request.OfficialWebsite is not null||request.Logo is not null||request.RemoveLogo){
            if(!await repository.RegistrationDetailsAvailableAsync())throw new ValidationException("Apply manual migration 058 before saving brand codes, websites or logos.");
            if(request.BrandCode is not null&&(await repository.GetAllAsync()).Any(b=>b.BrandId!=id&&string.Equals(b.BrandCode,request.BrandCode,StringComparison.OrdinalIgnoreCase)))throw new ValidationException("Brand code already exists.");
            if(request.OfficialWebsite is not null&&(!Uri.TryCreate(request.OfficialWebsite,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http")||string.IsNullOrEmpty(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo)))throw new ValidationException("Enter a valid HTTP or HTTPS brand website.");
            if(request.OfficialWebsite is not null)request.OfficialWebsite=new Uri(request.OfficialWebsite).AbsoluteUri;
            if(request.RemoveLogo&&request.Logo is not null)throw new ValidationException("Choose a logo to upload or remove the current logo.");
            request.Logo?.Validate();
        }
        if (request.CategoryIds is { } ids)
        {
            if (ids.Length > 100 || ids.Length == 0 && scope?.CatalogRegistrationEnhancementsAvailable != true || ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Length)
            {
                throw new ValidationException("Select 1 to 100 distinct categories for this brand.");
            }

            foreach (var categoryId in ids)
            {
                if (await categories.GetByIdAsync(categoryId) is not { IsActive: true } && !(id.HasValue&&(await repository.GetByIdAsync(id.Value))?.CategoryIds.Contains(categoryId)==true))
                {
                    throw new ValidationException("Choose active categories in your business for this brand.");
                }
            }

            if (scope?.CatalogRegistrationEnhancementsAvailable != true && id.HasValue && (await products.GetAllAsync(true)).Any(p => p.BrandId == id && !ids.Contains(p.CategoryId)))
            {
                throw new ValidationException("This brand has active products in a category you removed. Reassign those products first.");
            }
        }

        if (!request.BrandName.Any(char.IsLetterOrDigit))
        {
            throw new ValidationException("Enter a meaningful brand name.");
        }

        if (await repository.NameExistsAsync(request.BrandName.Trim(), id))
        {
            throw new ValidationException("Brand name already exists.");
        }
        if (request.Aliases is null || request.Aliases.Length > 20)
            throw new ValidationException("Use at most 20 brand aliases.");
        request.Aliases = request.Aliases.Select(a => Regex.Replace(a?.Trim() ?? "",@"\s+"," ")).ToArray();
        if (request.Aliases.Any(a => a.Length is < 2 or > 100 || a.Any(char.IsControl)) ||
            request.Aliases.Append(request.BrandName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Aliases.Length + 1)
            throw new ValidationException("Brand names and aliases must be distinct, meaningful values of 2 to 100 characters.");
        if (request.Aliases.Length > 0 && scope?.CatalogRegistrationEnhancementsAvailable != true)
            throw new ValidationException("Apply migration 052 manually before saving brand aliases.");
        foreach (var alias in request.Aliases)
            if (await repository.NameExistsAsync(alias, id)) throw new ValidationException("A brand name or alias already uses " + alias + ".");
    }

    private static Brand ToEntity(CreateBrandRequest request)
    {
        return new()
        {
            BrandName = request.BrandName.Trim(),
            BrandCode=request.BrandCode??string.Empty,
            OfficialWebsite=request.OfficialWebsite,
            LogoJson=request.Logo is null?null:JsonSerializer.Serialize(request.Logo,new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            RemoveLogo=request.RemoveLogo,
            Aliases = request.Aliases,
            CategoryIds = request.CategoryIds ?? [],
            Description = request.Description,
            IsActive = request.IsActive
        };
    }

    private static BrandResponse Map(Brand entity)
    {
        return new()
        {
            BrandId = entity.BrandId,
            OfficialWebsite=entity.OfficialWebsite,
            LogoUrl=entity.HasLogo?$"/api/brands/{entity.BrandId}/logo":null,
            ProductCount=entity.ProductCount,
            UpdatedAt=entity.UpdatedAt,
            Aliases = entity.Aliases,
            CategoryIds = entity.CategoryIds,
            BrandCode = entity.BrandCode,
            Description = entity.Description,
            BrandName = entity.BrandName,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };
    }
    private static BrandResponse MapWithCategories(Brand entity,List<Category> categories){
        var result=Map(entity);var ids=entity.CategoryIds.ToHashSet();
        for(var pass=0;pass<categories.Count;pass++)foreach(var category in categories)if(category.ParentCategoryId is int parent&&ids.Contains(parent))ids.Add(category.CategoryId);
        result.ApplicableCategoryIds=ids.ToArray();return result;
    }
}
