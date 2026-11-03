using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Products;

public sealed class UnitIdentifierReservation
{
    [Range(1,long.MaxValue)] public long VariantId { get; set; }
    public Guid RequestId { get; set; }
    [Range(1,10000)] public int Count { get; set; }
    [Required] public string BusinessProfileCode { get; set; } = "";
    [Range(1,long.MaxValue)] public long LocationId { get; set; }
}
