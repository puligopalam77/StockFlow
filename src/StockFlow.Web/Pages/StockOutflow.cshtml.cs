using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages;

[Authorize]
public class StockOutflowModel : PageModel
{
    public void OnGet()
    {
    }
}
