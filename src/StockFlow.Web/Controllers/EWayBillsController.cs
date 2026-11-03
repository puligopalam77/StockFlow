using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Gst;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Controllers;

[ApiController, Authorize, Route("api/ewaybills"), TypeFilter(typeof(Filters.EWayExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(250000)]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("eway-preparation")]
public sealed class EWayBillsController(IEWayRepository repository, IEWayProvider provider, PermissionContext access) : ControllerBase
{
    [HttpGet("options")]
    public async Task<IActionResult> Options()
    {
        await repository.RequireAsync("VIEW");
        return Ok(new { states = GstRules.States, registrations = await repository.RegistrationsAsync(), rulesVersion = EWayPreparationRules.Version, portal = "https://ewaybillgst.gov.in/",
            provider = new { name = provider.Name, automaticGeneration = false, monthlyStockFlowCap = (int?)null,
                note = "No StockFlow monthly numeric quota. Official operations require an authorized provider; no unlimited provider entitlement is claimed." },
            permissions = new { prepare = access.Can("EWAY_BILLS", "PREPARE"), edit = access.Can("EWAY_BILLS", "EDIT"), validate = access.Can("EWAY_BILLS", "VALIDATE"),
                recordExternal = access.Can("EWAY_BILLS", "RECORD_EXTERNAL"), export = access.Can("EWAY_BILLS", "EXPORT"), usage = access.Can("EWAY_BILLS", "VIEW_USAGE") } });
    }
    [HttpGet("provider")]
    public async Task<IActionResult> Provider()
    {
        await repository.RequireAsync("CONFIGURE_PROVIDER");
        return Ok(new { name = provider.Name, status = "Blocked: authorized integration contract and credentials required",
            sandboxVerified = false, productionVerified = false, supportedOperations = provider.SupportedOperations, storesCredentials = false,
            stockFlowMonthlyCap = (int?)null, providerFees = (decimal?)null });
    }
    [HttpGet("sources")] public async Task<IActionResult> Sources(string? term, int page = 1, string? type = null, long? saleId = null, long? purchaseId = null)
        => Ok(await repository.SourcesAsync(term, page, type, saleId, purchaseId));
    [HttpGet("source")] public async Task<IActionResult> Source(string type, long id, long registrationId) => Ok(await repository.SourceAsync(type, id, registrationId));
    [HttpGet] public async Task<IActionResult> List(string? term, string? status, DateTime? from, DateTime? to, int page = 1, int size = 20, string? sourceType = null, long? registrationId = null)
    {
        var result = await repository.ListAsync(term, status, from, to, page, size, sourceType, registrationId);
        return Ok(new { result.Total, result.Page, result.PageSize, rows = result.Rows.Select(d => {
            var s = JsonSerializer.Deserialize<JsonElement>(d.SourceJson);
            var p = JsonSerializer.Deserialize<EWayPreparation>(d.BodyJson, EWayPreparationRules.Json)!;
            return new { d.DocumentId, d.Version, d.Status, d.ExternalEbn, d.CreatedAt, d.SourceType, d.SourceId, d.RegistrationId,
                sourceNumber = s.GetProperty("number").GetString(), sourceDate = s.GetProperty("date").GetString(),
                party = s.TryGetProperty("partyName", out var party) ? party.GetString() : "Own stock movement",
                total = s.TryGetProperty("total", out var total) ? total.GetDecimal() : (decimal?)null, validUntil = (string?)null,
                fromState = p.DispatchFrom.State, toState = p.ShipTo.State, mode = p.Mode };
        }) });
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var d = await repository.GetAsync(id);
        var source = JsonSerializer.Deserialize<JsonElement>(d.SourceJson);
        var preparation = JsonSerializer.Deserialize<EWayPreparation>(d.BodyJson, EWayPreparationRules.Json)!;
        return Ok(new { document = new { d.DocumentId, d.Version, d.SourceType, d.SourceId, d.RegistrationId, d.Status, d.ExternalEbn, d.ExternalEvidence, d.CreatedAt, d.UpdatedAt },
            source, preparation, validation = EWayPreparationRules.Validate(preparation, source) });
    }
    [HttpPost, HttpPut] public async Task<IActionResult> Save(EWaySave input)
    {
        if ((Request.Method == "POST") != (input.ExpectedVersion == 0)) return BadRequest(new { message = "Use POST for a new preparation and PUT for a saved revision." });
        return Ok(new { version = await repository.SaveAsync(input) });
    }
    [HttpPost("{id:guid}/validate")] public async Task<IActionResult> Validate(Guid id, EWayAction input) => Ok(await repository.ActAsync(id, input, "VALIDATE"));
    [HttpPost("{id:guid}/external")] public async Task<IActionResult> External(Guid id, EWayAction input)
    { await repository.ActAsync(id, input, "RECORD_EXTERNAL"); return Ok(new { status = "EXTERNAL_UNVERIFIED" }); }
    [HttpGet("{id:guid}/history")] public async Task<IActionResult> History(Guid id) => Ok(await repository.HistoryAsync(id));
    [HttpGet("usage")] public async Task<IActionResult> Usage(DateTime from, DateTime to) => Ok(await repository.UsageAsync(from, to));
    [HttpPost("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, EWayAction input)
    {
        var result = JsonSerializer.SerializeToElement(await repository.ActAsync(id, input, "EXPORT"), EWayPreparationRules.Json);
        var lines = new List<string> { "Field,Value", "Document,PREPARATION WORKSHEET - NOT A PORTAL UPLOAD OR OFFICIAL E-WAY BILL" };
        string Cell(string text)
        {
            if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@') || text.StartsWith('\t') || text.StartsWith('\r')) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        void Flatten(string prefix, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) Flatten(prefix + "." + property.Name, property.Value);
            else if (value.ValueKind == JsonValueKind.Array) { var index = 0; foreach (var item in value.EnumerateArray()) Flatten(prefix + "[" + index++ + "]", item); }
            else lines.Add(Cell(prefix) + "," + Cell(value.ToString()));
        }
        Flatten("source", result.GetProperty("source")); Flatten("preparation", result.GetProperty("preparation")); Flatten("validation", result.GetProperty("validation"));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray(), "text/csv; charset=utf-8", "eway-preparation-" + id + ".csv");
    }
}
