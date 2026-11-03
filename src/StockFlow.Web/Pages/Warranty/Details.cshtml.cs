using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Warranty;

[Authorize]
public class DetailsModel : PageModel
{
    public void OnGet()
    {
    }
}
