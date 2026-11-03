using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.Common;

namespace StockFlow.Web.Pages.Admin;

public class PilotModel : PageModel
{
    private readonly BusinessScope scope;
    public PilotModel(BusinessScope scope)
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
