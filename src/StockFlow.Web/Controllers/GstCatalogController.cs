using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Services;

namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/gst/settings/catalog"),TypeFilter(typeof(StockFlow.Web.Filters.GstExceptionFilter))]
public sealed class GstCatalogController(IGstRepository repository,ICompanySettingsRepository company,StockFlow.Application.Common.BusinessScope scope):ControllerBase
{
    public sealed record BillingDefault(string Mode);
    [HttpPost("billing-defaults")] public async Task<IActionResult> Billing(BillingDefault request){await company.SaveTaxModeAsync(request.Mode,scope.UserId);return Ok(new{mode=request.Mode});}
    [HttpGet] public async Task<IActionResult> Catalog([FromQuery]GstCatalogQuery q)=>Ok(await repository.CatalogAsync(q));
    [HttpGet("options")] public async Task<IActionResult> Options()=>Ok(await repository.CatalogOptionsAsync());
    [HttpGet("rules")] public async Task<IActionResult> Rules([FromQuery]GstCatalogQuery q)=>Ok(await repository.TaxRulesAsync(q));
    [HttpPost("rules")] public async Task<IActionResult> Rule(GstTaxRule rule)=>Ok(new{id=await repository.SaveRuleAsync(rule)});
    [HttpPost("rules/review")] public async Task<IActionResult> Review(GstRuleReview request){await repository.ReviewRuleAsync(request.RuleId);return NoContent();}
    [HttpPost("rules/end")] public async Task<IActionResult> End(GstRuleEnd request){await repository.EndRuleAsync(request);return NoContent();}
    [HttpGet("history")] public async Task<IActionResult> History(int productId,long? variantId)=>Ok(await repository.MappingHistoryAsync(productId,variantId));
    [HttpPost("preview")] public async Task<IActionResult> Preview(GstAssignment request)=>Ok(await repository.PreviewAssignmentAsync(request));
    [HttpGet("operations/{id:guid}")] public async Task<IActionResult> Operation(Guid id,int page=1,int pageSize=25,string? status=null)=>Ok(await repository.OperationAsync(id,page,pageSize,status));
    [HttpGet("operations")] public async Task<IActionResult> Operations(int page=1,int pageSize=10)=>Ok(await repository.OperationsAsync(page,pageSize));
    [HttpPost("commit")] public async Task<IActionResult> Commit(GstOperationConfirm request)=>Ok(await repository.CommitAssignmentAsync(request));
    [HttpGet("export")] public async Task<IActionResult> Export([FromQuery]GstCatalogQuery q,string format="csv")=>Download(await repository.ExportMappingsAsync(q),format);
    [HttpPost("export")] public async Task<IActionResult> ExportSelection(GstAssignment request,string format="csv")=>Download(await repository.ExportMappingsAsync(request.Filter,request.Targets),format);
    [HttpGet("template")] public async Task<IActionResult> Template([FromQuery]GstCatalogQuery q,string format="xlsx")
    {
        var file=await repository.ImportTemplateAsync(q);
        return format=="xlsx"?File(GstMappingSpreadsheet.Xlsx(Encoding.UTF8.GetString(file.Content),true),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",Path.ChangeExtension(file.FileName,"xlsx")):Download(file,format);
    }
    [HttpPost("import"),RequestSizeLimit(11*1024*1024),RequestFormLimits(MultipartBodyLengthLimit=11*1024*1024)]
    public async Task<IActionResult> Import(IFormFile file,[FromForm]Guid requestId,[FromForm]bool overrideExisting=false,[FromForm]string? reason=null,[FromForm]bool variants=false)
    {
        if(file is null||file.Length>10*1024*1024)throw new ValidationException("Use a CSV/XLSX file up to 10 MB.");
        using var stream=new MemoryStream();await file.CopyToAsync(stream);List<GstImportRow> rows;
        try{rows=GstMappingSpreadsheet.Read(stream.ToArray(),file.FileName);}catch(Exception e) when(e is InvalidDataException or System.Xml.XmlException or DecoderFallbackException){throw new ValidationException("The uploaded spreadsheet is malformed or unsupported.");}
        return Ok(await repository.PreviewAssignmentAsync(new(){RequestId=requestId,Import=rows,OverrideExisting=overrideExisting,Reason=reason,Filter=new(){Variants=variants}}));
    }
    [HttpGet("operations/{id:guid}/errors")]
    public async Task<IActionResult> Errors(Guid id)
    {
        var first=await repository.OperationAsync(id,1,100);var rows=first.Rows.ToList();for(var p=2;p<=(first.Total+99)/100;p++)rows.AddRange((await repository.OperationAsync(id,p,100)).Rows);
        static string Cell(object? value){var s=value?.ToString()??"";if(s.Length>0&&"=+-@\t\r".Contains(s[0]))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
        var csv="Row,ProductId,VariantId,ProductName,Status,Message\r\n"+string.Join("\r\n",rows.Select(r=>string.Join(',',new object?[]{r.Row,r.ProductId,r.VariantId,r.ProductName,r.Status,r.Message}.Select(Cell))));return File(Encoding.UTF8.GetBytes(csv),"text/csv; charset=utf-8","tax-operation-results.csv");
    }
    private IActionResult Download(GstExportResult file,string format)=>format switch{"csv"=>File(file.Content,"text/csv; charset=utf-8",file.FileName),"xlsx"=>File(GstMappingSpreadsheet.Xlsx(Encoding.UTF8.GetString(file.Content)),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",Path.ChangeExtension(file.FileName,"xlsx")),_=>throw new ValidationException("Choose CSV or XLSX.")};
}
