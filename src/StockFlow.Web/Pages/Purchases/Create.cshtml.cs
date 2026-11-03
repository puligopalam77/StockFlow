using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Purchases;

[Authorize]
public class CreateModel : PageModel
{
    public void OnGet()
    {
    }
}
