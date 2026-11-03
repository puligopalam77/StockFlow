namespace StockFlow.Domain.Entities;

public class Brand
{
    public string? OfficialWebsite { get; set; }
    public string? LogoJson { get; set; }
    public bool HasLogo { get; set; }
    public bool RemoveLogo { get; set; }
    public int ProductCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string[] Aliases { get; set; } = [];
    public int BrandId
    {
        get; set;
    }
    public int[] CategoryIds { get; set; } = [];
    public string BrandCode { get; set; } = string.Empty;
    public string? Description
    {
        get; set;
    }
    public string BrandName { get; set; } = string.Empty;
    public bool IsActive
    {
        get; set;
    }
    public DateTime CreatedAt
    {
        get; set;
    }
}
