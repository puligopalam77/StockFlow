using StockFlow.Application.DTOs.Inventory;

namespace StockFlow.Application.Interfaces;

public interface ITransferRepository
{
    Task<IEnumerable<TransferResponse>> ListAsync();
    Task<TransferResponse?> GetAsync(long id);
    Task<IEnumerable<TransferUnit>> DispatchUnitsAsync(long id, long lineId, string? term, int offset);
    Task<object> LocationsAsync();
    Task<long> CreateAsync(CreateTransferRequest request);
    Task<long> ActAsync(long id, string action, TransferActionRequest request);
}
