# Phase 11 warranty management

## Manual database setup

With migrations through **013_phase10_invoices.sql** already applied, execute
[014_phase11_warranty.sql](../database/migrations/014_phase11_warranty.sql)
in your StockFlow application database. Run the entire file: it includes BEGIN/COMMIT.
The application never runs migrations automatically. Restart the application after applying it.
This migration is also required for new sales, which record warranty registration events.

The migration adds coverage fields, claims, an event history, a claim-number sequence,
indexes and replacement stock accounting. It preserves existing warranties.
If duplicate original warranties, duplicate current unit warranties or unsupported inventory
movements cause validation to fail, roll back and review those records before rerunning.
Do not rerun migration 009 after 014: its older stock view excludes warranty replacements.

[phase11_warranty_queries.sql](../database/queries/phase11_warranty_queries.sql)
contains optional read-only lookup, expiry, replacement audit, stock reconciliation and
missing-registration queries. Replace the sample serial/invoice values before running them.

## Using the module

- Warranty lookup accepts a serial scan or an invoice and links to details.
- The existing Scan screen also displays unit warranty status immediately.
- Details shows dates, coverage, eligibility, claims and event history.
- Admin and Service can register missing historical warranties, set coverage, create claims,
  review/approve/reject claims, close repairs and issue replacements. Sales has read access.
- Claims and expiring lists show 100 rows per page. Expiry filters include 7/30/60/90 days.
- New warrantied sales register automatically, including non-serialized items (one warranty
  per sale line). Physical replacement requires a serialized unit.

Eligibility uses PostgreSQL CURRENT_DATE, including both start and end dates. Configure the
database session timezone for the shop's calendar. Cancelled sales/warranties and replaced
original warranties cannot create claims. An in-progress claim blocks duplicate claims.
Claims opened before expiry can still be resolved after expiry.

Replacement requires an approved claim and an available unit of the same active product.
The old unit, replacement unit, stock movement, claim, warranty and linked child warranty
change in one transaction. The original serial is never overwritten.
Stock totals, scanner balances and sales/adjustment validation recognize WARRANTY_REPLACEMENT.
Request IDs protect retries; expected claim status detects stale updates.

The operator must explicitly select the confirmed replacement coverage rule:
continue the original dates, start a full term using the current product warranty months,
or choose a custom end date. There is no default replacement warranty policy.
Continued coverage can already be expired when an older claim is resolved.

## Verification

Build and API checks can run without database writes:

```powershell
dotnet build src/StockFlow.Web/StockFlow.Web.csproj --no-restore -o tests/bin/phase11-build
dotnet build tests/StockFlow.MasterDataChecks/StockFlow.MasterDataChecks.csproj --no-restore -o tests/bin/phase11-checks
dotnet tests/bin/phase11-checks/StockFlow.MasterDataChecks.dll
```

API tests use a fake warranty repository to check roles, CSRF, page rendering and validation.
They do not verify PostgreSQL transactions. After applying the SQL, verify in a test database:

The existing `--sales-database-test` option now also includes Phase 11 migration rerun,
claim retries, expiry, replacement and rollback checks. It requires an explicitly configured
disposable `phase9_test` database at `127.0.0.1:55439` via `STOCKFLOW_TEST_CONNECTION`;
never point it at your application database. These database checks were not executed as
part of this change.

1. Look up an active, expired and cancelled warranty; confirm dates and claim eligibility.
2. Create a claim, retry the same request ID, and confirm one claim/count increment.
3. Approve a claim and replace using an in-stock unit; confirm one outbound movement,
   old unit REPLACED, new unit REPLACEMENT_ISSUED and linked replacement coverage.
4. Attempt replacement with a sold/wrong-product unit; confirm no claim, unit or stock changes.
5. Retry replacement and attempt concurrent claims; confirm no duplicate issue or stock movement.
6. Close a repair, verify retained history and check expiring warranties.
