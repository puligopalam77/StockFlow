using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/gst"), TypeFilter(typeof(StockFlow.Web.Filters.GstExceptionFilter))]
public sealed partial class GstController(IGstRepository repository) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(bool catalog=true) => Ok(await repository.SettingsAsync(catalog));
    [HttpGet("settings/states")]
    public IActionResult States() => Ok(GstRules.States);
    [HttpGet("settings/readiness")]
    public async Task<IActionResult> Readiness(long registrationId)=>Ok(await repository.SetupReadinessAsync(registrationId));
    [HttpPost("settings/activate")]
    public async Task<IActionResult> Activate(GstActivateSetup request)=>Ok(await repository.ActivateSetupAsync(request));
    [HttpGet("settings/audit")]
    public async Task<IActionResult> Audit([FromQuery]GstAuditQuery query)=>Ok(await repository.AuditAsync(query));
    [HttpPost("settings/businesses")]
    public async Task<IActionResult> Business(GstBusiness request) => Ok(new{id=await repository.SaveBusinessAsync(request)});
    [HttpPost("settings/registrations")]
    public async Task<IActionResult> Registration(GstRegistration request) => Ok(new{id=await repository.SaveRegistrationAsync(request)});
    [HttpPost("settings/mappings")]
    public async Task<IActionResult> Mapping(GstMapping request) => Ok(new{id=await repository.SaveMappingAsync(request)});
    [HttpPost("settings/taxes")]
    public async Task<IActionResult> Tax(GstTaxConfiguration request) => Ok(new{id=await repository.SaveTaxAsync(request)});
    [HttpGet("registrations")]
    public async Task<IActionResult> Registrations() => Ok(await repository.RegistrationsAsync());
    [HttpGet("reports")]
    public async Task<IActionResult> Report([FromQuery] GstReportQuery query) => Ok(await repository.ReportAsync(query));
    [HttpGet("documents/{id:long}")]
    public async Task<IActionResult> Document(long id) => await repository.DocumentAsync(id) is {} document ? Ok(document) : NotFound();
    [HttpPost("documents")]
    public async Task<IActionResult> Record(GstRecordRequest request) => Ok(new{id=await repository.RecordAsync(request)});
    [HttpPost("review")]
    public async Task<IActionResult> Review(GstReviewRequest request){await repository.ReviewAsync(request);return NoContent();}
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] GstReportQuery query){var result=await repository.ExportAsync(query);return File(result.Content,"text/csv; charset=utf-8",result.FileName);}
    [HttpGet("source-options")]
    public async Task<IActionResult> SourceOptions()=>Ok(await repository.SourceOptionsAsync());
    [HttpGet("export-history")]
    public async Task<IActionResult> ExportHistory()=>Ok(await repository.ExportHistoryAsync());
    [HttpGet("documents/{id:long}/reviews")]
    public async Task<IActionResult> Reviews(long id)=>Ok(await repository.ReviewHistoryAsync(id));
}
