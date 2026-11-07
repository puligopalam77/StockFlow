using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StockFlow.Application.DTOs.Invoices;
namespace StockFlow.Application.Common;

public static class CompanySettingsViews
{
    public const string Business="business", Billing="billing";
    private static readonly string[] BillingFields=[nameof(CompanySettings.InvoicePrefix),nameof(CompanySettings.ReceiptPrefix),nameof(CompanySettings.Currency),nameof(CompanySettings.DefaultTaxMode),nameof(CompanySettings.DefaultPaymentTermsDays),nameof(CompanySettings.DefaultSalespersonId),nameof(CompanySettings.InvoiceFooter),nameof(CompanySettings.TermsAndConditions)];
    private static readonly string[] BusinessFields=[nameof(CompanySettings.CompanyName),nameof(CompanySettings.LegalBusinessName),nameof(CompanySettings.LogoDataUrl),nameof(CompanySettings.Address),nameof(CompanySettings.AddressLine1),nameof(CompanySettings.AddressLine2),nameof(CompanySettings.District),nameof(CompanySettings.City),nameof(CompanySettings.State),nameof(CompanySettings.PinCode),nameof(CompanySettings.MobileNumber),nameof(CompanySettings.AlternatePhone),nameof(CompanySettings.Email),nameof(CompanySettings.Website),nameof(CompanySettings.GstNumber),nameof(CompanySettings.PanNumber)];
    public static bool Valid(string? section)=>section is Business or Billing;
    public static string[] Fields(string section)=>section switch{Business=>BusinessFields.ToArray(),Billing=>BillingFields.ToArray(),_=>throw new ValidationException("Choose Business Information or Billing & Invoice Settings.")};
    public static CompanySettings Merge(CompanySettings saved,CompanySettings input,string section)
    {
        var result=JsonSerializer.Deserialize<CompanySettings>(JsonSerializer.Serialize(saved))!;
        foreach(var name in Fields(section)){var property=typeof(CompanySettings).GetProperty(name)!;property.SetValue(result,property.GetValue(input));}
        return result;
    }
    public static string Version(CompanySettings value,string section)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Fields(section).Select(name=>new{Field=name,Value=typeof(CompanySettings).GetProperty(name)!.GetValue(value)})))));
}
