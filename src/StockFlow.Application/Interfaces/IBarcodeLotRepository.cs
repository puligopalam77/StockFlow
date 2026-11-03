using StockFlow.Application.DTOs.Purchases;
namespace StockFlow.Application.Interfaces;

public interface IBarcodeLotRepository
{
    Task<IReadOnlyList<BarcodeLotResponse>> ListAsync();
    Task<IReadOnlyList<StockLabelResponse>?> ReceiptLabelsAsync(long receiptId, string? action, int userId);
}

public sealed class BarcodeLotResponse
{
    public long LotId { get; set; }
    public string Kind { get; set; } = "";
    public string LotNumber { get; set; } = "";
    public DateTime ReceivedDate { get; set; }
    public long? PurchaseId { get; set; }
    public string? PurchaseNumber { get; set; }
    public string? SupplierName { get; set; }
    public int Items { get; set; }
    public long Quantity { get; set; }
    public long WithBarcode { get; set; }
    public string BarcodeStatus => WithBarcode == 0 ? "Pending" : WithBarcode >= Quantity ? "Completed" : "Partial";
}
