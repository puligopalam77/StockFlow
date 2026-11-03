using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Categories;

public class CreateCategoryRequest
{
    [RegularExpression("^[A-Za-z][A-Za-z0-9]{2,7}$")]
    public string? SkuPrefix { get; set; }
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9_-]{1,29}$")]
    public string? CategoryCode { get; set; }
    [Range(0,99999)]
    public int? DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public CategoryConfiguration? Configuration { get; set; }
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string CategoryName { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? ParentCategoryId
    {
        get; set;
    }

    [StringLength(500)]
    public string? Description
    {
        get; set;
    }
}
