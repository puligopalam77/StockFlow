using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Configuration;

public class IndexModel : PageModel
{
    private readonly PermissionContext access;
    // ASP.NET supplies these services when it creates this class.
    public IndexModel(PermissionContext access)
    {
        this.access = access;
    }

    public IActionResult OnGet()
    {
        foreach (var module in new[]
        {
            "Categories",
            "Brands",
            "Products",
            "Suppliers",
            "Customers"
        }

        )
        {
            if (access.Can(module.ToUpperInvariant()))
            {
                return LocalRedirect("/" + module);
            }
        }

        if (access.Can("COMPANY_SETTINGS")) return LocalRedirect("/Settings/Company");
        return RedirectToPage("/Account/AccessDenied");
    }
}
