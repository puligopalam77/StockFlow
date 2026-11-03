using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using StockFlow.Application.DTOs.Notifications;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Configuration;

public sealed class NotificationsModel(INotificationRepository repository, PermissionContext access, IConfiguration configuration, StockFlow.Application.Common.BusinessScope scope, StockFlow.Web.Notifications.INotificationEmailProvider emailProvider, StockFlow.Web.Notifications.NotificationTestService testEmail) : PageModel
{
    [BindProperty,ValidateNever] public NotificationDeliverySettings Delivery { get; set; } = new();
    public bool DeliveryReady { get; private set; }
    public bool EmailServerEnabled => configuration.GetValue<bool>("Notifications:Email:Enabled");
    public bool CanManageWhatsApp => scope.IsOwner && access.Can("COMPANY_SETTINGS","EDIT");
    public IReadOnlyList<EmailDeliveryHistory> Emails { get; private set; }=[];
    [BindProperty,ValidateNever] public NotificationSettings Input { get; set; } = new();
    [BindProperty,ValidateNever] public CustomerConsent Consent { get; set; } = new();
    [BindProperty(SupportsGet=true),StringLength(100)] public string? Term { get; set; }
    [TempData] public string? Message { get; set; }
    public bool Ready { get; private set; }
    public bool WorkerEnabled => configuration.GetValue<bool>("Notifications:WorkerEnabled");
    public IReadOnlyList<NotificationRecipient> Staff { get; private set; } = [];
    public IReadOnlyList<ConsentCustomer> Customers { get; private set; } = [];
    public IReadOnlyList<NotificationJob> Jobs { get; private set; } = [];
    private async Task LoadAsync()
    {
        Ready=await repository.ReadyAsync(); if (!Ready) return;
        Staff=await repository.RecipientsAsync(); Jobs=await repository.JobsAsync();
        DeliveryReady=await repository.DeliveryReadyAsync();
        if(DeliveryReady) { Emails=await repository.EmailHistoryAsync(); if(!string.Equals(Request.Query["handler"],"Delivery",StringComparison.OrdinalIgnoreCase)) Delivery=await repository.DeliverySettingsAsync(); }
        if (access.Can("CUSTOMERS") && !string.IsNullOrWhiteSpace(Term) && Term.Length<=100) Customers=await repository.CustomersAsync(Term);
    }
    public async Task OnGetAsync() { await LoadAsync(); if(Ready) Input=await repository.SettingsAsync(); if(DeliveryReady) Delivery=await repository.DeliverySettingsAsync(); }
    public async Task<IActionResult> OnPostDeliveryAsync()
    {
        if(!access.Can("COMPANY_SETTINGS","EDIT") || (Delivery.DigestEnabled && !access.Can("INVENTORY"))) return Forbid();
        foreach(var key in ModelState.Keys.Where(x=>!x.StartsWith("Delivery.",StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        try
        {
            if(ModelState.IsValid)
            {
                NotificationDeliverySettings.Validate(Delivery);
                if(!await repository.DeliveryReadyAsync()) return StatusCode(503);
                await repository.SaveDeliverySettingsAsync(Delivery);
                Message="Business alert and email settings saved.";return RedirectToPage();
            }
        }
        catch(ValidationException error) { ModelState.AddModelError("",error.Message); }
        await LoadAsync(); if(Ready) Input=await repository.SettingsAsync();return Page();
    }
    public async Task<IActionResult> OnPostVerifySenderAsync()
    {
        if(!access.Can("COMPANY_SETTINGS","EDIT")) return Forbid();
        if(!await repository.DeliveryReadyAsync()) return StatusCode(503);
        var saved=await repository.DeliverySettingsAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var result=await emailProvider.VerifySenderDetailsAsync(scope.TenantId,saved.SenderEmail ?? "",timeout.Token);
            if(result.Verified) await repository.SetSenderVerifiedAsync(saved.SenderEmail ?? "");
            Message=result.Message;
            if(result.Verified && !EmailServerEnabled)
                Message+=" Email delivery is still disabled on the server. Set Notifications:Email:Enabled=true and restart StockFlow before using email.";
        }
        catch(Exception error) when(error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        { Message="Sender service could not be reached. Try verification again."; }
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostTestAsync()
    {
        if(!access.Can("COMPANY_SETTINGS","EDIT")) return Forbid();
        if(!await repository.DeliveryReadyAsync()) return StatusCode(503);
        var saved=await repository.DeliverySettingsAsync();
        var recipient=(saved.AdministratorEmails ?? "").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if(!EmailServerEnabled || !saved.EmailEnabled || !saved.SenderVerified || recipient is null)
        {Message="Save email settings, verify the sender and add an administrator email before testing.";return RedirectToPage();}
        if(!await repository.TestEmailReadyAsync()) {Message="Apply migration 041 before using test notifications.";return RedirectToPage();}
        if(!await testEmail.ReserveAsync(scope.TenantId)) {Message="A test was sent recently or the daily email limit is reached. Please wait before retrying.";return RedirectToPage();}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));var id=Guid.NewGuid();
        var result=await emailProvider.SendAsync(new(0,scope.TenantId,id,id,recipient,"StockFlow notification test","This is a test of your StockFlow notification email settings.",saved.SenderEmail ?? "",saved.SenderName),timeout.Token);
        Message=result.Status=="ACCEPTED"?"Test email accepted by the provider for the first saved administrator. Check their inbox.":"Test email was not confirmed. Check sender and provider configuration before retrying.";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!access.Can("COMPANY_SETTINGS","EDIT")) return Forbid();
        // This form validates notification settings, not the separate customer consent form.
        foreach(var key in ModelState.Keys.Where(x=>!x.StartsWith("Input.",StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        try
        {
            NotificationRule.Validate(Input);
            if (!await repository.ReadyAsync()) return StatusCode(503);
            await repository.SaveSettingsAsync(Input);
            Message="Notification settings saved."; return RedirectToPage();
        }
        catch (ValidationException e) { ModelState.AddModelError("",e.Message); await LoadAsync(); return Page(); }
    }
    public async Task<IActionResult> OnPostConsentAsync()
    {
        if (!access.Can("COMPANY_SETTINGS","EDIT") || !access.Can("CUSTOMERS")) return Forbid();
        foreach(var key in ModelState.Keys.Where(x=>!x.StartsWith("Consent.",StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) { await LoadAsync(); if(Ready) Input=await repository.SettingsAsync(); return Page(); }
        try
        {
            Validator.ValidateObject(Consent,new ValidationContext(Consent),true);
            if (!await repository.ReadyAsync()) return StatusCode(503);
            await repository.SaveConsentAsync(Consent);
            Message="Customer consent saved with an audit record."; return RedirectToPage(new { term=Term });
        }
        catch (ValidationException e) { ModelState.AddModelError("",e.Message); await LoadAsync(); if(Ready) Input=await repository.SettingsAsync(); return Page(); }
    }
}
