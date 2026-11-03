using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly IAuthService authService;
    private readonly ILogger<LoginModel> logger;
    private readonly StockFlow.Web.ErrorHandling.ErrorLogService errorLog;
    // ASP.NET supplies these services when it creates this class.
    public LoginModel(IAuthService authService,
ILogger<LoginModel> logger,
StockFlow.Web.ErrorHandling.ErrorLogService errorLog)
    {
        this.authService = authService;
        this.logger = logger;
        this.errorLog = errorLog;
    }

    [BindProperty]
    public LoginRequest Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl
    {
        get; set;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }
        else
        {
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        LoginResult result;
        try
        {
            result = await authService.ValidateLoginAsync(Input);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login service unavailable.");
            errorLog.Record(HttpContext, ex, 500);
            ModelState.AddModelError(string.Empty, "Sign-in is temporarily unavailable. Please try again later.");
            return Page();
        }

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorCode == LoginResult.AccountInactive
                ? LoginResult.InactiveMessage : "Invalid username or password.");
            return Page();
        }

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()), new Claim(ClaimTypes.Name, result.Username), new Claim("FullName", result.FullName), new Claim("SecurityVersion", result.SecurityVersion.ToString()), new Claim("TenantId", result.TenantId.ToString()), new Claim("StoreId", result.StoreId.ToString()), new Claim("LocationId", result.LocationId.ToString()), new Claim(ClaimTypes.Role, result.RoleName) },
CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
new ClaimsPrincipal(identity),
new AuthenticationProperties { IsPersistent = Input.RememberMe });
        return LocalRedirect(result.MustChangePassword ? "/Account/ChangePassword" : Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Account/Home");
    }
}
