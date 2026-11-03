namespace StockFlow.Application.DTOs.Products;

// Attribute codes are stable storage keys in the existing variant attributes JSON.
public sealed class CatalogAttribute
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "TEXT";
    public List<string> Options { get; set; } = [];
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
}
public sealed class CategoryAttributeRule
{
    public int? DefinedOnCategoryId { get; set; }
    public string Code { get; set; } = "";
    public bool Required { get; set; }
    public bool VariantAxis { get; set; }
    public bool Searchable { get; set; } = true;
    public bool Visible { get; set; } = true;
    public string? DefaultValue { get; set; }
}
public sealed class CategoryAttributeSet
{
    public int CategoryId { get; set; }
    public List<CategoryAttributeRule> Fields { get; set; } = [];
}
public sealed class CatalogSchema
{
    public bool CategoryDetailsAvailable { get; set; }
    public bool RegistrationAvailable { get; set; }
    public bool InheritanceAvailable { get; set; }
    public List<CategoryAttributeSet>? EffectiveCategories { get; set; }
    public bool Available { get; set; }
    public long Version { get; set; }
    public List<CatalogAttribute> Attributes { get; set; } = [];
    public List<CategoryAttributeSet> Categories { get; set; } = [];
    public List<int> LockedCategoryIds { get; set; } = [];
}
