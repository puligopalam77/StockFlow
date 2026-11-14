using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.Common;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Admin.Stores;

public sealed class CreateModel(BusinessScope scope,PermissionContext access):PageModel
{
    public IActionResult OnGet()=>scope.IsOwner&&access.Can("STORES","CREATE")?Page():Forbid();
}
