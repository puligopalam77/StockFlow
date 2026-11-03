# Running StockFlow locally

In VS Code, press **F5** and select **StockFlow: UI + Swagger** to build and run.
Press **Ctrl+Shift+B** to build only. The build task first stops the running
StockFlow app from this workspace to release its locked files. Building therefore
ends an active StockFlow debug session; press F5 again to run the new build.
Other applications and StockFlow copies in other folders are left running.

If you prefer a terminal:

From `E:\StockFlow`:

```powershell
dotnet build StockFlow.sln
dotnet run --project src/StockFlow.Web --launch-profile http
```

Open `http://localhost:5220`. Stop the running app with Ctrl+C (or stop the IDE debugger) before rebuilding from another terminal. A running app holds its loaded DLLs on Windows; MSB3027/MSB3021 reporting that a DLL is locked by `StockFlow.Web` means that process must stop before the build can replace it.

If an old process is left running, inspect its identity and executable path before stopping that specific PID:

```powershell
Get-Process -Name StockFlow.Web | Select-Object Id, Path
Stop-Process -Id <the-verified-StockFlow-PID>
```

For edit/run development, use one terminal with `dotnet watch --project src/StockFlow.Web --launch-profile http` and stop it before a separate build. The HTTP development profile does not redirect to an unavailable HTTPS endpoint. Production and the HTTPS launch profile retain HTTPS redirection.

Inventory is documented in [phase7-inventory.md](phase7-inventory.md), scanning in [phase8-scanning.md](phase8-scanning.md), sales in [phase9-sales.md](phase9-sales.md), and printable invoices/company settings in [phase10-invoices.md](phase10-invoices.md). Sales includes basic warranty registration for serialized units. Full warranty/service workflows and other settings remain later-phase work.

Daily cash closing, backup/restore scripts, the Windows backup schedule and the
business-wide language configuration are documented in [database setup](../database/README.md#daily-cash-closing-and-shop-readiness-migration-035).
Apply migration 035 manually after 034 before using cash closing, then migration
036 for the top-bar language dropdown. Restart the app after rebuilding.
Review the
[physical shop acceptance checklist](phase8-pilot-signoff.md) before rollout.

Run automated server and browser checks with `tests/Run-ShopPilotChecks.ps1`.
The browser suites require Microsoft Edge and the Playwright runtime restored by
building `StockFlow.UiChecks`. They use rendered pages with mocked APIs.
`-WithDatabase` additionally tests real cash history and backup/restore against a
disposable PostgreSQL server on `127.0.0.1:55441`; it creates and drops GUID-named
test databases and never reads the application's configured database connection.
Results and detailed logs are under `tests/bin/shop-pilot-results`.
