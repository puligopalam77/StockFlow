using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.Common;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Settings;

// Informational Business Setup page only. Never loads bank records or grants bank access.
[Authorize]
public sealed class BankAccountsSetupModel(BusinessScope scope,PermissionContext access):PageModel
{
    public IActionResult OnGet()=>scope.IsOwner&&access.Can("COMPANY_SETTINGS")?Page():Forbid();
}
