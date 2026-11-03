using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.Application.Common;

public static class SecurityRules
{
    // Admin sign-in is temporarily exempt from enrollment and MFA challenges.
    public static bool IsMfaExempt(string? roleName)
    {
        return string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase);
    }

    public static void Password(string? password, string? confirm)
    {
        if (password is null || password.Length is < 8 or > 128 || string.IsNullOrWhiteSpace(password) || password.Distinct().Count() < 4 || new[]
        {
            "password123456",
            "123456789012",
            "qwerty12345678",
            "passwordpassword"
        }.Contains(password.ToLowerInvariant()))
        {
            throw new ValidationException("Use 8–128 characters. Spaces and passphrases are allowed; avoid common or repeated passwords.");
        }

        if (password != confirm)
        {
            throw new ValidationException("Passwords do not match.");
        }
    }

    public static void User(SaveSecurityUserRequest r, bool creating)
    {
        r.FullName = r.FullName?.Trim() ?? "";
        r.Username = r.Username?.Trim() ?? "";
        r.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim();
        r.MobileNumber = string.IsNullOrWhiteSpace(r.MobileNumber) ? null : r.MobileNumber.Trim();
        if (r.Email is null || r.MobileNumber is null)
        {
            throw new ValidationException("Email and mobile number are required for staff accounts.");
        }

        if (r.FullName.Length is < 2 or > 100 || !r.FullName.Any(char.IsLetter) || r.Username.Length is < 3 or > 50 || !System.Text.RegularExpressions.Regex.IsMatch(r.Username, @"^[\p{L}\p{N}_.-]+$") || r.RoleId <= 0 || r.Email?.Length > 150 || r.MobileNumber?.Length > 20 || (r.Email is not null && !new EmailAddressAttribute().IsValid(r.Email)))
        {
            throw new ValidationException("Enter a valid name, username, role, email and mobile number.");
        }

        if (r.MobileNumber is not null && !System.Text.RegularExpressions.Regex.IsMatch(r.MobileNumber, FieldPatterns.Phone))
        {
            throw new ValidationException(FieldPatterns.PhoneMessage);
        }

        if (creating)
        {
            Password(r.Password, r.ConfirmPassword);
        }
        else if (r.Password is not null || r.ConfirmPassword is not null)
        {
            throw new ValidationException("Use Reset Password to change credentials.");
        }

        Permissions(r.Permissions);
    }

    public static void Permissions(List<ModulePermission>? rows)
    {
        if (rows is null || rows.Count > 100 || rows.Any(p => p is null || p.ModuleId <= 0) || rows.Select(p => p.ModuleId).Distinct().Count() != rows.Count)
        {
            throw new ValidationException("Submit one valid permission row per module.");
        }

        if (rows.Any(p => !p.CanView && (p.CanCreate || p.CanEdit || p.CanDelete || p.CanApprove || p.CanExport)))
        {
            throw new ValidationException("View access is required for other actions.");
        }

        foreach (var p in rows.Where(p => p.Actions is not null))
        {
            var actions = p.Actions!;
            if (actions.Distinct().Count() != actions.Length || actions.Any(a => !p.SupportedActions.Contains(a)))
            {
                throw new ValidationException("Unsupported or duplicate module action.");
            }

            if (actions.Length > 0 && !actions.Contains("VIEW"))
            {
                throw new ValidationException("View access is required for other actions.");
            }
        }
    }
}
