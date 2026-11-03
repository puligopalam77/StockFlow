using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Account;

public class ChangePasswordModel : PageModel
{
    private readonly ISecurityRepository repository;
    private readonly PermissionContext access;
    private readonly StockFlow.Web.ErrorHandling.ErrorLogService errorLog;
    // ASP.NET supplies these services when it creates this class.
    public ChangePasswordModel(ISecurityRepository repository,
PermissionContext access,
StockFlow.Web.ErrorHandling.ErrorLogService errorLog)
    {
        this.repository = repository;
        this.access = access;
        this.errorLog = errorLog;
    }

    [BindProperty]
    public ChangePasswordRequest Input { get; set; } = new();

    public async Task<IActionResult> OnPostPopupAsync()
    {
        if (!ModelState.IsValid)
        {
            return new JsonResult(new
            {
                message = "Complete all password fields."
            })
            {
                StatusCode = 400
            };
        }

        try
        {
            await repository.ChangePasswordAsync(access.User!.UserId, Input);
        }
        catch (ValidationException ex)
        {
            errorLog.Record(HttpContext, ex, 400);
            return new JsonResult(new
            {
                message = ex.Message
            })
            {
                StatusCode = 400
            };
        }

        HttpContext.Items["StockFlow.SessionEndReason"] = "PASSWORD_CHANGE";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return new JsonResult(new
        {
            redirectUrl = Url.Page("/Account/Login")
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await repository.ChangePasswordAsync(access.User!.UserId, Input);
        }
        catch (ValidationException ex)
        {
            errorLog.Record(HttpContext, ex, 400);
            ModelState.AddModelError("", ex.Message);
            return Page();
        }

        HttpContext.Items["StockFlow.SessionEndReason"] = "PASSWORD_CHANGE";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Account/Login");
    }
}
