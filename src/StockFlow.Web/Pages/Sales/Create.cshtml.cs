using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Sales;

[Authorize]
public class CreateModel : PageModel
{
    private readonly StockFlow.Application.Interfaces.ICompanySettingsRepository settings;
    public StockFlow.Application.DTOs.Invoices.CompanySettings Company { get; private set; } = new();
    public CreateModel(StockFlow.Application.Interfaces.ICompanySettingsRepository settings) => this.settings = settings;
    public async Task OnGetAsync()
    {
        Company = await settings.GetAsync();
    }
}
