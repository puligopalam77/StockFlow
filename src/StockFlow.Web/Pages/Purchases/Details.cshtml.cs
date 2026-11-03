using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Purchases;

[Authorize]
public class DetailsModel : PageModel
{
    public void OnGet()
    {
    }
}
