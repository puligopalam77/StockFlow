using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.Web.Pages.Account;

public class TwoFactorModel : PageModel
{
    private readonly TwoFactorService service;
    private readonly PermissionContext access;
    private readonly ISecurityRepository repository;
    private readonly StockFlow.Web.ErrorHandling.ErrorLogService errorLog;
    // ASP.NET supplies these services when it creates this class.
    public TwoFactorModel(TwoFactorService service,
PermissionContext access,
ISecurityRepository repository,
StockFlow.Web.ErrorHandling.ErrorLogService errorLog)
    {
        this.service = service;
        this.access = access;
        this.repository = repository;
        this.errorLog = errorLog;
    }

    [BindProperty]
    public string Password { get; set; } = "";

    [BindProperty]
    public string Code { get; set; } = "";
    public bool Enabled => access.User!.TwoFactorEnabled;
    public string? Secret
    {
        get; set;
    }
    public string? QrDataUrl
    {
        get; set;
    }
    public string[] RecoveryCodes { get; set; } = [];

    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
    }

    public async Task<IActionResult> OnPostSetupAsync()
    {
        try
        {
            Secret = await service.Begin(access.User!.UserId, Password);
            var label = Uri.EscapeDataString("StockFlow:" + User.FindFirstValue("TenantId") + ":" + access.User.Username);
            using var data = QRCodeGenerator.GenerateQrCode($"otpauth://totp/{label}?secret={Secret}&issuer=StockFlow&algorithm=SHA1&digits=6&period=30",
QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(data);
            QrDataUrl = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(5));
        }
        catch (ValidationException e)
        {
            errorLog.Record(HttpContext, e, 400);
            ModelState.AddModelError("", e.Message);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync()
    {
        try
        {
            var result = await service.Verify(access.User!.UserId, Code ?? "");
            if (!result.Success)
            {
                ModelState.AddModelError("", "Code is invalid, expired, or already used.");
                return Page();
            }

            var updated = await repository.GetAccessAsync(access.User.UserId);
            var auth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var identity = new ClaimsIdentity(User.Claims.Where(c => c.Type is not ("TwoFactorVerified" or "SecurityVersion")),
CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new("TwoFactorVerified", "true"));
            identity.AddClaim(new("SecurityVersion", updated!.SecurityVersion.ToString()));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), auth.Properties);
            RecoveryCodes = result.RecoveryCodes;
            if (RecoveryCodes.Length > 0)
            {
                return Page();
            }

            return LocalRedirect(updated.MustChangePassword ? "/Account/ChangePassword" : "/Account/Home");
        }
        catch (ValidationException e)
        {
            errorLog.Record(HttpContext, e, 400);
            ModelState.AddModelError("", e.Message);
            return Page();
        }
    }
}
