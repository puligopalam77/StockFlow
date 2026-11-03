namespace StockFlow.Application.DTOs.Products;

public class UpdateProductRequest : CreateProductRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(32,MinimumLength=32)]
    public string? ExpectedEditVersion { get; set; }
    public bool IsActive { get; set; } = true;
}
