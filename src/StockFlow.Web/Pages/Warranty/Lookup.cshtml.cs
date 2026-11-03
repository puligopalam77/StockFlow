using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Warranty;

[Authorize]
public class LookupModel : PageModel
{
    public void OnGet()
    {
    }
}
