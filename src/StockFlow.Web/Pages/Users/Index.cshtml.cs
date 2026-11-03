using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Users;

[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
