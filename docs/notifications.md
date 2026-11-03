# Notifications: setup and operation

WhatsApp Business is available as an initial transactional channel. See [WhatsApp setup and manual migration 076](whatsapp-business-integration.md) for owner onboarding, Meta prerequisites, invoice/payment notifications and deployment limitations. Earlier notes below describe the original notification/email releases.

This release implements notification settings, durable background delivery, daily stock digests, transactional email and per-user in-app history. It uses the same hosted web screens in the browser and Android APK. A closed APK does not receive Android push notifications in this phase.

## Manual database setup

1. Stop StockFlow and back up the application database.
2. Apply `database/migrations/039_notifications.sql` **once**, as PostgreSQL superuser, after migrations through 038. The migration creates tables, tenant/user permission policies, business-event triggers and the restricted worker role/function. It does not generate notifications for historical transactions or change stock or invoice totals.
3. If the server connection uses a non-superuser database login, have the DBA run `GRANT stockflow_notification_worker TO your_backend_login;`, replacing the identifier with the real server login. Do not grant this role to `stockflow_runtime`, browser users or Android users. The worker role has no direct business-table grants.
4. Rebuild/restart the web server with `Notifications:WorkerEnabled=true`. The checked-in default is false so the existing application can run before the migration is applied. Environment variables may be used:

```powershell
$env:Notifications__WorkerEnabled = 'true'
$env:Notifications__PollSeconds = '10'
```

The worker uses `ConnectionStrings:NotificationWorker` if supplied, otherwise `DefaultConnection`, then selects the restricted `stockflow_notification_worker` role. Keep connection credentials in server secrets. Polling is bounded to 5–300 seconds, batches to 25 events and database commands to 10 seconds. Multiple web instances can run workers safely.

## Shop settings

Open **Setup → Notification center**. Existing Company settings VIEW permission allows inspection; EDIT allows changes. Other configuration pages keep their current permissions. Choose staff recipients, enabled events and templates, then enable notifications. Templates support only `{reference}` and `{status}` and render as plain text. No staff recipient is subscribed automatically.

Events currently supported:

| Event | Trigger |
|---|---|
| Invoice completed | New completed sale or transition into COMPLETED |
| Stock received | New completed purchase receipt or transition into COMPLETED |
| Repair received | New repair request |
| Repair ready | Repair changes to READY |
| Warranty claim updated | Claim status changes |

Staff need the relevant module VIEW permission and access to the original stock location. Permissions are checked during delivery and when viewing existing notifications, so revoked access hides old alerts. Recipients are evaluated at delivery time. Disabled events/shops and events without an eligible recipient are marked SKIPPED rather than repeatedly retried.

Customer consent is also available here for email, WhatsApp and SMS when the administrator has Customers VIEW permission. Search by name or mobile, select the customer/channel, record explicit agreement or withdrawal and describe when/how it was obtained. Withdrawals remain recorded, with an immutable database history of each saved consent change. Recording consent alone does not send messages. Email requires the separate migration, settings and server setup below. WhatsApp, SMS, subscription reminders and Android push remain pending.

## Bell and delivery history

The header bell opens **Notifications** with unread counts, document links, individual read controls, mark-all-read and 25-item pagination. The feed follows the current shop/location selection, like the linked business documents; switching location shows its alerts. Counts refresh once per minute while the screen is visible. Refresh errors do not block the current workflow. Notification timestamps are labelled UTC. Android displays the same page; no APK rebuild is needed for these hosted-screen changes.

Configuration shows the latest 50 delivery events the administrator has module/location permission to view, including status, attempts and recipient counts. A temporary worker outage leaves committed events queued. Each event is claimed using database row locks; each recipient delivery has a uniqueness constraint. Delivery and event status commit together. Failed individual delivery transactions roll back their partial inserts and retry after 30, 60, 120 and 240 seconds; the fifth failed attempt marks FAILED. There is no automatic unlimited resend or manual retry action in this release.

No remote service is called while posting a business transaction. The outbox insert occurs in the same database transaction as the business event, so rollback cannot leave a false alert. A database failure saving that event rolls back the business transaction as well; worker/provider availability is independent of billing. Follow the normal retry/recovery workflow for a failed business save.

When enabling in production, verify one invoice/receipt/repair, recipient access, notification read state and delivery history. Monitor server logs for worker errors. Disabling the worker pauses delivery without discarding queued events; disabling shop notifications stops capture and skips pending events when processing resumes. Back up notification tables with the normal shop backup. History retention/archival is not automated in this release.

## Validation

