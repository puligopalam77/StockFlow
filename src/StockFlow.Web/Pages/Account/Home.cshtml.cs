using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Account;

public class HomeModel : PageModel
{
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public HomeModel(PermissionContext access)
    {
        this.access = access;
    }

    public IActionResult OnGet()
    {
        if (access.User?.Permissions.FirstOrDefault(p => p.CanView && p.Route is not null)?.Route is { } route)
        {
            return LocalRedirect(route);
        }
        else
        {
            return Page();
        }
    }
}
