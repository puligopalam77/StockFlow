# Phase 5: regression and device testing

The Android app and browser use the same business APIs. Test both against a hosted **test shop** before release. Automated browser interception exercises the real screen scripts but substitutes network responses; it does not establish live Android WebView or printer compatibility.

## Repeatable automated checks

From the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Run-MobileRegressionChecks.ps1
```

Requires .NET 10, the built `StockFlow.UiChecks` Playwright runtime and Microsoft Edge. Logs and a machine-readable result are written to `tests/bin/mobile-regression-results/`. A failed check stops the run and is recorded as failed. Results distinguish untested physical devices and database concurrency from passed automation.

The checks cover login expiry and permissions, sales/payment validation, receipt and inventory workflows, responsive layouts, interrupted save/reload with the original request, voice review/cancellation, barcode review and native print/download messages. APK origin and document policy checks run independently of Android hardware.

To test real transaction races, provision a **separate disposable PostgreSQL instance** on `127.0.0.1:55441`, with a local test `postgres` account, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Run-MobileRegressionChecks.ps1 -WithDisposableDatabase
```

The database suites create GUID-named temporary databases, apply migrations only there, and drop those databases in `finally`. Do not use the shop database instance for this port. These checks prove identical concurrent requests produce one invoice/receipt, altered retries are rejected, and competing sales cannot consume the same last quantity unit or serial. They also exercise transaction rollback and reconciliation queries. No migration of the shop database is needed for this testing phase.

## Physical APK and live URL acceptance

Use separate accounts on an Android phone, Android tablet and desktop browser, plus the same account on APK and URL to verify independent sessions. Record device model, Android/browser/WebView versions, APK version, test server origin, account role, observed record IDs and outcome. Never include credentials in evidence.

| Scenario | Expected result |
| --- | --- |
| Login, logout, idle expiry, server session revocation in APK and URL | Existing permissions and expiry apply; one device's idle clock does not refresh another device's session. |
| Two users sell the last available unit at the same time | One completes; the other receives an unavailable-stock error. Stock stays nonnegative. |
| Two users sell the same serial at the same time | One completes; one rejects. One sold unit, one stock deduction. |
| Rapid repeated taps on Complete sale or Save receipt | No extra invoice, payment, physical unit or inventory movement. |
| Airplane mode before sending a save | Clear failure, no success display. Recover/retry uses the original request. |
| Connection lost after server commit but before response | Restore connection and use recovery/retry; original saved record is returned once. Reconcile quantities and payments. |
| Background, rotate, close and reopen after an uncertain save | Check saved history and recover the original request; do not create a new bill to retry. |
| Permission revoked while a draft is open | API rejects saving; the client does not silently change account, store or request. |
| Camera/microphone permission grant and denial | Permission prompts work; capture stops on cancel/background; manual entry remains available. |
| Upload logo/file; download CSV/PDF | Correct contents and logo, safe picker cancellation, no unintended record submission. |
| Print invoice, receipt, saved cash closing and label | Save as PDF and compatible installed printer services produce readable output with the correct logo and paper size. |
| Phone/tablet landscape, portrait and keyboard open | Controls remain usable; text is readable and no content is clipped. |

Passing automated checks alone does not complete this physical acceptance table. See [Android setup](android-app.md) for the testing APK and supported device features.

## Recorded result — 10 October 2026

The full runner completed **23 checks, all passed**, including web/test builds, server permissions/session tests, browser regressions and both real PostgreSQL transaction suites. Test databases were created on a new isolated loopback PostgreSQL 18 instance and removed by the suites; that instance was stopped afterward. The shop database was not accessed.

The billing browser test now aborts the save connection, exercises rapid duplicate taps, reloads the uncertain draft and verifies exact-payload retries. Real database tests confirmed one invoice/receipt per concurrent retry, rejection of changed receipt payloads, and prevention of competing sales of the last quantity unit or serial. Responsive checks include 320, 390 and 768 px screens, with billing also checked at 1440 px. A cash-closing mobile history overflow found during testing was corrected without changing cash calculations or saving behavior.

The existing APK **1.1.0 / version code 2** passed signature verification (v2 and v3). Android policy and simulated native print/download checks passed. **No Android device/emulator was connected and no hosted test-shop origin was supplied**, so live APK/URL parity, Android capture and permission prompts, file-provider behavior and physical printers remain pending. Phase 5 physical acceptance is not yet signed off.

Detailed run output: `tests/bin/mobile-regression-results/results.json` and the adjacent per-check logs. These generated files are local test artifacts; rerun the script to refresh them.
