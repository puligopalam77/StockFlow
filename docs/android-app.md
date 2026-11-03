# Android app setup and device features (Phases 3–4)

StockFlow.Android is a .NET 10 Android WebView app. It loads the existing hosted StockFlow screens and uses the same backend and database. The browser website and its build remain unchanged. Open `src/StockFlow.Android/StockFlow.Android.sln` to build the APK; the main solution links to this project as solution items so web-only developers do not need Android tooling.

## Connect

On first launch, enter your shop's **HTTPS origin**, for example `https://stockflow.yourcompany.com`. Do not include `/Sales`, credentials or query strings. The server must be reachable from the phone with a publicly trusted TLS certificate. `localhost` on a phone refers to that phone. A private network address can work if reachable and its HTTPS certificate is trusted.

The app opens the existing `/Account/Login` screen. Use the same account as in the website. App and browser sessions are separate: signing in to one does not automatically sign in to the other. Both operate on the same live business data. Existing server permissions, store scope, CSRF checks, idle/absolute expiry and session revocation remain authoritative. No tokens or passwords are copied into native preferences. Only the server origin is saved there; authentication cookies remain in the app's private WebView cookie store. Device backups are disabled.

**Connection** lets you change the server after confirming that current work is saved. It clears the app's cookies and web storage, including local drafts. It does not sign out the browser or modify server records. Android rotation restores WebView navigation state; draft recovery continues to use the existing screens' behavior. Backgrounding does not refresh the login session.

## Build an APK

Prerequisites: .NET 10 SDK, Android workload, Android SDK, Microsoft OpenJDK 21. Follow [Microsoft's Android installation instructions](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/net-android) and [dependency installation instructions](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies).

From the repository root in PowerShell:

```powershell
dotnet workload install android
dotnet build src/StockFlow.Android/StockFlow.Android.csproj -t:InstallAndroidDependencies -p:AndroidSdkDirectory=E:/StockFlow/.artifacts/android-sdk -p:JavaSdkDirectory=E:/StockFlow/.artifacts/android-jdk -p:AcceptAndroidSdkLicenses=True
dotnet build src/StockFlow.Android/StockFlow.Android.csproj -c Debug -p:AndroidSdkDirectory=E:/StockFlow/.artifacts/android-sdk -p:JavaSdkDirectory=E:/StockFlow/.artifacts/android-jdk
```

Replace the SDK/JDK paths for your machine. The signed **debug/testing APK** appears under `src/StockFlow.Android/bin/Debug/net10.0-android/`. Install using `adb install -r <signed-apk-path>` or transfer the signed APK to a test phone. Android 8.0+ and an updated Android System WebView are required. The app requires a network connection; it does not introduce offline stock transactions.

For production, use a privately held release keystore, retain that key for updates, and build/sign in Release mode using [Microsoft's signing instructions](https://learn.microsoft.com/en-us/dotnet/android/deploy-test/signing/). Do not commit keystores, signing passwords or customer credentials. Keep the same package ID `com.stockflow.mobile` and increase `ApplicationVersion` for updates. Release signing/distribution is a separate deployment step.

## Device features

Phase 3 includes first-run connection, existing web login and business screens, first-party cookies/local storage, same-origin navigation, Android Back, safe device/keyboard insets, session re-check on resume, file selection for existing upload fields and explicit network/TLS error handling. Invalid certificates are rejected. Failed saves are **never automatically replayed**; check the saved result before retrying. HTTPS links to other hosts, phone and email links open their appropriate external apps.

Phase 4 is included in app version **1.1.0 (version code 2)**. Deploy the updated web application as well as the APK to show the new camera buttons. These features reuse existing workflows and server permissions:

- **Camera scanning:** tap Camera beside an existing scan field. Grant camera permission, point at the barcode, review the detected code and tap **Use code**. This calls the same handler as typing/scanning a code; it does not save a sale or receipt automatically. Cancel, backgrounding or leaving the screen stops the camera. The decoder is bundled locally; camera images are not uploaded. Manual entry and physical barcode scanners remain available.
- **Microphone:** the assistant's microphone records a question for review before sending it through the existing voice transcription endpoint. Android requests microphone permission when needed. The hosted server must have the existing voice service configured; see [AI setup](help/ai-setup.md). App permissions alone do not install or enable server transcription. Backgrounding stops recording and speaking. Browser microphone/camera use requires HTTPS and browser permission.
- **File uploads:** existing logo, attachment and other file inputs open Android's document picker. File types and multiple selection follow the field's accepted types; server validation remains authoritative. No broad storage permission is requested.
- **Downloads and exports:** PDF/document links and generated CSV exports open Android's **Save file** picker. Downloads are limited to **20 MB**, remain restricted to the configured StockFlow origin, and use the current signed-in session. Cancel leaves the document unsaved. Navigation or a connection change cancels a pending transfer; return to the document and retry. No failed save is automatically replayed.
- **Printing:** existing Print actions open Android's print dialog, including **Save as PDF** and installed printer services. A4 is the default, with 80 mm receipt and 63 × 38 mm label media suggested for the corresponding templates. The selected printer/service must support the paper size. Wi-Fi printers generally require a compatible Android print service. Bluetooth/USB thermal printing requires a compatible print service; this version does not add vendor-specific direct printer drivers. Existing invoice company details and logo are retained by the document template.

Camera and microphone access is granted only to the configured HTTPS origin after Android permission checks. Denial is handled with a message and manual input remains available. Permissions can be changed in Android Settings → Apps → StockFlow → Permissions. Device operations do not introduce new business APIs or database tables.

## Verification

Run `dotnet run --project tests/StockFlow.AndroidChecks` for HTTPS/origin/login routing and download policy checks. `tests/StockFlow.UiChecks/device-features-offline.cjs` checks the bundled barcode decoder, scan review/cancellation, responsive camera dialog and native print/download message flow. Existing web tests continue to run independently.

Build verification on 10 October 2026: Android Debug APK built with zero warnings/errors; signature verified; package targets API 36 with minimum API 26 and arm32/arm64/x64 support. Assemblies are embedded, so the APK can be transferred and installed without Visual Studio fast deployment. The main web solution build and server cookie/idle-expiry/device-revocation/CSRF checks also passed. The browser idle-session regression passed after guarding a late help translation callback when session expiry removes the screen. No connected Android device or hosted shop address was available for end-to-end APK verification.

Phase 4 automated checks cover real Code 128 decoding, camera UI with simulated capture, microphone recording/review/cancellation, native message transfer of CSV bytes, and printing lifecycle messages. These checks do not replace testing live Android camera capture, Android permission prompts, file providers or physical printers.

Before release, test on an actual phone/tablet against a hosted **test** shop: login/logout; all user roles/store restrictions; sales and receipt entry; stock corrections; reports; idle expiry after backgrounding; server session revocation; rotation; file selection; loss of connection during a save; relaunch; and changing shop connections. Also test camera/microphone permission grant and denial, real barcode capture, voice transcription, upload selection, CSV/PDF saving, Save as PDF and each supported printer/paper size. Confirm the same records are visible through the browser. Never use production transactions for test submissions.

No database migration is required.

Phase 5 has a repeatable regression runner and a live-device acceptance checklist: [Regression and device testing](mobile-regression-testing.md).

Phase 6 provides a signed Release APK, hosting templates and installation/update steps: [Release guide](release.md).
