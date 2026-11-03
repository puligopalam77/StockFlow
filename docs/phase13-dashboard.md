# Phase 13 — Dashboard extension

The existing hero, five inventory cards, stock movement chart, category donut and
recent inflow/outflow tables are retained. The dashboard adds business KPIs, recent
sales/services, low stock, useful links, global search and report navigation.

## Manual SQL

The dashboard requires the earlier migrations through **015_phase12_service.sql**.
There are no new required tables or data migrations for Phase 13.

You may manually run [016_phase13_dashboard_indexes.sql](../database/migrations/016_phase13_dashboard_indexes.sql)
after 015 for optional performance indexes. It does not change business data.
The app never runs this file automatically.

[phase13_dashboard_queries.sql](../database/queries/phase13_dashboard_queries.sql)
contains optional read-only verification queries for the KPI values, category balances,
low stock, seven-day movements and unknown legacy movement types.
No database scripts were executed during implementation.

## Metric definitions

- Products and suppliers count active records.
- Inflow: PURCHASE, CUSTOMER_RETURN, ADJUSTMENT_IN, SERVICE_IN.
- Outflow: SALE, SUPPLIER_RETURN, DAMAGED, ADJUSTMENT_OUT, SERVICE_OUT, WARRANTY_REPLACEMENT.
- Current stock uses the existing Phase 11 stock view, including inactive product balances.
  Category stock shows physical balances for active products, as requested. It is not
  a count of product definitions. Negative balances are flagged; the donut renders positive slices.
- Sales totals and recent sales include only COMPLETED invoices. Amounts are invoice
  totals, not payment receipts. Mixed payments display all recorded payment modes.
- Low stock includes active products at or below minimum; required is max(minimum − stock, 0).
- Pending repairs exclude DELIVERED and CANCELLED and include READY; the separate ready
  count is the subset awaiting collection.
- Expiring warranties use the same eligibility as the Phase 11 expiry list:
  ACTIVE or CLAIMED warranties on completed sales, ending today through day 30 inclusive.
  Replaced/cancelled coverage and cancelled sales are excluded.
- Date windows use PostgreSQL's session calendar. Configure the database timezone for the shop.

The initial dashboard is loaded within a repeatable-read transaction so its panels
share a consistent snapshot. A load failure displays unavailable values instead of
claiming zero totals. The chart loads 7 days, 30 days, 6 months or 12 months, includes
empty periods, and retains the previous chart if a refresh fails.
The older ambiguous ADJUSTMENT movement is no longer given an assumed direction;
reconcile legacy rows using the supplied diagnostic query so totals match the stock view.

## Screens and access

- Inventory and business cards open the corresponding module/filter.
- Stock Inflow and Stock Outflow now open general movement history with date/type/search
  filters and 100-row paging. Recent references link to invoices, services or warranty claims.
- Low Stock opens a paged report. Product links prefill inventory search.
- Global search returns up to ten matches each for products/SKUs/barcodes, serials,
  customers/mobile, invoice numbers and service numbers, with links to the record/module.
- Reports links to the existing operational reports. Users is an Admin-only read-only
  staff directory; account creation/editing is not added by this dashboard phase.
- Admin sees all metrics. Sales has no supplier details, purchases or administration.
  Service sees stock, customers, warranty and service information; sales amount KPIs
  and recent sales are hidden, and the recent-sales endpoint is forbidden.
  Supplier totals are null for non-Admin API consumers, and sales totals are null for Service.
  Invoice lookup links retain the existing invoice-view permissions.

## Dashboard API

GET endpoints under `/api/dashboard`:

| Path | Parameters |
| --- | --- |
| inventory-summary | None |
| business-summary | None |
| inflow-outflow-trend | period: 7days, 30days, 6months (default), 12months |
| category-stock | None |
| recent-inflow / recent-outflow | None; latest five |
| recent-sales | Admin/Sales; latest five completed sales |
| recent-services | Latest five service requests |
| low-stock | offset; 100 rows |
| movements | incoming, type, term, from, to, offset |
| search | term; 2–200 characters |
| users | Admin only |

All endpoints require authentication and use no-store responses. There are no dashboard write endpoints.

## Checks

```powershell
dotnet build tests/StockFlow.MasterDataChecks/StockFlow.MasterDataChecks.csproj --no-restore -o tests/bin/phase13-checks
dotnet tests/bin/phase13-checks/StockFlow.MasterDataChecks.dll
dotnet tests/bin/phase13-checks/StockFlow.MasterDataChecks.dll --dashboard-ui-fixtures
& 'tests/StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe' tests/StockFlow.UiChecks/dashboard-offline.cjs
```

API tests use a fake repository for roles, validation, encoding and page rendering.
Browser tests use rendered Razor pages with mocked APIs. They do not validate live
PostgreSQL data. Compare the supplied SQL results with the dashboard after setup,
including cancelled sales, READY repairs, expiry boundaries and low-stock equality.
Build, API/regression checks and headless Edge browser checks passed. Browser coverage
includes all four chart periods, retaining the chart on errors, movement/low-stock/search
screens, role-specific panels and responsive widths from 320 to 1440 pixels.
The build may report NU1900 if the NuGet vulnerability feed is unavailable.