These checks use an isolated test database on `127.0.0.1:55441`, never the configured shop database:

```powershell
dotnet run --project tests/StockFlow.MasterDataChecks -- --notification-database-checks
dotnet run --project tests/StockFlow.MasterDataChecks -- --notification-checks
```

They cover outbox rollback, duplicate/concurrent processing, consent withdrawal/audit, permission revocation, tenant/user isolation, bounded retries, recovery, CSRF protection, encoded notification text and settings access. Physical phone acceptance still requires testing against your hosted server.

Verification on 11 October 2026: the web build completed with zero warnings/errors. Both notification suites passed, including the hosted worker's actual delivery and shutdown. Existing billing, receiving, configuration and reports checks passed. The notification browser fixture check passed at widths 1920, 1440, 1024, 768, 390 and 320 pixels with no page overflow or JavaScript errors. Only disposable databases were migrated; the configured shop database was not changed.

## Business alerts and email (migration 040)

Stop the web server, back up and apply `database/migrations/040_business_alerts_email.sql` **once as PostgreSQL superuser after 039**. This has not been applied to your configured shop database. All new options default OFF. Keep the worker role grant from 039. Deploy/restart the updated web application afterwards; the old 039-only installation remains usable with advanced options unavailable.

In Setup > Notification center, enable the shop, select staff recipients, and configure **Business alerts and email**:

- Daily stock digest: choose an IANA time zone and hour. One digest per active stock location per local day, only when items are at/below their limit, including zero stock. Quantity respects serial tracking and location-specific limits. No historical digests are created. If the worker is offline at the selected hour, it processes the current day's digest when restarted later that day. Inventory VIEW permission is required to enable it.
- Repairs: existing received/READY events; warranty: status changes only, ordinary edits generate no alert. Staff still require source module and location access.
- Customer emails: select invoice, repair and/or warranty emails. An active customer must have a saved email address and explicit email consent. Pending sends check consent, customer email, event switches and shop activity again before claiming. Withdrawing consent stops unclaimed sends; an already claimed/in-flight send may finish.
- Invoice emails contain a plain-text invoice summary with final saved lines and total, not a public link to an authenticated invoice or PDF attachment. Capture waits until the business transaction commits.
- Administrator emails: up to five addresses receive enabled stock digests and actual failure alerts. Configure authorized shop administrators only. Company settings EDIT controls this list; shop data never goes to another shop's API account.
- Subscription reminders are intentionally deferred until subscription records and renewal dates exist. This migration does not invent subscription dates.

Create a Brevo account, activate transactional sending, verify the sender/domain and obtain an API key. The sender must appear as active in Brevo. Store secrets in the web service's protected environment/secrets, **not SQL or committed config**:

```powershell
# Examples for tenant 1; use the actual tenant ID and service environment.
$env:Notifications__WorkerEnabled = 'true'
$env:Notifications__Email__Enabled = 'true'
$env:Notifications__Email__Tenants__1__ApiKey = '<Brevo API key>'
```

Save the sender email/name in Configuration, click **Verify saved sender with Brevo**, then enable the required email events. Changing sender email clears its verification. There is no shared fallback API key across shops. Email is sent only after both server and shop email switches are enabled, sender verified, and the relevant event/consent permits it. The existing shop notification switch and event rules also apply.

