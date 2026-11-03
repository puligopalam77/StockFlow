using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.Common;

namespace StockFlow.Web.Pages.CashClosing;

public class IndexModel : PageModel
{
    private readonly BusinessScope scope;
    public IndexModel(BusinessScope scope)
    {
        this.scope = scope;
    }
    public IActionResult OnGet()
    {
        if (!scope.CanReviewStores)
            return StatusCode(403);
        return Page();
    }
}
