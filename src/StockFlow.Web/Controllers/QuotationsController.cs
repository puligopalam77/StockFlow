using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;
namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/quotations"),TypeFilter(typeof(Filters.QuotationExceptionFilter))]
public sealed class QuotationsController(IQuotationRepository repository,ICompanySettingsRepository company,PermissionContext access,IOptions<ShopTimeOptions> time,TimeProvider clock,BusinessScope scope):ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(string? term,string? status,DateTime? from,DateTime? to,int page=1,int pageSize=20)=>Ok(await repository.ListAsync(term,status,from,to,page,pageSize));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id,int? revision)=>Ok(await repository.GetAsync(id,revision));
    [HttpGet("options")] public async Task<IActionResult> Options()
    {
        await repository.RequireAsync("VIEW");var settings=await company.GetAsync();
        return Ok(new{today=time.Value.Today(clock).ToString("yyyy-MM-dd"),pricesIncludeTax=settings.DefaultTaxMode=="INCLUSIVE",
            permissions=new{create=access.Can("QUOTATIONS","CREATE"),edit=access.Can("QUOTATIONS","EDIT"),status=access.Can("QUOTATIONS","STATUS"),convert=access.Can("QUOTATIONS","CONVERT")&&access.Can("SALES","CREATE"),print=access.Can("QUOTATIONS","PRINT"),export=access.Can("QUOTATIONS","EXPORT"),manual=access.Can("SALES","MANUAL_ITEM")}});
    }
    [HttpPost("preview")] public async Task<IActionResult> Preview(CreateSaleRequest request)=>Ok(await repository.PreviewAsync(request));
    [HttpPost] public async Task<IActionResult> Create(SaveQuotationRequest request)
    {
        if(request.ExpectedVersion!=0)return BadRequest(new{message="Use revision update for existing quotations."});
        return Ok(new{version=await repository.SaveAsync(request),quotationId=request.QuotationId});
    }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Revise(Guid id,SaveQuotationRequest request)
    {
        if(request.QuotationId!=id||request.ExpectedVersion<1)return BadRequest(new{message="Quotation revision identity mismatch."});
        return Ok(new{version=await repository.SaveAsync(request),quotationId=id});
    }
    [HttpPost("{id:guid}/state")] public async Task<IActionResult> State(Guid id,QuotationStateRequest request)=>Ok(new{version=await repository.StateAsync(id,request)});
    [HttpPost("{id:guid}/checkout")] public async Task<IActionResult> Checkout(Guid id,QuotationCheckoutRequest request)=>Ok(await repository.CheckoutAsync(id,request));
    [HttpGet("export")] public async Task<IActionResult> Export(string? term,string? status,DateTime? from,DateTime? to)
    {
        await repository.RequireAsync("EXPORT");var rows=new List<QuotationRow>();var page=1;QuotationPage result;
        do{result=await repository.ListAsync(term,status,from,to,page++,100);rows.AddRange(result.Rows);if(result.Total>10000)return BadRequest(new{message="Narrow filters to at most 10,000 quotations for export."});}while(rows.Count<result.Total&&result.Rows.Count>0);
        static string Cell(object? v){var s=Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)??"";if(v is string&&s.TrimStart().Length>0&&"=+-@".Contains(s.TrimStart()[0]))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
        var exported=clock.GetUtcNow().ToString("O");
        var csv=new StringBuilder("Quotation ID,Shop ID,Location ID,Business Profile,Exported UTC,Number,Revision,Customer,Date,Valid Until,Status,Items,Estimated Total,Sale ID\r\n");
        foreach(var r in rows)csv.AppendLine(string.Join(',',new object?[]{r.QuotationId,scope.StoreId,scope.LocationId,scope.BusinessProfileCode,exported,r.QuotationNumber,r.CurrentRevision,r.CustomerName,r.DocumentDate.ToString("yyyy-MM-dd"),r.ValidUntil.ToString("yyyy-MM-dd"),r.Status,r.ItemCount,r.Total,r.SaleId}.Select(Cell)));
        return File(Encoding.UTF8.GetBytes("\uFEFF"+csv),"text/csv","stockflow-quotations.csv");
    }
}
