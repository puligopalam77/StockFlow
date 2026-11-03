using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Warranty;

[Authorize]
public class ClaimsModel : PageModel
{
    public void OnGet()
    {
    }
}
