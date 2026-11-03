using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Products;
public sealed class VehicleFitment
{
    [Range(1,long.MaxValue)] public long? VehicleId { get; set; }
    [Required,StringLength(60,MinimumLength=1)] public string Manufacturer { get; set; } = "";
    [Required,StringLength(100,MinimumLength=1)] public string Model { get; set; } = "";
    [StringLength(60)] public string? Variant { get; set; }
    [Range(1900,2100)] public int? YearFrom { get; set; }
    [Range(1900,2100)] public int? YearTo { get; set; }
    [StringLength(300)] public string? Notes { get; set; }
}
