using StockFlow.Application.DTOs.Notifications;

namespace StockFlow.Application.Interfaces;

public interface INotificationRepository
{
    Task<bool> TestEmailReadyAsync()=>Task.FromResult(false);
    Task<bool> ReadyAsync();
    Task<NotificationSettings> SettingsAsync();
    Task SaveSettingsAsync(NotificationSettings settings);
    Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync();
    Task<IReadOnlyList<ConsentCustomer>> CustomersAsync(string term);
    Task SaveConsentAsync(CustomerConsent consent);
    Task<NotificationFeed> FeedAsync(int page);
    Task<NotificationFeed> FeedPageAsync(int page,int pageSize) => FeedAsync(page);
    Task MarkReadAsync(long? id);
    Task<IReadOnlyList<NotificationJob>> JobsAsync();
    Task<bool> DeliveryReadyAsync() => Task.FromResult(false);
    Task<NotificationDeliverySettings> DeliverySettingsAsync() => Task.FromResult(new NotificationDeliverySettings());
    Task SaveDeliverySettingsAsync(NotificationDeliverySettings settings) => throw new NotSupportedException();
    Task SetSenderVerifiedAsync(string sender) => throw new NotSupportedException();
    Task<IReadOnlyList<EmailDeliveryHistory>> EmailHistoryAsync() => Task.FromResult<IReadOnlyList<EmailDeliveryHistory>>([]);
}
