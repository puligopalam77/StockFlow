  # WhatsApp Business notifications — first release

StockFlow has owner account onboarding, optional eligible Business App onboarding, English utility template submission/approval sync, invoice notifications, sale-payment confirmations, signed delivery webhooks, opt-out suppression and a durable background sender. Open **Notifications → Channel settings → WhatsApp → Configure** (`/Configuration/Notifications/WhatsApp`). Browser and hosted Android screens share this implementation; test Meta login on the actual browser/device before rollout.

This release does not implement payment reminder scheduling, order/delivery events, PDF attachments, a shared inbox, marketing, subscription enforcement or sharing one number across multiple shops. Each active shop/business-profile combination has one connection, and a phone is assigned to only one connection. Different numbers in the same WABA may serve shops within the same tenant; a WABA cannot be assigned across independent tenants. There is no StockFlow fallback number or funded credit line.

## Manual database setup

The application does **not** apply migrations. Stop writers, back up, and run the **entire** [076_whatsapp_business_notifications.sql](../database/migrations/076_whatsapp_business_notifications.sql) file **once as PostgreSQL superuser, after 075**. It is a transaction: investigate errors and roll back instead of running selected fragments. Existing financial values, stock and invoices are unchanged. No historical WhatsApp messages are generated.

From the repository root, replacing the placeholders with the real database:

```powershell
psql -h YOUR_DATABASE_HOST -U postgres -d YOUR_DATABASE_NAME -v ON_ERROR_STOP=1 -f database/migrations/076_whatsapp_business_notifications.sql
```

The backend login used for the worker connection needs the existing worker role from 039. If not already granted, the DBA should use the actual login identifier:

```sql
GRANT stockflow_notification_worker TO your_backend_login;
```

Never grant that role to `stockflow_runtime`, application users or browser clients. It receives narrowly scoped function execution and no direct WhatsApp/business-table grants. Deploy/restart after the migration. Without 076, the new page shows database setup pending.

## Meta deployment prerequisites

1. Configure StockFlow's Meta developer application and WhatsApp product. Complete the business verification, Tech Provider onboarding, app review and advanced access required for independent customer businesses. Request the management and messaging permissions needed by the approved Embedded Signup configuration. A developer test number is not production approval.
2. Configure Embedded Signup for authorization-code exchange, the public HTTPS application origin, and allowed JavaScript SDK domains. Record its configuration ID. The backend verifies the exchanged token's app, permissions and WABA/phone pair with Meta; browser account IDs alone do not authorize a connection.
3. Choose a supported Graph API version for the real app and set it explicitly. There is no hard-coded production version fallback.
4. Configure the callback as `https://YOUR_STOCKFLOW_HOST/api/whatsapp/webhook`, with a random verification secret of at least 32 characters, and subscribe to `messages`. The backend subscribes StockFlow's app to the authorized WABA. GET verification uses that verification secret; POST callbacks require Meta's HMAC signature using the **app secret** over the original raw body.
5. Enable Meta's eligible Business App onboarding flow before enabling StockFlow's coexistence flag. Pilot with an eligible owner's number. StockFlow does not force migration, delete mobile accounts or promise every number is eligible. Ordinary chats, history sync and message echoes are not imported into a StockFlow inbox in this release.
6. Verify the customer's direct billing arrangement in their own WhatsApp Manager. StockFlow never attaches its own credit line or collects Meta charges. The UI records an **owner confirmation**, not an independent billing API verification. Connecting a phone is not proof that billing is ready; verify the payer during pilot onboarding.

