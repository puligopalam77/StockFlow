using System.Text.Json;
namespace StockFlow.Application.Interfaces;
public record ExpenseOperation(Guid OperationId,long ExpectedVersion,JsonElement Body);
public interface IExpenseRepository
{
    Task<JsonElement> OptionsAsync();
    Task<JsonElement> ListAsync(string? term,DateTime? from,DateTime? to,int page,int pageSize);
    Task<JsonElement> GetAsync(Guid id);
    Task<long> WriteAsync(Guid id,string action,ExpenseOperation operation);
    Task<Guid> MasterAsync(string kind,Guid id,string name);
    Task<JsonElement> ReportAsync(DateTime from,DateTime to,bool export=false);
    Task<(string Name,string ContentType,byte[] Data)?> AttachmentAsync(Guid expenseId,Guid attachmentId);
}
