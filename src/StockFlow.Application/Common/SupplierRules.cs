using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Suppliers;

namespace StockFlow.Application.Common;

public static class SupplierRules
{
    public static readonly string[] Types = ["MANUFACTURER", "DISTRIBUTOR", "WHOLESALER", "DEALER", "IMPORTER", "SERVICE_PROVIDER", "OTHER"];
    public static readonly Dictionary<string, string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures).Select(c => new RegionInfo(c.Name)).Where(r => Regex.IsMatch(r.TwoLetterISORegionName, "^[A-Z]{2}$") && r.TwoLetterISORegionName != "IV")
.GroupBy(r => r.TwoLetterISORegionName)
.ToDictionary(g => g.Key, g => g.First().EnglishName);
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        else
        {
            return value.Trim();
        }
    }

    public static void Validate(CreateSupplierRequest r)
    {
        r.SupplierName = Regex.Replace(r.SupplierName?.Trim() ?? "", @"\s+", " ");
        r.CountryCode = r.CountryCode?.Trim().ToUpperInvariant() ?? "";
        r.SupplierType = Clean(r.SupplierType)?.ToUpperInvariant();
        r.ContactPerson = Clean(r.ContactPerson);
        r.Email = Clean(r.Email);
        r.Address = Clean(r.Address);
        r.State = Clean(r.State);
        r.City = Clean(r.City);
        r.PostalCode = Clean(r.PostalCode)?.ToUpperInvariant();
        r.Notes = Clean(r.Notes);
        r.GstNumber = Clean(r.GstNumber)?.ToUpperInvariant();
        Validator.ValidateObject(r, new ValidationContext(r), true);
        if (!r.SupplierName.Any(char.IsLetter))
        {
            throw new ValidationException("Supplier name must contain meaningful text.");
        }

        if (!Countries.ContainsKey(r.CountryCode))
        {
            throw new ValidationException("Choose a supported country.");
        }

        if (r.SupplierType is not null && !Types.Contains(r.SupplierType))
        {
            throw new ValidationException("Choose a supported supplier type.");
        }

        if (r.ContactPerson is not null && !Regex.IsMatch(r.ContactPerson, @"^[\p{L}\p{M} .,'’\-]{2,100}$"))
        {
            throw new ValidationException("Contact person must be 2–100 characters using letters and name punctuation.");
        }

        r.MobileNumber = Phone(r.MobileNumber, r.CountryCode);
        r.AlternateMobile = Phone(r.AlternateMobile, r.CountryCode);
        if (r.MobileNumber is not null && r.MobileNumber == r.AlternateMobile)
        {
            throw new ValidationException("Alternate mobile must differ from the primary number.");
        }

        if (r.GstNumber is not null)
        {
            if (r.CountryCode != "IN")
            {
                throw new ValidationException("GSTIN is for Indian suppliers. Leave it blank for other countries.");
            }

            if (!Regex.IsMatch(r.GstNumber, @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$"))
            {
                throw new ValidationException("Enter a 15-character GSTIN in the correct format.");
            }

            if (r.State is null)
            {
                throw new ValidationException("State is required when an Indian GSTIN is provided.");
            }
        }

        if (r.PostalCode is not null)
        {
            var pattern = r.CountryCode switch
            {
                "IN" => @"^[1-9][0-9]{5}$",
                "US" => @"^[0-9]{5}(-[0-9]{4})?$",
                "CA" => @"^[A-Z][0-9][A-Z] ?[0-9][A-Z][0-9]$",
                _ => @"^[\p{L}0-9][\p{L}0-9 -]{0,19}$"
            };
            if (!Regex.IsMatch(r.PostalCode, pattern))
            {
                throw new ValidationException("Postal code is not valid for the selected country.");
            }
        }
    }

    private static string? Phone(string? value, string country)
    {
        value = Clean(value);
        if (value is null)
        {
            return null;
        }

        if (!Regex.IsMatch(value, FieldPatterns.Phone))
        {
            throw new ValidationException(FieldPatterns.PhoneMessage);
        }
        // Keep the existing storage format and duplicate matching for Indian suppliers.
        if (country == "IN")
        {
            return "+91" + value;
        }
        return value;
    }
}
