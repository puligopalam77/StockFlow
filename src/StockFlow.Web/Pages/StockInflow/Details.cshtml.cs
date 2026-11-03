using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.StockInflow;

[Authorize]
public sealed class DetailsModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Source { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public long Id
    {
        get; set;
    }

    public IActionResult OnGet()
    {
        Source = Source.Trim().ToUpperInvariant();
        if (Id > 0 && Source is "PURCHASE" or "OPENING_STOCK" or "CUSTOMER_RETURN" or "STOCK_COUNT" or "ADJUSTMENT")
        {
            return Page();
        }
        else
        {
            return NotFound();
        }
    }
}
