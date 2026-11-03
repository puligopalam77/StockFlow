using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Reports;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("Activity", new { fromReports = 1 });
    }
}