Official references: [partner programme](https://whatsappbusiness.com/partners/become-a-partner/), [Tech Provider onboarding](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/onboarding-customers-as-a-tech-provider), [Business App onboarding](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/onboarding-business-app-users), [phone registration](https://developers.facebook.com/docs/whatsapp/cloud-api/reference/registration/), [messaging policy](https://whatsappbusiness.com/policy/). Developer pages were rate-limited during development; confirm the real app's approved configuration against current Meta documentation during the pilot. Fake-provider tests are not Meta certification.

## Server settings

Use protected service environment variables or a secret manager. Do not place actual credentials in SQL, chat, source control or browser fields. These placeholders are not usable credentials:

```powershell
$env:Notifications__WhatsApp__AppId = '<Meta app ID>'
$env:Notifications__WhatsApp__AppSecret = '<Meta app secret>'
$env:Notifications__WhatsApp__SignupConfigurationId = '<Embedded Signup configuration ID>'
$env:Notifications__WhatsApp__GraphVersion = '<supported version, for example vNN.0>'
$env:Notifications__WhatsApp__WebhookVerifyToken = '<random secret, minimum 32 characters>'
$env:Notifications__WhatsApp__CoexistenceEnabled = 'false'
$env:Notifications__WhatsApp__Enabled = 'false'
$env:Notifications__WorkerEnabled = 'true'
```

Keep WhatsApp delivery disabled while preparing the app. After validating billing, templates, consent and the pilot, set `Notifications__WhatsApp__Enabled=true` and restart. Both the worker and WhatsApp switches are required to send. Enable coexistence separately only when its Meta flow is approved. No additional SQL is needed for server settings. A process already running will not inherit new environment variables; restart it in the configured hosting environment.

The worker uses `ConnectionStrings:NotificationWorker`, falling back to `DefaultConnection`, and explicitly selects the worker role. Account tokens are encrypted with StockFlow's shared ASP.NET Data Protection keys and bound to tenant/sender. Preserve and protect those keys and database backups; losing the keys requires reconnecting accounts. Runtime table grants exclude ciphertext. OAuth client logging is disabled; generic error logs omit WhatsApp API query/body content. Configure proxy/APM logs to omit those query strings and bodies too, especially verification secrets and signup codes. No actual secret values were added to appsettings.

## Owner workflow

1. Sign in as tenant owner with Company settings EDIT permission and select an authorized shop/location/business setup.
2. Open WhatsApp Configure. Choose normal onboarding or, when enabled and eligible, **Connect existing Business App**. For a new Cloud API registration, expand the registration section and enter the six-digit PIN to use. Retain it securely; StockFlow does not store that PIN. An already registered Cloud API number normally needs no new registration.
3. Complete Meta authorization. Signup tickets are one-use, expire after ten minutes and are bound to owner, tenant, shop and profile. Starting a new signup invalidates older tickets for that owner.
4. Submit the English invoice/payment utility templates once. If they already exist, refresh their status instead of submitting duplicate names. Exact approved body, category, language and parameter compatibility are checked. Changed or non-approved templates are unavailable. The first release uses fixed text, without media headers, buttons or invoice URLs.
5. Complete direct Meta billing, record the owner confirmation, select event types and a daily attempt limit, then enable notifications. Connecting/reconnecting never enables sending automatically. Owner connection actions are audited without credentials.
6. Record the customer's explicit WhatsApp consent in Notification settings, with evidence of when/how it was obtained. CRM preferences do not imply consent. Recipients must be active with a ten-digit Indian mobile or an international number such as `+919876543210` (8–15 digits, no spaces).
7. Post a **new** pilot invoice/payment and check its sender, content, delivery and Meta billing. Invoices send their number, INR total and business name. Payments send payment ID, INR amount and invoice number. An initially paid invoice can produce both messages when both switches are enabled. This release sends no PDF or invoice link.
8. Test consent withdrawal, STOP/UNSUBSCRIBE/CANCEL, account revocation, paused templates, restart, disconnect and app/mobile coexistence on a real phone. Some WebViews restrict Meta popup login; use a supported normal browser for onboarding if necessary and validate APK acceptance separately.

## Delivery and recovery

Capture is deferred within the invoice/payment transaction; rollback cannot leave a false message. No remote call runs during checkout. A database capture error can still roll back the transaction; provider outages do not block a committed sale. WhatsApp is independent of staff in-app notification settings and recipients.

Each message snapshots connection generation, recipient, template and parameters. Before claiming, workers recheck consent, suppression, recipient, active tenant/shop/location, completed sale, event switches and connection identity. Disconnect/reconnect never silently substitutes a sender for queued jobs. Already claimed/in-flight sends can finish after settings change.

Claims use row locks and a fenced two-minute lease. Explicit HTTP 429 rejections retry with backoff, at most five attempts. Daily per-connection UTC quotas reserve attempts atomically, including retries; they do not replace Meta limits or account billing. Timeouts, interrupted responses, server errors and expired leases become **UNKNOWN** and are never automatically resent: Meta may already have accepted a chargeable message. Investigate in Meta before DBA-assisted recovery. Exactly-once external delivery is not promised.

Callbacks are transactional and deduplicated. Read/delivered states do not regress when older callbacks arrive, including callbacks racing the send acknowledgement. Enabled connections' template approvals refresh approximately hourly, with failed refreshes retried after ten minutes. Approvals older than 24 hours pause sending while new events remain queued; owners can refresh immediately. Monitor FAILED, UNKNOWN, prolonged pending jobs and worker logs. No account repair or history retention job is automatic.

STOP/UNSUBSCRIBE/CANCEL replies suppress future sends by tenant/customer number without importing chat history or attributing a customer action to a staff user. A later explicit, audited consent record can permit new messages again; duplicate old opt-outs do not undo that renewed consent. Record other wording/languages as withdrawal manually in this release. Disconnect wipes local credentials and pauses delivery but does not unsubscribe the entire WABA, which may serve other connected numbers. Owners can also revoke StockFlow in Meta Business Settings.

## Verification

Tests clone the existing `product_edit_069_test` disposable fixture on **127.0.0.1:55443**, apply 070–076, and delete only their own randomly named database. They never migrate the configured shop database or call real Meta/customer accounts:

```powershell
dotnet run --project tests/StockFlow.MasterDataChecks -- --whatsapp-checks
dotnet run --project tests/StockFlow.MasterDataChecks -- --notification-checks
```

Coverage: signup replay/scope, permissions, ciphertext isolation, template incompatibility, transaction rollback, invoice/payment deduplication, concurrent claims, callback ordering, fenced/expired leases, quotas, retry exhaustion, consent withdrawal, repeated STOP, hosted worker delivery/approval refresh, CSRF and signatures. Live Meta approval, eligibility, billing and device acceptance require the real application and owner account.
