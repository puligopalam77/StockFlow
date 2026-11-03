using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Products;

namespace StockFlow.Application.Services;

public static class CatalogSchemaValidator
{
    public static void ValidateDefinition(CatalogSchema schema, IReadOnlySet<int> categoryIds)
    {
        if (schema.Attributes is null || schema.Categories is null || schema.Attributes.Count > 30 || schema.Categories.Count > 200)
            throw new ValidationException("Use at most 30 reusable attributes and 200 category configurations per profile.");
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in schema.Attributes)
        {
            if (a is null || a.Code is null || !Regex.IsMatch(a.Code, "^[a-z][a-z0-9_]{0,39}$") || !codes.Add(a.Code) ||
                string.IsNullOrWhiteSpace(a.Label) || a.Label.Length > 80 || a.Label.Any(char.IsControl) ||
                a.Type is not ("TEXT" or "TEXT_LIST" or "NUMBER" or "DATE" or "BOOLEAN" or "SELECT") ||
                a.Options is null || a.Options.Count > 50 || a.Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 100 || o != o.Trim() || o.Any(char.IsControl)) ||
                a.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != a.Options.Count ||
                a.Type == "SELECT" && a.Options.Count == 0 || a.Type != "SELECT" && a.Options.Count != 0 ||
                a.Type != "NUMBER" && (a.Minimum.HasValue || a.Maximum.HasValue) || a.Minimum > a.Maximum)
                throw new ValidationException("Check attribute codes, labels, types, options and numeric limits. Codes must be unique lowercase identifiers.");
        }
        var categories = new HashSet<int>();
        foreach (var c in schema.Categories)
        {
            if (c is null || !categoryIds.Contains(c.CategoryId) || !categories.Add(c.CategoryId) || c.Fields is null || c.Fields.Count > 30 ||
                c.Fields.Any(f => f is null || !codes.Contains(f.Code)) || c.Fields.Select(f => f.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Fields.Count)
                throw new ValidationException("Choose current-profile categories and distinct existing attributes.");
            foreach (var f in c.Fields)
            {
                var a = schema.Attributes.Single(a => a.Code == f.Code);
                if (f.VariantAxis && (a.Type != "SELECT" || !f.Required))
                    throw new ValidationException("Variant axes must be required selection fields.");
                if (!f.Visible && (f.VariantAxis || f.Required && string.IsNullOrWhiteSpace(f.DefaultValue)))
                    throw new ValidationException("Hidden fields cannot create variants. Hidden required fields need a default value.");
                if (f.DefaultValue is not null)
                    ValidateValue(a, f.DefaultValue, f.Required);
            }
            if (c.Fields.Count(f => f.VariantAxis) > 3)
                throw new ValidationException("Use at most three variant axes per category.");
        }
    }

    public static void ValidateValues(CatalogSchema schema, int categoryId, IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string>? customAxes = null)
    {
        var rules = CategoryFieldInheritance.Fields(schema, categoryId);
        if ((rules is null || rules.Count == 0) && customAxes?.Count is not >0) return;
        rules ??= [];
        var identityAxes=rules.Where(r=>r.VariantAxis).Select(r=>r.Code).Concat(customAxes??[]).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        if(identityAxes.Length>3 || identityAxes.Any(code=>!Regex.IsMatch(code,@"^[a-z][a-z0-9_]{0,39}$")) || rules.Any(r=>!r.VariantAxis && identityAxes.Contains(r.Code)))throw new ValidationException("Choose up to three variant attributes; common specifications cannot become variant axes.");
        if (rows.Count is < 1 or > 30 || rows.Any(r => r is null)) throw new ValidationException("This category requires between 1 and 30 item configurations with attribute values.");
        var combinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row is null) throw new ValidationException("Supply attribute values for each item.");
            foreach (var rule in rules)
            {
                var a = schema.Attributes.Single(a => a.Code == rule.Code);
                var value = row.GetValueOrDefault(a.Code)?.Trim() ?? "";
                if (rule.Required && value.Length == 0) throw new ValidationException($"{a.Label} is required.");
                if (value.Length == 0) continue;
                ValidateValue(a, value, rule.Required);
                if (!rule.VariantAxis && rows.Any(r => (r.GetValueOrDefault(a.Code)?.Trim() ?? "") != value))
                    throw new ValidationException($"{a.Label} must be the same across all variants of this product.");
            }
            if(identityAxes.Any(code=>string.IsNullOrWhiteSpace(row.GetValueOrDefault(code))))throw new ValidationException("Choose a value for each variant attribute.");
            if (identityAxes.Length > 0 && !combinations.Add(System.Text.Json.JsonSerializer.Serialize(identityAxes.Select(code => row.GetValueOrDefault(code)?.Trim()).ToArray())))
                throw new ValidationException("Each variant must have a different option combination.");
        }
    }

    private static void ValidateValue(CatalogAttribute a, string value, bool required)
    {
        if (value.Length == 0 && !required) return;
        var valid = value.Length <= (a.Type == "TEXT_LIST" ? 1000 : 100) && a.Type switch
                {
                    "NUMBER" => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) && (!a.Minimum.HasValue || n >= a.Minimum) && (!a.Maximum.HasValue || n <= a.Maximum),
                    "DATE" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                    "BOOLEAN" => value is "true" or "false",
                    "SELECT" => a.Options.Contains(value, StringComparer.Ordinal),
                    "TEXT_LIST" => value.Split(';').Length <= 30 && value.Split(';').All(v => v.Trim().Length is >= 1 and <= 100 && !v.Any(char.IsControl))
                        && value.Split(';').Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.Split(';').Length,
                    _ => !value.Any(char.IsControl)
                };
                if (!valid) throw new ValidationException($"Enter a valid {a.Label} value.");
    }
}
