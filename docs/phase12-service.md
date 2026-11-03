# Phase 12 — Repair / service management

## Manual SQL setup

After applying migrations through **014_phase11_warranty.sql**, run the entire
[015_phase12_service.sql](../database/migrations/015_phase12_service.sql) file in your
StockFlow application database, then restart the app. It contains BEGIN/COMMIT.
The app does not run this migration automatically. No application database writes
were performed during implementation.

The migration adds the service number sequence, parts used, retry receipts, ticket
versions, warranty eligibility at receipt, and indexes. It extends the existing
service history rather than replacing it. Existing history is retained.

Existing duplicate open tickets for a unit, unsupported statuses, negative charges,
expected dates before receipt, or delivered tickets missing delivery timestamps
must be reviewed if validation fails. Roll back a failed migration and correct the
underlying records before retrying. Legacy tickets have unknown warranty eligibility
at receipt; the screen identifies that explicitly.

Optional [read-only service queries](../database/queries/phase12_service_queries.sql)
cover pending/completed tickets, history, technician choices, part movements and
stock reconciliation. Replace the example service number before running a lookup.
The Phase 11 stock view already treats SERVICE_OUT as outbound; no new view is required.

## Screens and permissions

- **Service → New service request** scans a serial or unit barcode, retrieves the sale
  customer and current warranty, and records complaint, technician, expected date and notes.
- For quantity-only products, search for an existing product and customer. Customer
  registration remains available to Admin/Sales in the existing Customers module.
- **Pending services** excludes delivered and cancelled tickets. **Service history**
  includes all tickets and links to a completed-only view.
- Search supports service number, customer name/mobile, product, serial, status, and
  inclusive received-date filters. Lists, parts and history are paged in batches of 100.
- **Details** shows diagnosis, repairs, parts, labour charge, total charge, warranty
  status, dates and the retained change history.
- Admin/Service can create and update tickets. Sales can view/search only. Technicians
  must be active users with an active Admin or Service role.

## Lifecycle and inventory rules

| Current status | Allowed next status |
| --- | --- |
| RECEIVED | INSPECTION, CANCELLED |
| INSPECTION | REPAIRING, WAITING_FOR_PART, READY, CANCELLED |
| REPAIRING | WAITING_FOR_PART, READY |
| WAITING_FOR_PART | REPAIRING, READY |
| READY | DELIVERED |
| DELIVERED / CANCELLED | No further edits |

Every create, repair-detail update, part issue and status change adds history. There
are no normal deletion or history-edit endpoints. Delivered date is set automatically.
An open ticket is unique per serialized unit. Version checks reject stale edits.
Unchanged retries reuse a request ID so an interrupted response cannot create another
ticket or consume the same parts twice.

Serialized intake requires a unit owned through a completed sale, matching the selected
product and sale customer. Walk-in sales can be associated with an existing customer.
An in-stock, replaced or already-in-service unit cannot be received as a customer's repair.
Expired/no warranty does not block service. The server checks any submitted warranty link.
Quantity-only warranty links can also be supplied through the API using the original
warranty ID; the product and sale customer must match.

Intake sets the customer unit to IN_SERVICE without creating a stock movement.
Delivery or cancellation restores its previous SOLD / REPLACEMENT_ISSUED status.
Legacy tickets default to SOLD on return because no previous unit status was recorded.
This does not perform a customer return or make the unit available for resale.

Parts can be issued during INSPECTION, REPAIRING or WAITING_FOR_PART. Choose an active
inventory product; serialized parts require an available unit and quantity 1.
The transaction checks stock, creates a service part row, records SERVICE_OUT, updates
serialized parts to SERVICE_USED, and writes history. It uses the same product locks as
sales and stock adjustments. Failure rolls everything back.

Cancellation after parts have been issued is blocked: consumed parts are retained
and the repair must be completed/delivered. Parts cannot be removed or silently restocked.
Service does not automatically resolve warranty claims or issue warranty replacements.
A unit currently IN_SERVICE cannot simultaneously be issued as a warranty replacement.

Service/labour charge remains editable, even under warranty. Parts are totalled separately
and total charge is labour plus parts. Prices are non-negative and have at most two
decimal places. Payment collection, service invoices and service taxes are outside this phase.
Calendar checks use PostgreSQL CURRENT_DATE; configure the database timezone for the shop.

## Verification

Build and API checks without database writes:

```powershell
dotnet build tests/StockFlow.MasterDataChecks/StockFlow.MasterDataChecks.csproj --no-restore -o tests/bin/phase12-checks
dotnet tests/bin/phase12-checks/StockFlow.MasterDataChecks.dll
```

API tests use a fake service repository and check page/API access, write permissions,
CSRF, filters, amounts, serialized part quantities and transition rules.

Build and API checks passed. The headless Edge browser check also passed using rendered
Razor pages and mocked API responses: scanned intake, expired warranty, locked sale
customer/product, repair charges, part issue, delivery, pending/completed lists, text
encoding and desktop/mobile layout. No live database was used. The build reported
NU1900 because the NuGet vulnerability feed was unavailable; compilation succeeded.

To rerun that browser check with the existing Playwright installation:

```powershell
dotnet tests/bin/phase12-checks/StockFlow.MasterDataChecks.dll --service-ui-fixtures
& 'tests/StockFlow.UiChecks/bin/Debug/net10.0/.playwright/node/win32_x64/node.exe' tests/StockFlow.UiChecks/service-offline.cjs
```

The existing `--sales-database-test` option now includes service transaction tests:
expired-warranty intake, no extra stock movement, duplicate-ticket prevention, part
stock rollback, concurrent retries, totals, delivery and cancellation.
It is guarded to accept only a separately configured disposable `phase9_test` database
at `127.0.0.1:55439` via `STOCKFLOW_TEST_CONNECTION`.
Those database tests were not run during implementation.

After applying the migration, verify in a test database:

1. Scan a sold serial; confirm customer, invoice and active/expired warranty.
2. Receive it and confirm IN_SERVICE with unchanged shop stock.
3. Assign a technician, record diagnosis/repair/charges and follow the status flow.
4. Issue two parts from ten: stock becomes eight, with one SERVICE_OUT movement.
5. Attempt to issue more than available: no part, stock or history changes.
6. Deliver: delivery date is set, unit returns to its previous owned status, ticket
   leaves pending, and all history remains.
7. Check Sales cannot write, stale edits fail, and retries do not duplicate work.
