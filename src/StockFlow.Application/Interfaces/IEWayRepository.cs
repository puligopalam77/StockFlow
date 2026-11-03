using System.Text.Json;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Application.Interfaces;

public interface IEWayRepository
{
    Task RequireAsync(string action);
    Task<IReadOnlyList<EWayRegistration>> RegistrationsAsync();
    Task<object> SourcesAsync(string? term, int page, string? type, long? saleId, long? purchaseId);
    Task<JsonElement> SourceAsync(string type, long id, long registrationId);
    Task<EWayPage> ListAsync(string? term, string? status, DateTime? from, DateTime? to, int page, int size, string? sourceType = null, long? registrationId = null);
    Task<EWayDocument> GetAsync(Guid id);
    Task<long> SaveAsync(EWaySave input);
    Task<object> ActAsync(Guid id, EWayAction input, string action);
    Task<object> HistoryAsync(Guid id);
    Task<object> UsageAsync(DateTime from, DateTime to);
}
