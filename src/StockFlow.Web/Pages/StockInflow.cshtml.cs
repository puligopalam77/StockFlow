using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages;

[Authorize]
public class StockInflowModel : PageModel
{
    public void OnGet()
    {
    }
}