Brevo references: [transactional send API](https://developers.brevo.com/reference/send-transac-email), [active senders](https://developers.brevo.com/reference/get-senders), [sender/domain setup](https://developers.brevo.com/docs/getting-started-with-senders-and-domains). This integration uses plain text and one recipient per request. No live customer emails were sent during development/testing.

Sender verification reports missing shop API keys, rejected keys (401), denied access (403), rate limits, missing/inactive senders and connection failures separately. It queries Brevo's saved sender list; it does not create or activate senders. Add and verify the exact saved sender address in Brevo first. An API key is required, not an SMTP key. A verified sender alone does not enable server email delivery.

For local testing, environment variables must be set in the process that starts StockFlow. Start `dotnet run --project src/StockFlow.Web` from the same PowerShell window after setting the variables above. A web server already running in Visual Studio/VS Code will not inherit variables set later in another terminal; restart it with the configured environment. Do not paste API keys into chat or commit them. No additional database migration is needed for these verification diagnostics.

## Delivery monitoring and recovery

Configuration shows the latest permitted 50 in-app events and 50 email attempts. ACCEPTED means the provider returned a message ID, **not proof of inbox delivery**; use Brevo logs for bounces/delivery. There is no webhook or automatic bounce reconciliation in this release.

Email claims commit before HTTP requests, have a fenced two-minute lease and stable request UUID. Concurrent workers cannot claim the same row. Provider rate-limit responses defer and retry at most five attempts. The daily limit defaults to 250 send attempts, capped at 300, and is reserved atomically per shop per UTC day, including retries. This local limit does not guarantee provider availability: other account activity or provider restrictions can still cause rate limits.

Timeouts, interrupted connections, provider server errors and expired worker leases become UNKNOWN. They are **not automatically resent**, because the provider may already have accepted them. Check the provider using the request/message details and reconcile with the DBA before a deliberate resend. Provider idempotency keys are additional protection, not an unlimited exactly-once guarantee. Terminal email/in-app failures create one operational alert; failure alerts do not recursively alert about themselves. Disabling server email pauses sending; disabling shop/event/consent skips unclaimed jobs when processing resumes.

Monitor worker error logs, FAILED/UNKNOWN states, prolonged PENDING/RETRY, scheduled backup task results, and verified backup archives. If the database/server is unavailable, it cannot queue or send its own outage email; use external uptime/task monitoring as well. No history cleanup runs automatically.

## Actual backup failure reporting

`Run-StockFlowBackupWithAlerts.ps1` wraps the existing verified backup script and reports **only a real failure**, retaining the original failing task outcome. Newly installed tasks use this wrapper; existing tasks must be updated manually to call it (same backup parameters). No task is changed automatically by deploying the web application.

Generate a random secret of at least 32 characters; configure the web service:

```powershell
$env:Notifications__Jobs__Tenants__1__Token = '<random secret>'
```

Configure the backup task account's environment securely, then restart its process/task host as appropriate:

```powershell
$env:STOCKFLOW_JOB_ALERT_URL = 'https://your-host/api/notification-jobs/failure'
$env:STOCKFLOW_JOB_ALERT_TOKEN = '<same random secret>'
$env:STOCKFLOW_JOB_TENANT_ID = '1'
```

For a local task on the web server, `http://127.0.0.1:5220/api/notification-jobs/failure` is supported. Remote reporting requires HTTPS. Passwords continue to use PGPASSFILE. API keys/tokens are never stored in notification records; the endpoint accepts only backup job/run identity, not exception details. A repeated report with the same run UUID is deduplicated. If reporting fails, the task emits a warning and still fails; check Task Scheduler/logs because there is no persistent offline alert spool.

## Release acceptance

Run the notification database and HTTP suites plus existing billing/receiving regressions. Tests use only the disposable PostgreSQL server on port 55441 and fake provider responses. Verify advanced settings at phone/tablet/desktop widths. Browser and APK use the same server pages; no APK rebuild is needed for these web screens.

Before client release, test on an actual phone/APK against the hosted HTTPS URL: sign in, post a bill/receipt/repair, inspect the bell, grant/revoke permissions, mark read, disconnect/reconnect, restart the worker, verify one email in Brevo and the recipient inbox, and force a disposable backup failure. Live provider credentials, domain verification and physical device acceptance require your environment; they are not replaced by automated fixtures.

Phase 4�6 verification (11 October 2026): advanced database tests passed for concurrent daily digests, final invoice content, consent withdrawal, sender verification reset protection, daily quotas, fenced/expired leases, five-attempt email retries, concurrent email claims, backup failure deduplication and hosted worker send/acknowledgement using a fake provider. Provider response tests covered acceptance, rate limiting, authorization failure and uncertain server errors. HTTP/settings checks, responsive fixtures at six widths, existing billing/receiving database regressions and PowerShell syntax checks passed. Web build: zero warnings/errors. Live provider delivery, scheduled-task execution and physical APK testing remain deployment acceptance steps.

## Password login; email OTP retired (migration 045)

Email OTP has been removed from login, staff editing and notification preferences. StockFlow signs in using username/password without consulting the email provider. Existing password-change, session, permission and authenticator policies remain in force. Development/debug restarts require fresh credentials; production sessions retain their normal lifetime.

Stop StockFlow, back up, then manually apply `database/migrations/045_disable_login_email_otp.sql` once after 044 as PostgreSQL superuser. The script disables saved account/shop email OTP flags, consumes pending challenges and removes worker access to retired OTP begin/verify functions. Historical migrations and tables remain to preserve the migration chain. Do not undo 041–044 or drop notification tables.

Administrator emails remain optional recipients for business alerts and test notifications. Email notification delivery still requires a configured provider and verified sender, but password login does not. The old `Notifications__LoginOtp__HashKey` setting is no longer used and may be removed from deployment secrets. Test-email rate limiting from migration 041 remains active.
