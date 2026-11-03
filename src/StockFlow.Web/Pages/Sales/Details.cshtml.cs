using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Sales;

[Authorize]
public class DetailsModel : PageModel
{
    public void OnGet()
    {
    }
}
