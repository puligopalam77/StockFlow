using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
namespace StockFlow.Web.Controllers;
// Reuses existing PostgreSQL error mapping; validation errors remain explicit.
[ApiController,Authorize,Route("api/replenishment"),TypeFilter(typeof(StockFlow.Web.Filters.ExpenseExceptionFilter))]
public sealed class ReplenishmentController(IReplenishmentRepository repository,IConfiguration configuration,StockFlow.Application.Common.BusinessScope scope):ControllerBase,IAsyncActionFilter
{
 [NonAction]public async Task OnActionExecutionAsync(ActionExecutingContext c,ActionExecutionDelegate next){if(!configuration.GetValue<bool>("Features:Replenishment")){c.Result=new NotFoundResult();return;}var result=await next();if(result.Exception is ValidationException or UnauthorizedAccessException){result.Result=new ObjectResult(new{message=result.Exception.Message}){StatusCode=result.Exception is UnauthorizedAccessException?403:400};result.ExceptionHandled=true;}}
 [HttpGet]public async Task<IActionResult> Plan(string? term,string? status,int? supplierId,int days=30,int page=1,int pageSize=10)=>Ok(await repository.PlanAsync(term,status,supplierId,days,page,pageSize));
 [HttpGet("suppliers")]public async Task<IActionResult> Suppliers()=>Ok(await repository.SuppliersAsync());
 [HttpPut("policy")]public async Task<IActionResult> Policy(ReplenishmentPolicy request)=>Ok(new{version=await repository.SavePolicyAsync(request)});
 [HttpPost("draft")]public async Task<IActionResult> Draft(ReplenishmentDraftRequest request)=>Ok(new{purchaseId=await repository.PrepareAsync(request)});
 [HttpGet("export")]public async Task<IActionResult> Export(string? term,string? status,int? supplierId,int days=30)
 {
  // Export pages would race; deliberately bound to a single consistent response.
  var result=await repository.PlanAsync(term,status,supplierId,days,1,100);
  if(result.Total>100)return BadRequest(new{message="Narrow filters to at most 100 variants for a consistent export."});
  static string Csv(object? o){var s=Convert.ToString(o,System.Globalization.CultureInfo.InvariantCulture)??"";var significant=s.TrimStart();if(significant.Length>0&&("=+-@".Contains(significant[0])||significant[0]=='0')||s.StartsWith('\t')||s.StartsWith('\r')||s.StartsWith('\n'))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
  var text=new StringBuilder("Product,Variant,SKU,Location,Ledger Stock,Net Daily Demand,Configured Lead Days,Open PO,Incoming Transfer,Suggested,Status,Cutoff UTC,Policy Version\r\n");
  foreach(var r in result.Rows)text.AppendLine(string.Join(',',new object?[]{r.ProductName,r.VariantName,r.Sku,scope.LocationName,r.Stock,r.DailyDemand,r.Policy?.LeadDays,r.OpenPo,r.IncomingTransfer,r.Suggested,r.Status,result.Cutoff.ToString("O"),r.Policy?.Version}.Select(Csv)));
  return File(Encoding.UTF8.GetBytes(text.ToString()),"text/csv","replenishment.csv");
 }
}
