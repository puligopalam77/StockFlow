using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Account;

public sealed class SessionsModel : PageModel
{
    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
    }
}
