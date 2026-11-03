using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Inventory;

[Authorize]
public class LowStockModel : PageModel
{
    public void OnGet()
    {
    }
}
