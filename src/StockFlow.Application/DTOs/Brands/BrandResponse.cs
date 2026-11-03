namespace StockFlow.Application.DTOs.Brands;

public class BrandResponse
{
    public string? OfficialWebsite { get; set; }
    public string? LogoUrl { get; set; }
    public int ProductCount { get; set; }
    public int[] ApplicableCategoryIds { get; set; } = [];
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
