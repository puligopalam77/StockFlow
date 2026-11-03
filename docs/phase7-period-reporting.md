# Phase 7: Daily, weekly and monthly reporting

Open **Reports → Period Report**. Today, This Week, This Month and Custom dates use the same date controls on the report hub and the period report. This Week starts on Monday and runs through today; This Month starts on the first and runs through today. Custom dates include both endpoints. Dates are chosen in the browser's local calendar and compared against the stored application timestamps. Reports accept ranges up to ten years.

The current store/location comes from the signed-in session. Staff have their assigned sales location and cannot select another store or pass an alternative store through query parameters. Owners/admins use the existing top-bar store controls to select one location, all locations in an authorized store, or consolidated authorized stores. Existing database RLS remains effective; company transit appears in consolidated stock only where location access permits it.

## Stock reconciliation

Rows are grouped by location and variant, including inactive items with historical movements. Opening is the net ledger balance before the start day. Opening added is stock registered as OPENING_STOCK during the period. Receipts are PURCHASE movements. Sales quantity is SALE movements. Adjustments, transfer in/out/loss and other movements are separate. Damage belongs to adjustment/damage out, including transfer damage; this offsets the incoming damaged transfer quantity. Other inflow covers customer returns/service in; other outflow covers supplier returns/service out/warranty replacement.

Closing is the net ledger balance before the day after the end date. The row check verifies:

`opening + opening added + receipts + adjustment in + transfer in + other in − sales − adjustment/damage out − transfer out − transfer loss − other out = closing`

Invalid movement types/quantities cause a review status. Transfers never increase receipt or sales columns. Consolidated totals include transit where authorized, so internal dispatch/receipt pairs cancel at company level; losses/damage remain reductions. Stock reports follow posting timestamps (`inventory_transactions.created_at`), preserving the same rules as current inventory balances.

## Sales, collections and credit

Only completed invoices contribute to sales value and invoice quantity, by `sale_date`. Value includes invoice discounts and tax. Invoice checks compare item values to invoice totals and collected/unpaid balances, detecting mismatched item totals or overcollection. They also compare invoice quantities with SALE movements referencing those invoices during the period; backdated/missing ledger movements cause a review status. The ledger quantity shown in financial reports is limited to these completed invoices.

Collections sum actual non-CREDIT payments by `payment_date` within the period, including payments against older invoices. CREDIT payment entries represent an amount owed and do not count as money collected. Outstanding credit is the positive invoice total less actual payments through the end date, across all completed invoices existing by that date. The outstanding invoice list reconciles to that summary and includes customer/invoice details. It is an end-date balance, not only credit created within the selected range.

These reports read existing payments; Phase 7 does not introduce a debt collection entry screen. Later payments recorded through an authorized payment workflow reduce outstanding credit. Backdated movements or payments can change historical reports. Stock posting dates and invoice dates can differ for legacy/backdated data; the optional SQL checks identify invoice/ledger quantity discrepancies.

## Access and deployment

The page and API require REPORTS / VIEW. Stock data additionally requires INVENTORY / VIEW; financial and customer invoice data requires SALES / VIEW. Missing permissions suppress the corresponding repository queries and page sections. Requests without either stock or sales access are rejected. All sections read in one read-only repeatable-read database transaction, so concurrent posts cannot produce inconsistent sections.

**No new database migration is required.** Keep the existing baseline through 027 and restart the rebuilt application. No SQL runs automatically and the application database was not accessed for development. Optional manual read-only checks are in [period_reporting_checks.sql](../database/queries/period_reporting_checks.sql); edit its dates for your report. Its first query shows balances; subsequent integrity queries should return zero rows, with any historical exceptions reviewed.

Configuration files and module registration logic remain unchanged. Existing Stock Position and Low Stock links show current balances; date ranges apply to Period Report and sales lists.

## Verification

Build `tests/StockFlow.MasterDataChecks` with `--artifacts-path tests/bin/store-assignment-build`. Run the built checks DLL with `--reports-checks` for date validation, permission filtering and pinned staff scope. Run with `--multistore-database-test` against the disposable PostgreSQL test cluster at port 55441 for date boundaries, opening-stock separation, actual collections, credit reconciliation, store isolation and consolidated transfers. It creates/drops only its uniquely named test database.

Playwright scripts `tests/StockFlow.UiChecks/reports-offline.cjs` and `period-report-offline.cjs` verify actual Razor fixtures, date presets, hub links, custom ranges, report errors, module visibility and desktop/mobile widths. They mock APIs and do not access the application database.
