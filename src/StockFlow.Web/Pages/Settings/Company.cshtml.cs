using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;

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
    public IReadOnlyList<CompanySalesperson> Salespeople { get; private set; } = [];
    private async Task LoadSalespeople() => Salespeople = await repository.GetSalespeopleAsync();

    [TempData]
    public string? Message
    {
        get; set;
    }

    public async Task OnGetAsync()
    {
        Input = await repository.GetAsync();
        await LoadSalespeople();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSalespeople();
        Input.LogoDataUrl = RemoveLogo ? null : (await repository.GetAsync()).LogoDataUrl;
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
                catch (ValidationException ex) { ModelState.AddModelError("LogoUpload", ex.Message); Input.LogoDataUrl = null; }
            }
        }
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
            await repository.SaveAsync(Input, userId);
        }
        catch (ValidationException ex)
        {
            errorLog.Record(HttpContext, ex, 400);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        Message = "Company details saved. Newly completed sales will use these details.";
        return RedirectToPage();
    }
}
