using System.Text.Json;
using StockFlow.Application.DTOs.Crm;
namespace StockFlow.Application.Interfaces;
public interface ICrmRepository
{
    Task<JsonElement> CustomersAsync(CrmQuery query);
    Task<JsonElement?> CustomerAsync(int id,int page,int pageSize);
    Task LinkCustomerAsync(int id);
    Task NoteAsync(int id,CrmNoteRequest request);
    Task PreferencesAsync(int id,CrmPreferencesRequest request);
    Task<JsonElement> EnquiriesAsync(CrmQuery query);
    Task<JsonElement?> EnquiryAsync(long id);
    Task<long> SaveAsync(long? id,CrmEnquiryRequest request);
    Task FollowUpAsync(long id,CrmFollowUpRequest request);
    Task<JsonElement> OptionsAsync(string? term);
    Task<JsonElement> DashboardAsync();
}
