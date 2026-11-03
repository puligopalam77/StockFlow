using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StockFlow.Web.Pages.Account;

public class LogoutModel : PageModel
{
    [BindProperty]
    public string? SessionId
    {
        get; set;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (SessionId is null || SessionId != User.FindFirst(StockFlow.Web.Security.IdleSessionService.ClaimType)?.Value)
        {
            return new StatusCodeResult(StatusCodes.Status409Conflict);
        }

        HttpContext.Items["StockFlow.SessionEndReason"] = "LOGOUT";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }
}
