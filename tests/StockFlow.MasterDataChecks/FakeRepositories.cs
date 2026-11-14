using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
namespace StockFlow.MasterDataChecks;
sealed class FakeCategoryRepository(IHttpContextAccessor? context = null) : ICategoryRepository
{
    private string Profile => context?.HttpContext?.RequestServices.GetRequiredService<StockFlow.Application.Common.BusinessScope>().BusinessProfileCode ?? "ELECTRICAL";
    private readonly List<Category> rows = [];
    public Task<IEnumerable<Category>> GetAllAsync(bool activeOnly = false, string? term = null) => Task.FromResult(rows.Where(x => x.BusinessProfileCode == Profile && (!activeOnly || x.IsActive) && (string.IsNullOrEmpty(term) || (x.CategoryName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))));
    public Task<Category?> GetByIdAsync(int id) => Task.FromResult(rows.SingleOrDefault(x => x.CategoryId == id && x.BusinessProfileCode == Profile));
    public Task<int> CreateAsync(Category entity) { entity.BusinessProfileCode = Profile; entity.CategoryId = rows.Count + 1; if(string.IsNullOrEmpty(entity.CategoryCode))entity.CategoryCode="CAT"+entity.CategoryId.ToString("D4"); entity.CreatedAt = DateTime.UtcNow; rows.Add(entity); try{ApplyConfiguration(entity);}catch{rows.Remove(entity);throw;} return Task.FromResult(entity.CategoryId); }
    public Task<bool> UpdateAsync(Category entity) { var index = rows.FindIndex(x => x.CategoryId == entity.CategoryId); if (index < 0) return Task.FromResult(false); var previous=rows[index];if(entity.CheckSkuPrefix&&entity.ExpectedSkuPrefix!=previous.SkuPrefix)throw new System.ComponentModel.DataAnnotations.ValidationException("Category prefix changed. Reload the category before saving.");entity.SkuPrefix??=previous.SkuPrefix;entity.SkuPrefixUsed=previous.SkuPrefixUsed;if(string.IsNullOrEmpty(entity.CategoryCode))entity.CategoryCode=previous.CategoryCode;entity.DisplayOrder??=previous.DisplayOrder;entity.BusinessProfileCode=previous.BusinessProfileCode;entity.InheritsParentFields=previous.InheritsParentFields;entity.RegistrationDefaultsJson=previous.RegistrationDefaultsJson;rows[index] = entity;try{ApplyConfiguration(entity);}catch{rows[index]=previous;throw;}return Task.FromResult(true); }
    private void ApplyConfiguration(Category entity)
    {
        if(entity.ConfigurationJson is null)return;
        var json=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var config=System.Text.Json.JsonSerializer.Deserialize<StockFlow.Application.DTOs.Categories.CategoryConfiguration>(entity.ConfigurationJson,json)!;
        var repository=(FakeCatalogSchemaRepository)context!.HttpContext!.RequestServices.GetRequiredService<ICatalogSchemaRepository>();
        var doc=System.Text.Json.JsonSerializer.Deserialize<StockFlow.Application.DTOs.Products.CatalogSchema>(System.Text.Json.JsonSerializer.Serialize(repository.Schema,json),json)!;
        if(config.SchemaVersion!=doc.Version)throw new System.ComponentModel.DataAnnotations.ValidationException("Category settings changed; reload.");
        foreach(var a in config.Attributes){doc.Attributes.RemoveAll(old=>old.Code==a.Code);doc.Attributes.Add(a);}
        doc.Categories.RemoveAll(c=>c.CategoryId==entity.CategoryId);doc.Categories.Add(new(){CategoryId=entity.CategoryId,Fields=config.Fields});
        StockFlow.Application.Services.CatalogSchemaValidator.ValidateDefinition(doc,rows.Where(c=>c.BusinessProfileCode==Profile).Select(c=>c.CategoryId).ToHashSet());
        StockFlow.Application.Services.CategoryFieldInheritance.Resolve(doc,rows.Where(c=>c.BusinessProfileCode==Profile));
        entity.RegistrationDefaultsJson=System.Text.Json.JsonSerializer.Serialize(config.Inventory,json);doc.Version++;repository.Schema=doc;
    }
    public Task<bool> DeleteAsync(int id) { var entity = rows.SingleOrDefault(x => x.CategoryId == id); if (entity is null) return Task.FromResult(false); entity.IsActive = false; return Task.FromResult(true); }
    public Task<bool> NameExistsAsync(string name, int? excludingId = null) => Task.FromResult(rows.Any(x => x.CategoryId != excludingId && string.Equals(x.CategoryName, name, StringComparison.OrdinalIgnoreCase)));
}
sealed class FakeBrandRepository : IBrandRepository
{
    public bool DetailsAvailable {get;set;}
    public Task<bool> RegistrationDetailsAvailableAsync()=>Task.FromResult(DetailsAvailable);
    private readonly List<Brand> rows = [];
    public Task<IEnumerable<Brand>> GetAllAsync(bool activeOnly = false, string? term = null) => Task.FromResult(rows.Where(x => (!activeOnly || x.IsActive) && (string.IsNullOrEmpty(term) || (x.BrandName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))));
    public Task<Brand?> GetByIdAsync(int id) => Task.FromResult(rows.SingleOrDefault(x => x.BrandId == id));
    public Task<int> CreateAsync(Brand entity) { entity.BrandId = rows.Count + 1; if(string.IsNullOrEmpty(entity.BrandCode))entity.BrandCode="BRD"+entity.BrandId.ToString("D4");entity.HasLogo=entity.LogoJson is not null; entity.CreatedAt = DateTime.UtcNow; rows.Add(entity); return Task.FromResult(entity.BrandId); }
    public Task<bool> UpdateAsync(Brand entity) { var index = rows.FindIndex(x => x.BrandId == entity.BrandId); if (index < 0) return Task.FromResult(false); var previous=rows[index];if(string.IsNullOrEmpty(entity.BrandCode))entity.BrandCode=previous.BrandCode;if(entity.RemoveLogo)entity.LogoJson=null;else entity.LogoJson??=previous.LogoJson;entity.HasLogo=entity.LogoJson is not null;entity.ProductCount=previous.ProductCount;rows[index] = entity; return Task.FromResult(true); }
    public Task<bool> DeleteAsync(int id) { var entity = rows.SingleOrDefault(x => x.BrandId == id); if (entity is null) return Task.FromResult(false); entity.IsActive = false; return Task.FromResult(true); }
    public Task<bool> NameExistsAsync(string name, int? excludingId = null) => Task.FromResult(rows.Any(x => x.BrandId != excludingId && (string.Equals(x.BrandName, name, StringComparison.OrdinalIgnoreCase) || x.Aliases.Contains(name,StringComparer.OrdinalIgnoreCase))));
}
sealed class FakeSupplierRepository : ISupplierRepository
{
    public Task<StockFlow.Application.DTOs.Suppliers.SupplierPage<Supplier>> GetPageAsync(string? term, bool? active, int offset, int limit)
    {
        var filtered = rows.Where(x => (!active.HasValue || x.IsActive == active) && (string.IsNullOrWhiteSpace(term) ||
            new[] { x.SupplierName, x.SupplierCode, x.MobileNumber, x.GstNumber }.Any(v => v?.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase) == true)))
            .OrderBy(x => x.SupplierName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SupplierId).ToList();
        return Task.FromResult(new StockFlow.Application.DTOs.Suppliers.SupplierPage<Supplier>(filtered.Skip(offset).Take(limit).ToList(), filtered.Count));
    }
    public Task<bool> SetActiveAsync(int id, bool active)
    {
        var row = rows.SingleOrDefault(x => x.SupplierId == id);
        if (row is null) return Task.FromResult(false);
        row.IsActive = active; return Task.FromResult(true);
    }
    private readonly List<Supplier> rows = [];
    public Task<IEnumerable<Supplier>> GetAllAsync(bool activeOnly = false, string? term = null) => Task.FromResult(rows.Where(x => (!activeOnly || x.IsActive) && (string.IsNullOrEmpty(term) || (x.SupplierName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.ContactPerson?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.MobileNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Address?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.GstNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))));
    public Task<Supplier?> GetByIdAsync(int id) => Task.FromResult(rows.SingleOrDefault(x => x.SupplierId == id));
    public Task<int> CreateAsync(Supplier entity) { entity.SupplierId = rows.Count + 1; entity.CreatedAt = DateTime.UtcNow; rows.Add(entity); return Task.FromResult(entity.SupplierId); }
    public Task<bool> UpdateAsync(Supplier entity) { var index = rows.FindIndex(x => x.SupplierId == entity.SupplierId); if (index < 0) return Task.FromResult(false); rows[index] = entity; return Task.FromResult(true); }
    public Task<bool> DeleteAsync(int id) { var entity = rows.SingleOrDefault(x => x.SupplierId == id); if (entity is null) return Task.FromResult(false); entity.IsActive = false; return Task.FromResult(true); }
}
sealed class FakeCustomerRepository : ICustomerRepository
{
    private readonly List<Customer> rows = [];
    public Task<IEnumerable<Customer>> GetAllAsync(bool activeOnly = false, string? term = null) => Task.FromResult(rows.Where(x => (!activeOnly || x.IsActive) && (string.IsNullOrEmpty(term) || (x.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.MobileNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Address?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.GstNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))));
    public Task<Customer?> GetByIdAsync(int id) => Task.FromResult(rows.SingleOrDefault(x => x.CustomerId == id));
    public Task<int> CreateAsync(Customer entity, bool preventIdentifierDuplicates = false) { if(preventIdentifierDuplicates && rows.Any(c=>(entity.MobileNumber is not null && c.MobileNumber==entity.MobileNumber)||(entity.GstNumber is not null && string.Equals(c.GstNumber,entity.GstNumber,StringComparison.OrdinalIgnoreCase))))throw new System.ComponentModel.DataAnnotations.ValidationException("A customer with this mobile number or GSTIN already exists."); entity.CustomerId = rows.Count + 1; entity.CreatedAt = DateTime.UtcNow; rows.Add(entity); return Task.FromResult(entity.CustomerId); }
    public Task<bool> UpdateAsync(Customer entity) { var index = rows.FindIndex(x => x.CustomerId == entity.CustomerId); if (index < 0) return Task.FromResult(false); rows[index] = entity; return Task.FromResult(true); }
    public Task<bool> DeleteAsync(int id) { var entity = rows.SingleOrDefault(x => x.CustomerId == id); if (entity is null) return Task.FromResult(false); entity.IsActive = false; return Task.FromResult(true); }
}
sealed class FakeProductRepository(IVariantRepository? variants = null, IHttpContextAccessor? context = null) : IProductRepository
{
    public async Task<int> SaveRegistrationAsync(Product entity, List<StockFlow.Application.DTOs.Products.RegistrationVariantRequest> configurations)
    {
        if(entity.ProductId==0)await CreateAsync(entity);else await UpdateAsync(entity);
        foreach(var variant in configurations){variant.ProductId=entity.ProductId;await variants!.SaveAsync(variant.VariantId,variant,1);}
        return entity.ProductId;
    }
    private readonly List<Product> rows = [];
    private readonly Dictionary<int,string> profiles=[];
    public IReadOnlyList<Product> AllRows => rows;
    private bool Visible(Product p) {
        var scope=context?.HttpContext?.RequestServices.GetRequiredService<StockFlow.Application.Common.BusinessScope>();
        return scope?.ProfileCatalogSwitchingAvailable!=true || profiles.GetValueOrDefault(p.ProductId,"ELECTRICAL")==scope.BusinessProfileCode;
    }
    public Task<IEnumerable<Product>> GetAllAsync(bool activeOnly = false, string? term = null) => Task.FromResult(rows.Where(x => Visible(x) && (!activeOnly || x.IsActive) && (string.IsNullOrEmpty(term) || (x.ProductName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Model?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) || (x.Barcode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))));
    public Task<Product?> GetByIdAsync(int id) => Task.FromResult(rows.SingleOrDefault(x => x.ProductId == id && Visible(x)));
    public Task<int> CreateAsync(Product entity) { entity.ProductId = rows.Count + 1; entity.CreatedAt = DateTime.UtcNow; rows.Add(entity); profiles[entity.ProductId]=context?.HttpContext?.RequestServices.GetRequiredService<StockFlow.Application.Common.BusinessScope>().BusinessProfileCode??"ELECTRICAL";return Task.FromResult(entity.ProductId); }
    public Task<bool> UpdateAsync(Product entity) { var index = rows.FindIndex(x => x.ProductId == entity.ProductId); if (index < 0) return Task.FromResult(false); rows[index] = entity; return Task.FromResult(true); }
    public Task<bool> DeleteAsync(int id) { var entity = rows.SingleOrDefault(x => x.ProductId == id); if (entity is null) return Task.FromResult(false); entity.IsActive = false; return Task.FromResult(true); }
    public Task<Product?> GetByBarcodeAsync(string barcode) => Task.FromResult(rows.SingleOrDefault(x => x.Barcode == barcode && x.IsActive));
    public Task<bool> SkuExistsAsync(string sku, int? excludingId = null) => Task.FromResult(rows.Any(x => x.ProductId != excludingId && x.Sku == sku));
    public Task<bool> BarcodeExistsAsync(string barcode, int? excludingId = null) => Task.FromResult(rows.Any(x => x.ProductId != excludingId && x.Barcode == barcode));
}

