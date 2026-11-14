namespace StockFlow.Domain.Entities;

public class Category
{
    public string? SkuPrefix { get; set; }
    // Write-time expectation only; not a database column.
    public string? ExpectedSkuPrefix { get; set; }
    public bool CheckSkuPrefix { get; set; }
    public bool SkuPrefixUsed { get; set; }
    public int? DisplayOrder { get; set; }
    public string? ConfigurationJson { get; set; }
    public string? RegistrationDefaultsJson { get; set; }
    public int ProductCount { get; set; }
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
