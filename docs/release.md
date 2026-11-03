# Phase 6: hosting, signed APK and updates

Release preparation adds a production web bundle, HTTPS reverse-proxy template and Android signing script. It does not deploy to a public server automatically. The public domain, hosting account, DNS and production configuration must be supplied for live hosting. No database migration is added by this phase; any previously unapplied migrations still need your manual review and execution.

## Prepared files

- `deploy/Build-Release.ps1`: publishes the web application without local `appsettings*.json`, builds a signed Release APK, verifies its signature and writes the APK SHA-256 checksum.
- `deploy/Caddyfile`: HTTPS reverse proxy to the loopback-only web process.
- `deploy/Start-StockFlow.ps1`: runs the published web application in Production with the exact public hostname and trusted loopback proxy.
- `.artifacts/release/`: generated web bundle, `StockFlow.apk`, checksum and hosting scripts. Keep signing material out of this distributable directory.

Signing uses .NET Android's environment-variable password support; passwords do not appear in command arguments. See [Microsoft build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties). Caddy obtains and renews public certificates for eligible public domains and redirects HTTP to HTTPS; see [Caddy HTTPS setup](https://caddyserver.com/docs/quick-starts/https).

## Release key and build

This first release uses a newly generated private RSA key, alias `stockflow`, in `.artifacts/android-signing/stockflow-release.jks`. Its passwords are in `.artifacts/android-signing/signing-secrets.json`. The directory is restricted to the current Windows user and SYSTEM, and ignored by source control. **Back up both files to a protected location outside this computer before distributing the APK.** Do not send them with the APK, upload them to the web server or commit them. Losing the key prevents ordinary in-place app updates. If an app with this package ID was previously released using another key, use that original key instead.

Build from the repository root in PowerShell:

```powershell
$signingSecrets = Get-Content -LiteralPath '.artifacts/android-signing/signing-secrets.json' -Raw | ConvertFrom-Json
$env:STOCKFLOW_STORE_PASSWORD = $signingSecrets.storePassword
$env:STOCKFLOW_KEY_PASSWORD = $signingSecrets.keyPassword
try {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File deploy/Build-Release.ps1 -Keystore '.artifacts/android-signing/stockflow-release.jks'
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
} finally {
    Remove-Item Env:STOCKFLOW_STORE_PASSWORD,Env:STOCKFLOW_KEY_PASSWORD -ErrorAction SilentlyContinue
    $signingSecrets = $null
}
```

Supply `-AndroidSdkDirectory` and `-JavaSdkDirectory` if the local SDK/JDK paths differ. Never replace the keystore just to build an update. The generated bundle uses the existing app package ID `com.stockflow.mobile`.

## HTTPS hosting

The current address supplied for this release is **localhost**. Continue using the existing localhost website on the development computer. Localhost is not a public hosting address and, on an Android phone, refers to the phone itself. This Release app requires a reachable HTTPS origin with a trusted certificate; use a public hostname or a reachable LAN hostname/address with an appropriate trusted certificate. A self-signed development certificate is not automatically trusted by Android. The release scripts do not disable TLS validation or enable cleartext HTTP to work around this. Public certificate provisioning and live hosting are therefore pending.

1. Choose a public hostname such as `stockflow.yourcompany.com`. Point DNS to the hosting server; permit inbound TCP 80/443 to Caddy. Keep PostgreSQL and the application's loopback port private.
2. Install the .NET 10 ASP.NET Core runtime and Caddy on the server. Copy `.artifacts/release/web` to a versioned deployment directory. Copy the hosting scripts separately. Keep the server's Caddy certificate storage persistent across updates.
3. Configure `ConnectionStrings__DefaultConnection` through the hosting service's protected secret store with the existing application database connection. The release bundle deliberately excludes local configuration and database credentials. Carry over required production options (voice/model service, mail, shop timezone, security certificates and other configured integrations) through environment variables or a protected server-side `appsettings.Production.json`. Use double underscores for nested environment settings. Review [AI setup](help/ai-setup.md) for voice requirements.
4. Run `Start-StockFlow.ps1 -Domain stockflow.yourcompany.com -WebDirectory <published-web-folder>` under a dedicated service account. Configure your Windows service manager or hosting platform to keep this process running and restart it after reboot. The script is a process launcher, not a service installer. The web process listens only on `127.0.0.1:5220` and trusts forwarding headers only from `127.0.0.1`.
5. Set `STOCKFLOW_DOMAIN=stockflow.yourcompany.com` in the **Caddy service environment** too (a separate process does not inherit values set inside the web launcher). Validate and run Caddy with the supplied `Caddyfile`:

```powershell
caddy validate --config Caddyfile --adapter caddyfile
caddy run --config Caddyfile --adapter caddyfile
```

Use a managed Caddy service for ongoing hosting. If the proxy runs on another machine or in a container, adjust its upstream address and the exact trusted proxy IPs; do not trust arbitrary forwarding headers. HTTPS terminates at Caddy; proxy-to-web HTTP stays on loopback.

