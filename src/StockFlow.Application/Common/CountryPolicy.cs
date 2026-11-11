using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.Common;

// Current deployment policy; stored historical country values remain authoritative.
public static class CountryPolicy
{
    public const string Name = "India";
    public const string Code = "IN";
    public static bool IsIndia(string? value) => string.Equals(value?.Trim(), Name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value?.Trim(), Code, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value, string? existing = null, bool code = false)
    {
        var canonical = code ? Code : Name;
        if (!string.IsNullOrWhiteSpace(existing) && !IsIndia(existing))
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsIndia(value) && !string.Equals(value.Trim(), existing.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("The saved country cannot be changed.");
            return existing;
        }
        if (string.IsNullOrWhiteSpace(value) || IsIndia(value)) return canonical;
        throw new ValidationException("Only India is supported for new country details.");
    }
}
