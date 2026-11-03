using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Scanning;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class ScanService : IScanService
{
    private readonly IScanRepository repository;
    // ASP.NET supplies these services when it creates this class.
    public ScanService(IScanRepository repository)
    {
        this.repository = repository;
    }

    public async Task<ScanLookupResponse?> LookupAsync(string code)
    {
        code = code?.Trim() ?? "";
        if (code.Length == 0)
        {
            throw new ValidationException("Barcode or serial number is required.");
        }

        if (code.Length > 200 || code.Any(char.IsControl))
        {
            throw new ValidationException("Invalid barcode or serial number (maximum 200 characters).");
        }

        var result = await repository.FindByCodeAsync(code);
        if (result != null)
        {
            result.IsAvailable = result.IsActive && (result.ProductUnitId.HasValue ? result.UnitStatus == "IN_STOCK" : result.CurrentStock > 0);
        }

        return result;
    }
}
