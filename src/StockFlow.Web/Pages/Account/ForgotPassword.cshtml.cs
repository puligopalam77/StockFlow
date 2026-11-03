using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.Common;

namespace StockFlow.Web.Pages.Account;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ForgotPasswordModel : PageModel
{
    public class PasswordInput
    {
        [Required, StringLength(50)]
        public string Username { get; set; } = "";
        [Required, StringLength(128, MinimumLength = 8)]
        public string Password { get; set; } = "";
        [Required, StringLength(128, MinimumLength = 8), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public void OnGet(string? username)
    {
        var enteredUsername = username?.Trim();
        if (!string.IsNullOrEmpty(enteredUsername) && enteredUsername.Length <= 50)
            Input.Username = enteredUsername;
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();
        try { SecurityRules.Password(Input.Password, Input.ConfirmPassword); }
        catch (ValidationException error)
        {
            ModelState.AddModelError("", error.Message);
            return Page();
        }
        // A username alone is not proof of account ownership. Until recovery
        // verification is configured, only the existing administrator reset can save.
        ModelState.AddModelError("", "Contact your StockFlow administrator to reset your password. Account verification is required before saving a new password.");
        return Page();
    }
}
