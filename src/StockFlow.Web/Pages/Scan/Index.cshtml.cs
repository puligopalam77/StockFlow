using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Scan;

[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
