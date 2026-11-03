using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Customers;
using StockFlow.Application.DTOs.Security;

namespace StockFlow.MasterDataChecks;

internal static class FieldValidationChecks
{
    public static async Task Run(Factory factory)
    {
        var checks = 0;
        void Check(bool valid, string message)
        {
            if (!valid) throw new Exception("FAIL: " + message);
            checks++;
        }

        // Exercise the declared boundaries on every request/settings DTO, including inherited fields.
        foreach (var type in typeof(CreateCustomerRequest).Assembly.GetTypes()
            .Where(type => type.Namespace?.Contains(".DTOs.") == true))
        {
            foreach (var property in type.GetProperties())
            {
                var name = type.Name + "." + property.Name;
                foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>())
                {
                    if (attribute is StringLengthAttribute length)
                    {
                        Check(length.IsValid(new string('a', length.MaximumLength)), name + " accepts maximum length");
                        Check(!length.IsValid(new string('a', length.MaximumLength + 1)), name + " rejects excessive length");
                        if (length.MinimumLength > 0)
                        {
                            Check(length.IsValid(new string('a', length.MinimumLength)), name + " accepts minimum length");
                            Check(!length.IsValid(new string('a', length.MinimumLength - 1)), name + " rejects short text");
                        }
                    }
                    if (attribute is RequiredAttribute required)
                    {
                        Check(!required.IsValid(null), name + " rejects missing value");
                        if (property.PropertyType == typeof(string))
                        {
                            Check(!required.IsValid("   "), name + " rejects whitespace");
                            Check(required.IsValid("Example"), name + " accepts supplied text");
                        }
                    }
                    if (attribute is RangeAttribute range)
                    {
                        var minimum = Convert.ToDecimal(range.Minimum, CultureInfo.InvariantCulture);
                        var maximum = Convert.ToDecimal(range.Maximum, CultureInfo.InvariantCulture);
                        Check(range.IsValid(minimum.ToString(CultureInfo.InvariantCulture)), name + " accepts minimum");
                        Check(range.IsValid(maximum.ToString(CultureInfo.InvariantCulture)), name + " accepts maximum");
                        Check(!range.IsValid((minimum - 1).ToString(CultureInfo.InvariantCulture)), name + " rejects below minimum");
                        var aboveMaximumRejected = false;
                        try { aboveMaximumRejected = !range.IsValid((maximum + 1).ToString(CultureInfo.InvariantCulture)); }
                        catch (OverflowException) { aboveMaximumRejected = true; }
                        Check(aboveMaximumRejected, name + " rejects above maximum");
                    }
                    if (attribute is RegularExpressionAttribute pattern && pattern.Pattern == FieldPatterns.Phone)
                    {
                        Check(pattern.IsValid("0123456789"), name + " preserves leading zero");
                        Check(pattern.IsValid("9876543210"), name + " accepts ten digits");
                        foreach (var invalid in new[] { "123456789", "12345678901", "98765abc10", "+919876543210", "98765 43210", "９８７６５４３２１０" })
                            Check(!pattern.IsValid(invalid), name + " rejects " + invalid);
                        Check(pattern.IsValid(null) && pattern.IsValid(""), name + " permits an omitted optional phone");
                    }
                    else if (attribute is RegularExpressionAttribute format)
                    {
                        var sample = format.Pattern switch
                        {
                            FieldPatterns.Gst => "29ABCDE1234F1Z5",
                            "^[0-9]{4,8}$" => "12345678",
                            "^(PIECE|BOX|PAIR|SET)$" => "PIECE",
                            "^(INTERNAL|MANUFACTURER)$" => "INTERNAL",
                            "^(QUANTITY|SERIAL)$" => "QUANTITY",
                            "^(MANUFACTURER|DEALER|BOTH)$" => "MANUFACTURER",
                            "^[1-9][0-9]{5}$" => "560038",
                            "^[A-Z]{5}[0-9]{4}[A-Z]$" => "ABCDE1234F",
                            "^[A-Za-z0-9{}_-]+$" => "INV-{YYYY}-",
                            "^INR$" => "INR",
                            "^(EXCLUSIVE|INCLUSIVE)$" => "EXCLUSIVE",
                            "^(email|whatsapp|sms)$" => "email",
                            "^[0-9]{6}$" => "123456",
                            _ => throw new Exception("Add a valid test example for " + name + ": " + format.Pattern)
                        };
                        Check(format.IsValid(sample), name + " accepts valid format");
                        Check(!format.IsValid("INVALID VALUE!"), name + " rejects invalid format");
                    }
                    if (attribute is EmailAddressAttribute email)
                    {
                        Check(email.IsValid("shop@example.com"), name + " accepts email");
                        foreach (var invalid in new[] { "invalid", "@example.com", "shop@" })
                            Check(!email.IsValid(invalid), name + " rejects invalid email");
                    }
                    if (attribute is UrlAttribute url)
                    {
                        Check(url.IsValid("https://example.com/photo.jpg"), name + " accepts URL");
                        Check(!url.IsValid("javascript:alert(1)"), name + " rejects invalid URL");
                    }
                    if (attribute is MinLengthAttribute minimumLength)
                    {
                        Check(minimumLength.IsValid(new object[minimumLength.Length]), name + " accepts minimum entries");
                        Check(!minimumLength.IsValid(new object[minimumLength.Length - 1]), name + " rejects too few entries");
                    }
                    if (attribute is MaxLengthAttribute maximumLength)
                    {
                        Check(maximumLength.IsValid(new object[maximumLength.Length]), name + " accepts maximum entries");
                        Check(!maximumLength.IsValid(new object[maximumLength.Length + 1]), name + " rejects too many entries");
                    }
                    if (attribute is CompareAttribute compare)
                    {
                        var instance = Activator.CreateInstance(type)!;
                        type.GetProperty(compare.OtherProperty)!.SetValue(instance, "Example passphrase 123");
                        var context = new ValidationContext(instance) { MemberName = property.Name };
                        Check(compare.GetValidationResult("Example passphrase 123", context) is null, name + " accepts confirmation");
                        Check(compare.GetValidationResult("Different passphrase 123", context) is not null, name + " rejects mismatch");
                    }
                }
            }
        }

