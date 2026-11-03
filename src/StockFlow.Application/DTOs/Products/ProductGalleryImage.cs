namespace StockFlow.Application.DTOs.Products;

public sealed class ProductGalleryImage
{
    public string? VariantName { get; set; }
    public Guid? ImageId { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Data { get; set; }
    public string? Url { get; set; }
    public bool IsLegacy { get; set; }
}
