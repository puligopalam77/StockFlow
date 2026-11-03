using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace StockFlow.Web.Notifications;

public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string SignupConfigurationId { get; set; } = "";
    public string GraphVersion { get; set; } = "";
    public string WebhookVerifyToken { get; set; } = "";
    // Explicit opt-in only after Meta has enabled this app's Business App onboarding flow.
    public bool CoexistenceEnabled { get; set; }
    public bool Configured => Regex.IsMatch(AppId,"^[0-9]{5,30}$") && AppSecret.Length >= 16
        && Regex.IsMatch(SignupConfigurationId,"^[0-9]{5,30}$") && Regex.IsMatch(GraphVersion,"^v[0-9]{2,3}\\.0$") && WebhookVerifyToken.Length >= 32;
}
public sealed class WhatsAppConnection
{
    public Guid ConnectionId { get; set; }
    public string WabaId { get; set; } = "";
    public string PhoneId { get; set; } = "";
    public string DisplayNumber { get; set; } = "";
    public string BusinessName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool BillingConfirmed { get; set; }
    public bool InvoiceEnabled { get; set; }
    public bool PaymentEnabled { get; set; }
    public int DailyLimit { get; set; } = 100;
    public DateTime? DisconnectedAt { get; set; }
}
public sealed record WhatsAppTemplate(string Event,string TemplateName,string Language,string Body,string Status,DateTime SyncedAt);
public sealed record WhatsAppDelivery(long Id,string Event,string Reference,string Recipient,string Status,int Attempts,string? Diagnostic,DateTime CreatedAt);
public sealed record WhatsAppSecret(Guid ConnectionId,string WabaId,string PhoneId,string ProtectedToken);
public sealed record WhatsAppJob(long Id,long TenantId,Guid ConnectionId,Guid Token,Guid RequestKey,string PhoneId,string ProtectedToken,string Recipient,string TemplateName,string Language,string Parameters);
public sealed record WhatsAppResult(string Status,string? ProviderId=null,string? Diagnostic=null,int DelaySeconds=300);
public sealed class WhatsAppPreferences
{
    public bool Enabled { get; set; }
    public bool BillingConfirmed { get; set; }
    public bool InvoiceEnabled { get; set; }
    public bool PaymentEnabled { get; set; }
    [Range(1,10000)] public int DailyLimit { get; set; }=100;
}
public sealed class WhatsAppSignupCompletion
{
    public Guid Ticket { get; set; }
    [Required,StringLength(4096)] public string Code { get; set; }="";
    [Required,RegularExpression("^[0-9]{5,30}$")] public string WabaId { get; set; }="";
    [Required,RegularExpression("^[0-9]{5,30}$")] public string PhoneId { get; set; }="";
    [RegularExpression("^[0-9]{6}$"),StringLength(6)] public string? RegistrationPin { get; set; }
    public bool BusinessApp { get; set; }
}
public sealed class WhatsAppTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector=provider.CreateProtector("StockFlow.WhatsApp.Credentials.v1");
    public string Protect(long tenant,string phone,string token)=>protector.Protect(JsonSerializer.Serialize(new Envelope(tenant,phone,token)));
    public string Unprotect(long tenant,string phone,string ciphertext)
    {
        var value=JsonSerializer.Deserialize<Envelope>(protector.Unprotect(ciphertext));
        if(value is null || value.Tenant!=tenant || value.Phone!=phone || string.IsNullOrWhiteSpace(value.Token)) throw new CryptographicException("WhatsApp credential scope mismatch.");
        return value.Token;
    }
    private sealed record Envelope(long Tenant,string Phone,string Token);
}
public static class WhatsAppWebhookSecurity
{
    public static bool Verify(ReadOnlySpan<byte> body,string signature,string secret)
    {
        if(secret.Length<16 || !signature.StartsWith("sha256=",StringComparison.Ordinal) || signature.Length!=71) return false;
        try {return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),body),Convert.FromHexString(signature[7..]));}
        catch(FormatException){return false;}
    }
    public static bool IsOptOut(string text)=>text.Trim().ToUpperInvariant() is "STOP" or "UNSUBSCRIBE" or "CANCEL";
}
