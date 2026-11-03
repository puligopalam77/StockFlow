using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace StockFlow.Web.Pages.Categories;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet(string? mode = null, int? id = null)
    {
        if (mode is not null && !new[] { "create", "edit", "view" }.Contains(mode.ToLowerInvariant())) return NotFound();
        if (mode is not null && mode.ToLowerInvariant() is "edit" or "view" && id is not > 0) return NotFound();
        return Page();
    }
}
