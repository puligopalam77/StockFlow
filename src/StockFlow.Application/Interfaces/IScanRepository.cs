using StockFlow.Application.DTOs.Scanning;

namespace StockFlow.Application.Interfaces;

public interface IScanRepository
{
    Task<ScanLookupResponse?> FindByCodeAsync(string code);
}
