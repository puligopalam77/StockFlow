namespace StockFlow.Application.DTOs.Categories;

public class CategoryResponse
{
    public string? SkuPrefix { get; set; }
    public bool SkuPrefixUsed { get; set; }
    public int DisplayOrder { get; set; }
    public CategoryInventoryDefaults? RegistrationDefaults { get; set; }
    public List<CategoryResponse>? Children { get; set; }
    public int ProductCount { get; set; }
    public int SubtreeProductCount { get; set; }
    public int CategoryLevel { get; set; }
    public bool IsLeaf { get; set; }
    public bool IsEffectivelyActive { get; set; }
    public bool InheritsParentFields { get; set; } = true;
    public string BusinessProfileCode { get; set; } = "ELECTRICAL";
    public int CategoryId
    {
        get; set;
    }
    public string CategoryCode { get; set; } = string.Empty;
    public int? ParentCategoryId
    {
        get; set;
    }
    public string? ParentCategoryName
    {
        get; set;
    }
    public string CategoryPath { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description
    {
        get; set;
    }
    public bool IsActive
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