        var user = new SaveSecurityUserRequest { FullName = "Example User", Username = "example.user", RoleId = 1, Email = "staff@example.com" };
        foreach (var phone in new[] { "9876543210", "0123456789" })
        {
            user.MobileNumber = phone;
            SecurityRules.User(user, false);
            Check(true, "Staff phone accepts valid input");
        }
        foreach (var phone in new[] { "123456789", "12345678901", "98765abc10", "+919876543210" })
        {
            user.MobileNumber = phone;
            try { SecurityRules.User(user, false); throw new Exception("Invalid staff phone accepted"); }
            catch (ValidationException) { Check(true, "Staff phone rejects invalid input"); }
        }

        user.MobileNumber = "9876543210";
        foreach (var email in new[] { (string?)null, "", " ", "invalid-email" })
        {
            user.Email = email;
            try { SecurityRules.User(user, false); throw new Exception("Invalid staff email accepted"); }
            catch (ValidationException) { Check(true, "Staff email is mandatory and valid"); }
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        foreach (var module in new[] { "customers", "suppliers" })
        {
            foreach (var invalid in new[] { "123456789", "12345678901", "12345abc90", "+919876543210" })
            {
                var body = new JsonObject { [module == "customers" ? "customerName" : "supplierName"] = "Validation Example", ["mobileNumber"] = invalid };
                Check((int)(await client.PostAsJsonAsync("/api/" + module, body)).StatusCode == 400, module + " API rejects invalid phone");
            }
            var valid = new JsonObject { [module == "customers" ? "customerName" : "supplierName"] = "Validation Example", ["mobileNumber"] = "0123456789" };
            Check((await client.PostAsJsonAsync("/api/" + module, valid)).IsSuccessStatusCode, module + " API accepts valid phone");
            var missing = new JsonObject();
            Check((int)(await client.PostAsJsonAsync("/api/" + module, missing)).StatusCode == 400, module + " API rejects blank registration");
        }
        foreach (var (field, invalid) in new[]
        {
            ("customerName", " "), ("customerName", "x"), ("customerName", "12345"),
            ("customerName", new string('a', 151)), ("email", "invalid-email"),
            ("address", new string('a', 1001)), ("gstNumber", "NOT-A-GSTIN")
        })
        {
            var body = new JsonObject { ["customerName"] = "Validation Customer", [field] = invalid };
            Check((int)(await client.PostAsJsonAsync("/api/customers", body)).StatusCode == 400, "Customer API rejects invalid " + field);
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "tests/bin/field-validation-fixtures");
        Directory.CreateDirectory(output);
        foreach (var page in new[] { "/Products", "/Categories", "/Brands", "/Suppliers", "/Customers", "/Admin/Users", "/Admin/Stores", "/Admin/Security", "/Settings/Company", "/Products/Variants", "/Purchases/Create", "/Purchases/Details/1", "/Receiving", "/Sales/Create", "/Sales/Details/1", "/Service/Create", "/Service/Details/1", "/Warranty/Lookup", "/Warranty/Details/1", "/Inventory", "/Transfers", "/Account/ChangePassword", "/Account/Login" })
        {
            var response = await client.GetAsync(page);
            if (!response.IsSuccessStatusCode) throw new Exception("Fixture failed: " + page + " " + await response.Content.ReadAsStringAsync());
            await File.WriteAllTextAsync(Path.Combine(output, page.Trim('/').Replace('/', '-') + ".html"), await response.Content.ReadAsStringAsync());
        }
        Console.WriteLine($"PASS: {checks} server field boundary and API validation checks; 23 browser form fixtures written. No database writes.");
    }
}
