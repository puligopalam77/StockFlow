using System.ComponentModel.DataAnnotations;
using System.Net.Mail;

namespace StockFlow.Application.DTOs.Notifications;

public sealed class NotificationDeliverySettings
{
    public bool DigestEnabled { get; set; }
    [Range(0,23)] public int DigestHour { get; set; } = 9;
    [Required,StringLength(100)] public string TimeZone { get; set; } = "Asia/Kolkata";
    public bool EmailEnabled { get; set; }
    [StringLength(150)] public string? SenderEmail { get; set; } = "";
    [Required,StringLength(100)] public string SenderName { get; set; } = "StockFlow";
    public bool SenderVerified { get; set; }
    // Optional alert recipients, never the destination for staff login OTP.
    [StringLength(1000)] public string? AdministratorEmails { get; set; } = "";
    [Range(1,300)] public int DailyLimit { get; set; } = 250;
    public bool InvoiceEmails { get; set; }
    public bool RepairEmails { get; set; }
    public bool WarrantyEmails { get; set; }
    public bool FailureAlerts { get; set; }
    public static void Validate(NotificationDeliverySettings input)
    {
        input.SenderEmail=(input.SenderEmail ?? "").Trim();
        input.AdministratorEmails ??= "";
        Validator.ValidateObject(input,new ValidationContext(input),true);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZone,out _)) throw new ValidationException("Choose a valid time zone.");
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(input.TimeZone,out var iana)) input.TimeZone=iana;
        input.SenderEmail=(input.SenderEmail ?? "").Trim();
        if ((input.EmailEnabled || input.SenderEmail.Length>0) && !ValidEmail(input.SenderEmail)) throw new ValidationException("Enter a valid sender email address.");
        var addresses=(input.AdministratorEmails ?? "").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(addresses.Length>5 || addresses.Any(x=>!ValidEmail(x))) throw new ValidationException("Enter up to five administrator emails separated by commas.");
        input.AdministratorEmails=string.Join(',',addresses.Distinct(StringComparer.OrdinalIgnoreCase));
    }
    public static bool ValidEmail(string value)=>MailAddress.TryCreate(value,out var address) && address.Address==value && !value.Contains('\r') && !value.Contains('\n');
}
public sealed record EmailDeliveryHistory(long Id,string Reference,string Recipient,string Status,int Attempts,string? ProviderId,string? Diagnostic,DateTime CreatedAt);
