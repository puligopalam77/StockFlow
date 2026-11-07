using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Suppliers;

public record SupplierPage<T>(IReadOnlyList<T> Items, long Total);

public class SupplierStatusRequest
{
    [Required]
    public bool? IsActive { get; set; }
}
