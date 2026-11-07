using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace StockFlow.Web.Pages.Replenishment;
[Authorize]
public sealed class IndexModel(IConfiguration configuration):PageModel
{
 public IActionResult OnGet()=>configuration.GetValue<bool>("Features:Replenishment")?Page():NotFound();
}
