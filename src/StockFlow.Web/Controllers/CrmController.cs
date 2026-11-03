using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.DTOs.Crm;
using StockFlow.Application.Interfaces;
namespace StockFlow.Web.Controllers;

[ApiController,Authorize,Route("api/crm"),TypeFilter(typeof(StockFlow.Web.Filters.CrmExceptionFilter))]
public sealed class CrmController(ICrmRepository repository) : ControllerBase
{
    [HttpGet("dashboard")] public async Task<IActionResult> Dashboard()=>Ok(await repository.DashboardAsync());
    [HttpGet("customers")] public async Task<IActionResult> Customers([FromQuery]CrmQuery q)=>Ok(await repository.CustomersAsync(q));
    [HttpGet("customers/{id:int}")] public async Task<IActionResult> Customer(int id,int page=1,int pageSize=10)=>await repository.CustomerAsync(id,page,pageSize) is {} value?Ok(value):NotFound();
    [HttpPost("customers/{id:int}/link")] public async Task<IActionResult> Link(int id){await repository.LinkCustomerAsync(id);return NoContent();}
    [HttpPost("customers/{id:int}/notes")] public async Task<IActionResult> Note(int id,CrmNoteRequest r){await repository.NoteAsync(id,r);return NoContent();}
    [HttpPut("customers/{id:int}/preferences")] public async Task<IActionResult> Preferences(int id,CrmPreferencesRequest r){await repository.PreferencesAsync(id,r);return NoContent();}
    [HttpGet("options")] public async Task<IActionResult> Options(string? term)=>Ok(await repository.OptionsAsync(term));
    [HttpGet("enquiries")] public async Task<IActionResult> Enquiries([FromQuery]CrmQuery q)=>Ok(await repository.EnquiriesAsync(q));
    [HttpGet("enquiries/{id:long}")] public async Task<IActionResult> Enquiry(long id)=>await repository.EnquiryAsync(id) is {} value?Ok(value):NotFound();
    [HttpPost("enquiries")] public async Task<IActionResult> Create(CrmEnquiryRequest r)=>Ok(new{id=await repository.SaveAsync(null,r)});
    [HttpPut("enquiries/{id:long}")] public async Task<IActionResult> Edit(long id,CrmEnquiryRequest r)=>Ok(new{id=await repository.SaveAsync(id,r)});
    [HttpPost("enquiries/{id:long}/follow-ups")] public async Task<IActionResult> FollowUp(long id,CrmFollowUpRequest r){await repository.FollowUpAsync(id,r);return NoContent();}
}
