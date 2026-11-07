using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Common;

namespace StockFlow.Web.Pages.Settings;

[Authorize]
public class CompanyModel : PageModel
{
    private readonly ICompanySettingsRepository repository;
    private readonly StockFlow.Web.ErrorHandling.ErrorLogService errorLog;
    // ASP.NET supplies these services when it creates this class.
    public CompanyModel(ICompanySettingsRepository repository, StockFlow.Web.ErrorHandling.ErrorLogService errorLog)
    {
        this.repository = repository;
        this.errorLog = errorLog;
    }

    [BindProperty]
    public CompanySettings Input { get; set; } = new();
    [BindProperty] public IFormFile? LogoUpload { get; set; }
    [BindProperty] public bool RemoveLogo { get; set; }
    [BindProperty] public bool UseStructuredAddress { get; set; }
    [BindProperty(SupportsGet=true)] public string Section { get; set; }=CompanySettingsViews.Business;
    [BindProperty] public string EditVersion { get; set; }="";
    public bool IsBusiness=>Section==CompanySettingsViews.Business;
    public string Title=>IsBusiness?"Business Information":"Billing & Invoice Settings";
    public string Subtitle=>IsBusiness?"Manage your company identity, contact details, address and branding.":"Manage invoice numbering, billing defaults, printing and checkout discount policies.";
    public CompanyGstIdentity? GstIdentity { get; private set; }
    public IReadOnlyList<CompanySalesperson> Salespeople { get; private set; } = [];
    private async Task LoadSalespeople() => Salespeople = await repository.GetSalespeopleAsync();

    [TempData]
    public string? Message
    {
        get; set;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if(!CompanySettingsViews.Valid(Section))return BadRequest("Choose a valid settings section.");
        ModelState.Remove(nameof(Section));
        Input = await repository.GetAsync();
        EditVersion=CompanySettingsViews.Version(Input,Section);
        UseStructuredAddress=Input.AddressLine1 is not null;
        GstIdentity=await repository.GetGstIdentityAsync();
        await LoadSalespeople();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if(!CompanySettingsViews.Valid(Section))return BadRequest("Choose a valid settings section.");
        // The deterministic legacy default is valid even without a query/form key.
        // Section authorization/validation is explicit above, not MVC's implicit
        // non-nullable-required rule for a missing bound string.
        ModelState.Remove(nameof(Section));
        await LoadSalespeople();
        GstIdentity=await repository.GetGstIdentityAsync();
        var saved=await repository.GetAsync();
        var allowed=CompanySettingsViews.Fields(Section);
        foreach(var key in ModelState.Keys.Where(k=>k.StartsWith("Input.")&&!allowed.Contains(k[6..])).ToArray())ModelState.Remove(key);
        var required=IsBusiness?new[]{"CompanyName","City","State","PinCode","MobileNumber","Email",UseStructuredAddress?"AddressLine1":"Address"}:new[]{"InvoicePrefix","ReceiptPrefix","Currency","DefaultTaxMode"};
        foreach(var name in required)if(!Request.Form.ContainsKey("Input."+name))ModelState.AddModelError("Input."+name,"This field is required. Reload the settings view if it is missing.");
        Input=CompanySettingsViews.Merge(saved,Input,Section);
        if(IsBusiness){
        Input.LogoDataUrl = RemoveLogo ? null : saved.LogoDataUrl;
        if(UseStructuredAddress)
        {
            Input.Address=saved.Address;
            ModelState.Remove("Input.Address");
            if(string.IsNullOrWhiteSpace(Input.AddressLine1))ModelState.AddModelError("Input.AddressLine1","Enter Address Line 1.");
            else
            {
                StockFlow.Application.Common.CompanyProfileRules.NormalizeAddress(Input);
            }
        }
        else if(saved.AddressLine1 is not null)
        {
            // A stale legacy form must not erase structured fields saved by a newer form.
            Input.AddressLine1=saved.AddressLine1;Input.AddressLine2=saved.AddressLine2;Input.District=saved.District;
            StockFlow.Application.Common.CompanyProfileRules.NormalizeAddress(Input);
            ModelState.Remove("Input.Address");UseStructuredAddress=true;
        }
        if (LogoUpload is not null)
        {
            if (LogoUpload.Length is < 8 or > 2000000 || LogoUpload.ContentType is not ("image/png" or "image/jpeg"))
                ModelState.AddModelError("LogoUpload", "Choose a PNG or JPG logo up to 2 MB.");
            else
            {
                using var stream = new MemoryStream();
                await LogoUpload.CopyToAsync(stream);
                Input.LogoDataUrl = $"data:{LogoUpload.ContentType};base64,{Convert.ToBase64String(stream.ToArray())}";
                try { StockFlow.Application.Common.CompanyProfileRules.Validate(Input); }
                catch (ValidationException ex) { ModelState.AddModelError("LogoUpload", ex.Message); Input.LogoDataUrl = RemoveLogo?null:saved.LogoDataUrl; }
            }
        }
        try { StockFlow.Application.Common.CompanyProfileRules.ValidateTaxDisplay(Input,saved); }
        catch(ValidationException ex){ModelState.AddModelError(ex.Value is string field?"Input."+field:string.Empty,ex.Message);}
        }
        else {ModelState.Remove(nameof(LogoUpload));ModelState.Remove(nameof(RemoveLogo));ModelState.Remove(nameof(UseStructuredAddress));}
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            await repository.SaveSectionAsync(Input,Section,EditVersion,userId);
        }
        catch (ValidationException ex)
        {
            errorLog.Record(HttpContext, ex, 400);
            ModelState.AddModelError(ex.Value is string field?"Input."+field:string.Empty, ex.Message);
            return Page();
        }

        Message = IsBusiness?"Business information saved. Existing invoices retain their saved details.":"Billing and invoice settings saved. New documents use these defaults; the active-shop discount policy is saved separately.";
        return RedirectToPage(new{section=Section});
    }
}
