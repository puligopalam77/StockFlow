using StockFlow.Application.DTOs.Products;

namespace StockFlow.Application.Interfaces;

public interface IVariantRepository
{
    Task<IEnumerable<VariantResponse>> FindIdentityConflictsAsync(string[] skus,string[] barcodes) => GetAsync(null,null,false);
    Task<IReadOnlyList<StockFlow.Application.DTOs.Purchases.StockLabelResponse>> ItemLabelsAsync(long variantId, bool generate, int userId) =>
        throw new System.ComponentModel.DataAnnotations.ValidationException("Unique item labels are unavailable.");
    Task LogItemLabelPrintAsync(long variantId, long[] labelIds, string action, int userId) => Task.CompletedTask;
    Task LogExistingBarcodePrintAsync(long variantId, string barcode, int copies, string action, int userId) =>
        throw new System.ComponentModel.DataAnnotations.ValidationException("Existing barcode printing is unavailable.");
    Task<IEnumerable<VariantResponse>> GetAsync(int? productId, string? term, bool activeOnly);
    async Task<IEnumerable<VariantResponse>> SearchForSaleAsync(string term) => (await GetAsync(null, term, false)).Take(30);
    async Task<IEnumerable<VariantResponse>> SearchForReceiptAsync(string term) => (await GetAsync(null, term, true)).Take(30);
    Task<long> SaveAsync(long? id, VariantRequest request, int userId);
    Task<string> GenerateBarcodeAsync(long id, int userId);
    Task<string?> SaveUnitBarcodeAsync(long unitId, UnitBarcodeRequest request, int userId);
}
