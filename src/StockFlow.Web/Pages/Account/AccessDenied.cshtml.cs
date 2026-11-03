using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Account;

public class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
        Response.StatusCode = 403;
    }
}
