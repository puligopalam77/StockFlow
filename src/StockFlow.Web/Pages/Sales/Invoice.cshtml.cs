using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Pages.Sales;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InvoiceModel : PageModel
{
    private readonly IInvoiceService service;
    // ASP.NET supplies these services when it creates this class.
    public InvoiceModel(IInvoiceService service)
    {
        this.service = service;
    }

    public InvoiceResponse Invoice { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var invoice = await service.GetInvoiceAsync(id);
        if (invoice is null)
        {
            return NotFound();
        }

        Invoice = invoice;
        return Page();
    }
}
