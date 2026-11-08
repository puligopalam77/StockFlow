using System.Text.Json;
namespace StockFlow.Application.Interfaces;
public record ExpenseOperation(Guid OperationId,long ExpectedVersion,JsonElement Body);
public record ExpenseTaxPreview(decimal Amount,DateTime IncurredDate,JsonElement Gst);
public interface IExpenseRepository
{
    Task<JsonElement> OptionsAsync();
    Task<JsonElement> TaxPreviewAsync(ExpenseTaxPreview request);
    Task<JsonElement> ListAsync(string? term,DateTime? from,DateTime? to,int page,int pageSize,Guid? category=null,Guid? payee=null,string? status=null,string? paymentStatus=null,string sort="newest");
    Task<JsonElement> GetAsync(Guid id);
    Task<long> WriteAsync(Guid id,string action,ExpenseOperation operation);
    Task<long> RecordAsync(Guid id,ExpenseOperation operation);
    Task<Guid> MasterAsync(string kind,Guid id,string name,string? mobileNumber=null);
    Task<JsonElement> ReportAsync(DateTime from,DateTime to,bool export=false);
    Task<(string Name,string ContentType,byte[] Data)?> AttachmentAsync(Guid expenseId,Guid attachmentId);
}
