using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.DTOs.Notifications;

public sealed class NotificationSettings
{
    public bool Enabled { get; set; }
    public int[] RecipientIds { get; set; } = [];
    public List<NotificationRule> Rules { get; set; } = NotificationRule.Defaults();
}
public sealed class NotificationRule
{
    public string Event { get; set; } = "";
    public bool Enabled { get; set; } = true;
    [Required, StringLength(100, MinimumLength = 2)] public string Title { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 2)] public string Body { get; set; } = "";
    public static List<NotificationRule> Defaults() =>
    [
        new() { Event = "invoice.completed", Title = "Invoice completed", Body = "Invoice {reference} was completed." },
        new() { Event = "receipt.completed", Title = "Stock received", Body = "Receipt {reference} was completed." },
        new() { Event = "repair.received", Title = "Repair received", Body = "Repair {reference} was received." },
        new() { Event = "repair.ready", Title = "Repair ready", Body = "Repair {reference} is ready." },
        new() { Event = "warranty.updated", Title = "Warranty claim updated", Body = "Claim {reference} changed to {status}." }
    ];
    public static void Validate(NotificationSettings settings)
    {
        var allowed = Defaults().Select(x => x.Event).ToHashSet();
        if (settings.Rules.Count != allowed.Count || !settings.Rules.Select(x => x.Event).ToHashSet().SetEquals(allowed)
            || settings.RecipientIds.Length > 200 || settings.RecipientIds.Any(x => x <= 0))
            throw new ValidationException("Choose valid events and staff recipients.");
        foreach (var rule in settings.Rules)
        {
            Validator.ValidateObject(rule, new ValidationContext(rule), true);
            foreach (var token in System.Text.RegularExpressions.Regex.Matches(rule.Title + rule.Body, @"\{[^{}]*\}"))
                if (token.ToString() is not ("{reference}" or "{status}"))
                    throw new ValidationException("Templates support only {reference} and {status}.");
        }
        if (settings.Enabled && settings.RecipientIds.Length == 0)
            throw new ValidationException("Choose at least one staff recipient before enabling notifications.");
    }
}
public sealed record NotificationRecipient(int UserId, string Name, string Role = "Staff");
public sealed record ConsentCustomer(int CustomerId, string Name, string Channel, bool Allowed, string Evidence);
public sealed class CustomerConsent
{
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    [Required,RegularExpression("^(email|whatsapp|sms)$")] public string Channel { get; set; } = "email";
    public bool Allowed { get; set; }
    [Required, StringLength(250, MinimumLength = 5)] public string Evidence { get; set; } = "";
}
public sealed record NotificationItem(long Id, string Title, string Body, string Url, DateTime CreatedAt, DateTime? ReadAt);
public sealed record NotificationFeed(long Unread, long Total, IReadOnlyList<NotificationItem> Items);
public sealed record NotificationJob(long Id, string Event, string Reference, string Status, int Attempts, int Delivered, DateTime CreatedAt, string? LastError);
