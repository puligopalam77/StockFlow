using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Interfaces;
namespace StockFlow.Web.Controllers;
[ApiController,Authorize,Route("api/expenses"),TypeFilter(typeof(Filters.ExpenseExceptionFilter))]
public sealed class ExpensesController(IExpenseRepository repository):ControllerBase
{
    [HttpGet("options")]public async Task<IActionResult> Options()=>Ok(await repository.OptionsAsync());
    [HttpGet]public async Task<IActionResult> List(string? term,DateTime? from,DateTime? to,int page=1,int pageSize=20)=>Ok(await repository.ListAsync(term,from,to,page,pageSize));
    [HttpGet("{id:guid}")]public async Task<IActionResult> Get(Guid id)=>Ok(await repository.GetAsync(id));
    [HttpPost("{id:guid}/{command}")]public async Task<IActionResult> Write(Guid id,string command,ExpenseOperation request)
    {
        var action=command.ToUpperInvariant();
        if(!new[]{"SAVE","POST","CORRECT","PAY","REFUND","REVERSE","CANCEL"}.Contains(action.ToUpperInvariant()))return BadRequest(new{message="Unsupported expense action."});
        return Ok(new{version=await repository.WriteAsync(id,action.ToUpperInvariant(),request)});
    }
    public record MasterRequest(Guid Id,string Name);
    [HttpPost("masters/{kind}")]public async Task<IActionResult> Master(string kind,MasterRequest request)=>Ok(new{id=await repository.MasterAsync(kind.ToUpperInvariant(),request.Id,request.Name)});
    [HttpGet("report")]public async Task<IActionResult> Report(DateTime from,DateTime to)=>Ok(await repository.ReportAsync(from,to));
    [HttpGet("export")]public async Task<IActionResult> Export(DateTime from,DateTime to)
    {
        var report=await repository.ReportAsync(from,to,true);var csv=new StringBuilder("Type,Source ID,Incurred date,Paid date,Status,Category,Payee,Amount,Method,Reference,Store,Location,Profile\r\n");
        string Cell(string? s)=>"\""+((s??"").TrimStart().StartsWith('=')||(s??"").TrimStart().StartsWith('+')||(s??"").TrimStart().StartsWith('-')||(s??"").TrimStart().StartsWith('@')?"'":"")+(s??"").Replace("\"","\"\"")+"\"";
        string Value(JsonElement r,string key)=>r.TryGetProperty(key,out var v)?v.ToString():"";
        foreach(var r in report.GetProperty("expenses").EnumerateArray())csv.AppendLine(string.Join(',',new[]{"EXPENSE",Value(r,"expense_id"),Value(r,"incurred_date"),"",Value(r,"status"),Value(r,"category"),Value(r,"payee"),Value(r,"amount"),"","",report.GetProperty("storeId").ToString(),report.GetProperty("locationId").ToString(),report.GetProperty("profile").ToString()}.Select(Cell)));
        foreach(var r in report.GetProperty("payments").EnumerateArray())csv.AppendLine(string.Join(',',new[]{Value(r,"kind"),Value(r,"expense_id"),"",Value(r,"paid_date"),"","","",Value(r,"amount"),Value(r,"payment_mode"),Value(r,"reference"),report.GetProperty("storeId").ToString(),report.GetProperty("locationId").ToString(),report.GetProperty("profile").ToString()}.Select(Cell)));
        return File(Encoding.UTF8.GetBytes("\uFEFF"+csv),"text/csv; charset=utf-8","operating-expenses.csv");
    }
    [HttpPost("{id:guid}/attachments"),RequestSizeLimit(6*1024*1024)]public async Task<IActionResult> Upload(Guid id,[FromForm]Guid operationId,[FromForm]long expectedVersion,IFormFile file)
    {
        if(file.Length<1||file.Length>5*1024*1024)throw new ValidationException("Attachment must be 1 byte to 5 MB.");
        using var stream=new MemoryStream();await file.CopyToAsync(stream);var bytes=stream.ToArray();
        var type=bytes.AsSpan().StartsWith("%PDF-"u8)?"application/pdf":bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10})?"image/png":bytes.AsSpan().StartsWith(new byte[]{255,216,255})?"image/jpeg":null;
        if(type is null)throw new ValidationException("Upload a PDF, PNG or JPEG receipt.");
        var name=Path.GetFileName(file.FileName.Replace('\\','/'));
        var body=JsonSerializer.SerializeToElement(new{name,contentType=type,data=Convert.ToBase64String(bytes)});
        return Ok(new{version=await repository.WriteAsync(id,"ATTACH",new(operationId,expectedVersion,body))});
    }
    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]public async Task<IActionResult> Download(Guid id,Guid attachmentId)
    {
        var item=await repository.AttachmentAsync(id,attachmentId);if(item is null)return NotFound();
        Response.Headers.CacheControl="no-store";Response.Headers.XContentTypeOptions="nosniff";return File(item.Value.Data,item.Value.ContentType,item.Value.Name);
    }
}
