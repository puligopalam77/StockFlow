using System.ComponentModel.DataAnnotations;
using System.Text.Json;
namespace StockFlow.Application.Interfaces;

public interface IPurchaseChangeRepository
{
 Task<IReadOnlyList<PurchaseChange>> HistoryAsync(long id);
 Task<long> ChangeAsync(long id, PurchaseChangeRequest request, int userId);
}
public sealed class PurchaseChangeRequest
{
 public Guid RequestId { get; set; }
 [Range(0,long.MaxValue)] public long ExpectedVersion { get; set; }
 public long? LocationId { get; set; }
 [Required,RegularExpression("^(DETAILS|ORDER|FINANCIAL)$")] public string Kind { get; set; } = "DETAILS";
 [Required,StringLength(1000,MinimumLength=3)] public string Reason { get; set; } = "";
 [StringLength(100)] public string? SupplierInvoiceNumber { get; set; }
 public DateTime? SupplierInvoiceDate { get; set; }
 [StringLength(4000)] public string? Notes { get; set; }
 [StringLength(100)] public string? InternalReference { get; set; }
 [StringLength(255)] public string? InvoiceAttachmentName { get; set; }
 [StringLength(100)] public string? InvoiceAttachmentContentType { get; set; }
 [MaxLength(5000000)] public byte[]? InvoiceAttachmentData { get; set; }
 [Range(typeof(decimal),"0","9999999999.99")] public decimal DiscountAmount { get; set; }
 [Range(typeof(decimal),"0","9999999999.99")] public decimal FreightAmount { get; set; }
 [Range(typeof(decimal),"0","9999999999.99")] public decimal OtherLandedCost { get; set; }
 [MaxLength(200)] public List<PurchaseChangeLine> Items { get; set; } = [];
}
public sealed class PurchaseChangeLine
{
 public long PurchaseItemId { get; set; }
 [Range(1,int.MaxValue)] public int Quantity { get; set; }
 [Range(typeof(decimal),"0","9999999999.99")] public decimal PurchasePrice { get; set; }
 [Range(typeof(decimal),"0","9999999999.99")] public decimal DiscountAmount { get; set; }
 [Range(0,100)] public decimal TaxPercentage { get; set; }
}
public sealed class PurchaseChange
{
 public long ChangeId { get; set; }
 public string ChangeType { get; set; } = "";
 public string Reason { get; set; } = "";
 public string ChangedBy { get; set; } = "";
 public DateTime ChangedAt { get; set; }
 public decimal AmountDelta { get; set; }
 public long? AdjustmentId { get; set; }
 [System.Text.Json.Serialization.JsonIgnore] public string OldJson { get; set; } = "{}";
 [System.Text.Json.Serialization.JsonIgnore] public string NewJson { get; set; } = "{}";
 public JsonElement OldValues => JsonSerializer.Deserialize<JsonElement>(OldJson);
 public JsonElement NewValues => JsonSerializer.Deserialize<JsonElement>(NewJson);
}
