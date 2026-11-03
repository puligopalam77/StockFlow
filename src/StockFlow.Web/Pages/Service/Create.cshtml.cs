using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Service;

[Authorize]
public class CreateModel : PageModel
{
    public void OnGet()
    {
    }
}
