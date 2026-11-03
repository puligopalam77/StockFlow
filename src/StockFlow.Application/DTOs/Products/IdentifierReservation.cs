using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Products;
public sealed class IdentifierReservation
{
    [Required] public string BusinessProfileCode { get; set; } = "";
    public bool ReserveParent { get; set; }
    [Range(1,int.MaxValue)] public int CategoryId { get; set; }
    public Guid RequestId { get; set; }
    [StringLength(40)] public string? ParentCode { get; set; }
    public Guid OperationId { get; set; }
    [Range(1,30)] public int Count { get; set; } = 1;
    public int? ProductId { get; set; }
}
