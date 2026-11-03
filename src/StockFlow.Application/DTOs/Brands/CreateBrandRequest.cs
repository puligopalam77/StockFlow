using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Brands;

public class CreateBrandRequest
{
    public bool IsActive { get; set; } = true;
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9-]{1,19}$")]
    public string? BrandCode { get; set; }
    [StringLength(2048)]
    public string? OfficialWebsite { get; set; }
    public BrandLogo? Logo { get; set; }
    public bool RemoveLogo { get; set; }
    public string[] Aliases { get; set; } = [];
    // Omitted by legacy clients; explicit lists assign the brand to these categories.
    public int[]? CategoryIds
    {
        get; set;
    }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string BrandName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description
    {
        get; set;
    }
}