Verify the public login URL from outside the server and on a phone: a trusted certificate, HTTP redirect, successful login, secure cookies and working sales/report permissions. Do not start a second standalone HTTP endpoint or disable certificate validation. The app's first-run screen takes this same HTTPS origin; the URL website and APK continue to use the same backend and database.

## Install

Distribute only `StockFlow.apk` and its checksum through your controlled channel. Install on Android 8.0+ with an updated System WebView. Open the APK and, if prompted, allow installation from that trusted source. Alternatively:

```powershell
adb install -r StockFlow.apk
```

Open StockFlow, enter the public HTTPS origin, then sign in with your existing account. Camera/microphone permission is requested when used. Follow the [physical device checklist](mobile-regression-testing.md) before wider distribution; a Release build is not proof of live device compatibility.

The earlier Debug APK has a different signing certificate. Android will reject installing this Release APK over it. First resolve any uncertain sales/receipts and save necessary drafts, then uninstall the Debug app and install Release. Uninstalling clears local app sessions/drafts but does not remove server records. Normal updates signed with this release key do not require uninstalling.

## Update and rollback

For each Android update, keep the package ID and keystore, increase `ApplicationVersion` in `StockFlow.Android.csproj` (the next version is at least **3**), update `ApplicationDisplayVersion`, run regression checks, rebuild and verify the signed APK. Install using `adb install -r` or open the new APK on the device. App data is retained for an ordinary compatible update; users may still need to log in if their server session expired.

For web updates, back up the database using the established shop procedure, publish to a new versioned directory, carry over production configuration, and switch the web service to it. Do not overwrite the server's secrets or Caddy certificate storage. Apply any future migration manually as documented, then check APK and browser workflows against the same server. This release phase does not execute migrations.

Keep the previous web bundle for rollback. Only roll back to a version compatible with the current database schema. Android normally rejects downgrading version codes; publish a corrected APK with a higher code using the same key instead of uninstalling customer apps. Resolve pending transactions before any deployment that might interrupt a save.

The release artifacts are prepared locally. Live HTTPS deployment and phone/printer acceptance remain pending until the public hosting details and physical device are available.

## API query performance review — 11 October 2026

The inventory list reads image URL/name columns directly, avoiding conversion of
uploaded image bytes into JSON. Initial dashboard panels use one batched database
command instead of nine separate query commands, with the same scoped connection
and repeatable-read snapshot. Shop summary's previous-period comparison retrieves
only financial rows instead of repeating credit, trend, product and activity work.

Read-only checks on the current small dataset measured inventory at 102 ms and
dashboard reads at 96–190 ms. Financial-only reads took 9–37 ms versus 48–78 ms
for complete reports on the same date ranges, and their financial rows matched.
These are repository timings, not end-to-end API response guarantees or load-test
results. No business records or database schema were changed.

Run `--product-sales-readonly` with `tests/StockFlow.MasterDataChecks` to repeat
the guarded live read checks. `--dashboard-checks`, `--reports-checks` and
`--inventory-control-checks` cover API/UI access and behavior with controlled data.
Rebuild and restart the web application to apply these optimizations. No new
migration or APK rebuild is required.

## Build verification — 10 October 2026

Release web publish and signed Android Release build completed with zero warnings/errors. The APK is **1.1.0, version code 2**, approximately **13 MB**, supports arm32/arm64/x64 and Android API 26+, and is not marked debuggable. Its v2/v3 signatures and SHA-256 checksum were verified. No local `appsettings*.json` files are present in the distributable web bundle. Android policy checks and the main web build passed. Caddy accepted the supplied template with a placeholder public hostname; this validates configuration syntax, not real DNS, certificate issuance or live hosting. No database scripts were executed.
## In-app notification release

Apply manual migration **039_notifications.sql** after 038 before enabling the notification worker. Notifications start disabled per shop; configure recipients/events under **Setup → Configuration → Notification settings**. See [notification setup and operation](notifications.md) for worker configuration, database login permissions, retry behaviour and validation. External customer messaging and closed-app push are later phases.


### Business alert and email release

Apply manual migration **040 after 039** before enabling advanced alerts. Configure sender verification, per-shop provider secrets and optional backup failure reporting following [notification setup and monitoring](notifications.md#business-alerts-and-email-migration-040). New settings are in Setup > Notification center; all new channels default off. Subscription reminders await subscription records. Confirm real provider delivery and APK/device acceptance before the client rollout.

### Password login release

Email login OTP is retired. Stop StockFlow, back up, and manually apply `database/migrations/045_disable_login_email_otp.sql` once after 044 as PostgreSQL superuser. Deploy the updated app and restart it. Username/password sign-in requires no Brevo account or OTP hash key. Email notifications remain optional and independently configured. Keep historical migrations 041–044 in the migration chain; 042 retains the eight-character default password minimum. See [notification setup](notifications.md).
