using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Web.Notifications;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Configuration;

public sealed class WhatsAppModel(WhatsAppService service,BusinessScope scope,PermissionContext access,IOptions<WhatsAppOptions> options,IConfiguration configuration):PageModel
{
    public bool Owner=>scope.IsOwner && access.Can("COMPANY_SETTINGS","EDIT");
    public bool Ready {get;private set;}
    public bool Configured=>options.Value.Configured;
    public bool DeliveryEnabled=>options.Value.Enabled && configuration.GetValue<bool>("Notifications:WorkerEnabled");
    public bool CoexistenceEnabled=>options.Value.CoexistenceEnabled;
    public string AppId=>options.Value.AppId;
    public string GraphVersion=>options.Value.GraphVersion;
    public WhatsAppConnection? Connection {get;private set;}
    public IReadOnlyList<WhatsAppTemplate> Templates {get;private set;}=[];
    public IReadOnlyList<WhatsAppDelivery> Deliveries {get;private set;}=[];
    [BindProperty] public WhatsAppPreferences Input {get;set;}=new();
    [TempData] public string? Message {get;set;}
    public async Task<IActionResult> OnGetAsync()
    {
        if(!Owner) return Forbid();
        await Load();
        if(Connection is not null) Input=new(){Enabled=Connection.Enabled,BillingConfirmed=Connection.BillingConfirmed,InvoiceEnabled=Connection.InvoiceEnabled,PaymentEnabled=Connection.PaymentEnabled,DailyLimit=Connection.DailyLimit};
        return Page();
    }
    private async Task Load()
    {
        Ready=await service.Ready();if(!Ready)return;
        Connection=await service.Connection();Templates=await service.Templates();Deliveries=await service.Deliveries();
    }
    public Task<IActionResult> OnPostSaveAsync()=>Act(async()=>
    {
        if(!ModelState.IsValid) throw new ValidationException("Enter a daily send limit between 1 and 10,000.");
        await service.Save(Input);
    },"WhatsApp notification preferences saved.");
    public Task<IActionResult> OnPostSyncAsync(CancellationToken token)=>Act(()=>service.Sync(token),"Template approval statuses refreshed from Meta.");
    public Task<IActionResult> OnPostSubmitAsync(string kind,CancellationToken token)=>Act(()=>service.SubmitTemplate(kind,token),"Template submitted to Meta. Sync approval status after Meta reviews it.");
    public Task<IActionResult> OnPostDisconnectAsync()=>Act(()=>service.Disconnect(),"WhatsApp disconnected in StockFlow. Queued sends will be skipped. You can also remove StockFlow's access in Meta Business Settings.");
    private async Task<IActionResult> Act(Func<Task> action,string success)
    {
        if(!Owner)return Forbid();if(!await service.Ready())return StatusCode(503);
        try{await action();Message=success;return RedirectToPage();}
        catch(ValidationException e){ModelState.AddModelError("",e.Message);}
        catch(Exception e) when(e is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {ModelState.AddModelError("","Meta could not complete this action. Check your connection and retry.");}
        catch(PostgresException e) when(e.SqlState is "23514" or "42501")
        {ModelState.AddModelError("","Check owner access, billing confirmation and approved templates for each selected message type.");}
        await Load();return Page();
    }
}
