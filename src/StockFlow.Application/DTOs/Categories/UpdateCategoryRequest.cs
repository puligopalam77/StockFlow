namespace StockFlow.Application.DTOs.Categories;

public class UpdateCategoryRequest : CreateCategoryRequest
{
    [System.ComponentModel.DataAnnotations.RegularExpression("^[A-Z][A-Z0-9]{2,7}$")]
    public string? ExpectedSkuPrefix { get; set; }
}
