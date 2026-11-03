using System.ComponentModel.DataAnnotations;
using Dapper;
using StockFlow.Application.DTOs.Notifications;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public sealed class NotificationRepository(DbConnectionFactory factory) : INotificationRepository
{
    public async Task<bool> TestEmailReadyAsync()
    { await using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<bool>("SELECT to_regprocedure('public.sf_notification_test_reserve(bigint)') IS NOT NULL"); }
    public async Task<bool> DeliveryReadyAsync()
    {
        await using var c=factory.CreateConnection();return await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.notification_delivery_settings') IS NOT NULL");
    }
    public async Task<NotificationDeliverySettings> DeliverySettingsAsync()
    {
        await using var c=factory.CreateConnection();
        var settings=await c.QuerySingleOrDefaultAsync<NotificationDeliverySettings>("""
            SELECT digest_enabled AS DigestEnabled,digest_hour AS DigestHour,time_zone AS TimeZone,email_enabled AS EmailEnabled,
             sender_email AS SenderEmail,sender_name AS SenderName,sender_verified AS SenderVerified,administrator_emails AS AdministratorEmails,
             daily_limit AS DailyLimit,invoice_emails AS InvoiceEmails,repair_emails AS RepairEmails,warranty_emails AS WarrantyEmails,failure_alerts AS FailureAlerts FROM notification_delivery_settings
            """) ?? new();
        return settings;
    }
    public async Task SaveDeliverySettingsAsync(NotificationDeliverySettings settings)
    {
        NotificationDeliverySettings.Validate(settings);
        await using var c=factory.CreateConnection();
        if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_timezone_names WHERE name=@TimeZone)",settings)) throw new ValidationException("Choose an IANA time zone such as Asia/Kolkata.");
        await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await c.ExecuteAsync("""
            INSERT INTO notification_delivery_settings(digest_enabled,digest_hour,time_zone,email_enabled,sender_email,sender_name,administrator_emails,daily_limit,invoice_emails,repair_emails,warranty_emails,failure_alerts)
            VALUES(@DigestEnabled,@DigestHour,@TimeZone,@EmailEnabled,@SenderEmail,@SenderName,@AdministratorEmails,@DailyLimit,@InvoiceEmails,@RepairEmails,@WarrantyEmails,@FailureAlerts)
            ON CONFLICT(tenant_id) DO UPDATE SET digest_enabled=EXCLUDED.digest_enabled,digest_hour=EXCLUDED.digest_hour,time_zone=EXCLUDED.time_zone,email_enabled=EXCLUDED.email_enabled,
             sender_verified=(notification_delivery_settings.sender_verified AND notification_delivery_settings.sender_email=EXCLUDED.sender_email),sender_email=EXCLUDED.sender_email,sender_name=EXCLUDED.sender_name,
             administrator_emails=EXCLUDED.administrator_emails,daily_limit=EXCLUDED.daily_limit,invoice_emails=EXCLUDED.invoice_emails,repair_emails=EXCLUDED.repair_emails,warranty_emails=EXCLUDED.warranty_emails,failure_alerts=EXCLUDED.failure_alerts,updated_by=sf_actor(),updated_at=now()
            """,settings,tx);
        await tx.CommitAsync();
    }
    public async Task SetSenderVerifiedAsync(string sender)
    {
        await using var c=factory.CreateConnection();await c.ExecuteAsync("UPDATE notification_delivery_settings SET sender_verified=true,updated_by=sf_actor(),updated_at=now() WHERE sender_email=@sender",new{sender});
    }
    public async Task<IReadOnlyList<EmailDeliveryHistory>> EmailHistoryAsync()
    {
        await using var c=factory.CreateConnection();return (await c.QueryAsync<EmailDeliveryHistory>("""
            SELECT e.id AS Id,o.reference::text AS Reference,e.recipient::text AS Recipient,e.status::text AS Status,e.attempts AS Attempts,e.provider_id::text AS ProviderId,e.diagnostic::text AS Diagnostic,e.created_at AS CreatedAt
            FROM notification_email_deliveries e JOIN notification_outbox o ON o.tenant_id=e.tenant_id AND o.id=e.outbox_id ORDER BY e.id DESC LIMIT 50
            """)).ToList();
    }
    public async Task<bool> ReadyAsync()
    {
        await using var c = factory.CreateConnection();
        return await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.notifications') IS NOT NULL");
    }
    public async Task<NotificationSettings> SettingsAsync()
    {
        await using var c = factory.CreateConnection();
        var result = new NotificationSettings { Enabled = await c.ExecuteScalarAsync<bool>("SELECT COALESCE((SELECT enabled FROM notification_settings),false)") };
        result.RecipientIds = (await c.QueryAsync<int>("SELECT user_id FROM notification_recipients ORDER BY user_id")).ToArray();
        var saved = (await c.QueryAsync<NotificationRule>("SELECT event AS Event,enabled AS Enabled,title AS Title,body AS Body FROM notification_rules")).ToDictionary(x => x.Event);
        result.Rules = result.Rules.Select(x => saved.GetValueOrDefault(x.Event, x)).ToList();
        return result;
    }
    public async Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync()
    {
        await using var c = factory.CreateConnection();
        return (await c.QueryAsync<NotificationRecipient>("SELECT u.user_id AS UserId,u.full_name::text AS Name,COALESCE(r.role_name,'Staff')::text AS Role FROM users u LEFT JOIN roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id WHERE u.is_active ORDER BY u.full_name,u.user_id LIMIT 200")).ToList();
    }
    public async Task SaveSettingsAsync(NotificationSettings settings)
    {
        NotificationRule.Validate(settings);
        await using var c = factory.CreateConnection(); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        // Serialize concurrent edits to the same shop, including its first settings save.
        await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('notification-settings:'||sf_tenant()::text,0))", transaction:tx);
        var ids = settings.RecipientIds.Distinct().ToArray();
        if (await c.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE user_id=ANY(@ids) AND is_active", new { ids }, tx) != ids.Length)
            throw new ValidationException("Choose active staff from this shop.");
        await c.ExecuteAsync("INSERT INTO notification_settings(enabled) VALUES(@Enabled) ON CONFLICT(tenant_id) DO UPDATE SET enabled=EXCLUDED.enabled,updated_by=sf_actor(),updated_at=now()", settings, tx);
        await c.ExecuteAsync("DELETE FROM notification_recipients", transaction:tx);
        foreach (var id in ids) await c.ExecuteAsync("INSERT INTO notification_recipients(user_id) VALUES(@id)", new { id }, tx);
        foreach (var r in settings.Rules) await c.ExecuteAsync("INSERT INTO notification_rules(event,enabled,title,body) VALUES(@Event,@Enabled,@Title,@Body) ON CONFLICT(tenant_id,event) DO UPDATE SET enabled=EXCLUDED.enabled,title=EXCLUDED.title,body=EXCLUDED.body", r, tx);
        await tx.CommitAsync();
    }
    public async Task<IReadOnlyList<ConsentCustomer>> CustomersAsync(string term)
    {
        await using var c = factory.CreateConnection();
        return (await c.QueryAsync<ConsentCustomer>("""
            SELECT c.customer_id AS CustomerId,c.customer_name::text AS Name,channels.channel::text AS Channel,
                   COALESCE(n.allowed,false) AS Allowed,COALESCE(n.evidence,'')::text AS Evidence
            FROM customers c CROSS JOIN (VALUES('email'),('whatsapp'),('sms')) channels(channel)
            LEFT JOIN notification_customer_consents n ON n.tenant_id=c.tenant_id AND n.customer_id=c.customer_id AND n.channel=channels.channel
            WHERE c.is_active AND c.customer_id IN (SELECT customer_id FROM customers WHERE is_active AND
             (customer_name ILIKE @pattern OR mobile_number ILIKE @pattern) ORDER BY customer_name,customer_id LIMIT 20)
            ORDER BY c.customer_name,c.customer_id,channels.channel
            """, new { pattern = "%" + term.Trim().Replace("%", "\\%").Replace("_", "\\_") + "%" })).ToList();
    }
    public async Task SaveConsentAsync(CustomerConsent consent)
    {
        Validator.ValidateObject(consent, new ValidationContext(consent), true);
        await using var c = factory.CreateConnection();
        if (!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM customers WHERE customer_id=@CustomerId AND is_active)", consent))
            throw new ValidationException("Choose an active customer from this shop.");
        await c.ExecuteAsync("""
            INSERT INTO notification_customer_consents(customer_id,channel,allowed,evidence) VALUES(@CustomerId,@Channel,@Allowed,@Evidence)
            ON CONFLICT(tenant_id,customer_id,channel) DO UPDATE SET allowed=EXCLUDED.allowed,evidence=EXCLUDED.evidence
            """, consent);
    }
    public async Task<NotificationFeed> FeedAsync(int page)
        => await FeedPageAsync(page,25);
    public async Task<NotificationFeed> FeedPageAsync(int page,int pageSize)
    {
        pageSize=Math.Clamp(pageSize,1,100);
        await using var c = factory.CreateConnection();
        using var rows = await c.QueryMultipleAsync("""
            SELECT count(*) FILTER(WHERE read_at IS NULL)::bigint AS unread,count(*)::bigint AS total FROM notifications;
            SELECT id AS Id,title::text AS Title,body::text AS Body,url::text AS Url,created_at AS CreatedAt,read_at AS ReadAt
            FROM notifications ORDER BY id DESC LIMIT @pageSize OFFSET @offset;
            """,new { pageSize,offset = (Math.Clamp(page,1,10000)-1)*pageSize });
        var counts = await rows.ReadSingleAsync<(long Unread,long Total)>();
        return new(counts.Unread,counts.Total,(await rows.ReadAsync<NotificationItem>()).ToList());
    }
    public async Task MarkReadAsync(long? id)
    {
        await using var c = factory.CreateConnection();
        await c.ExecuteAsync("UPDATE notifications SET read_at=now() WHERE read_at IS NULL AND (@id::bigint IS NULL OR id=@id)",new { id });
    }
    public async Task<IReadOnlyList<NotificationJob>> JobsAsync()
    {
        await using var c = factory.CreateConnection();
        return (await c.QueryAsync<NotificationJob>("SELECT id AS Id,event::text AS Event,reference::text AS Reference,status::text AS Status,attempts AS Attempts,delivered AS Delivered,created_at AS CreatedAt,last_error::text AS LastError FROM notification_outbox ORDER BY id DESC LIMIT 50")).ToList();
    }
}
