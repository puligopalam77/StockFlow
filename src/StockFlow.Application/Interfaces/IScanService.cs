using StockFlow.Application.DTOs.Scanning;

namespace StockFlow.Application.Interfaces;

public interface IScanService
{
    Task<ScanLookupResponse?> LookupAsync(string code);
}
