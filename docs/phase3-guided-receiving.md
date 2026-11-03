# Phase 3: guided stock receiving

Receive Stock / Receive goods opens `/Receiving`: receipt details, add items,
then review and save. Staff use their automatic location from Phase 1; there is
no staff store picker. Owners/admins must choose one location before posting.

Scanning a registered model/variant fills its details and purchase cost. Repeated
scans increase quantity. Existing physical-unit identifiers are rejected for new
receiving. Serialized goods require exactly one new distinct serial per unit.
Their serials can be scanned directly into the item's serial field.

An unknown barcode opens inline product registration for authorized users and
preserves the scanned value, including leading zeros. Choose whether that code
identifies a model or an individual serial: serials are not assigned as shared
model barcodes. New registration creates catalog details but adds no stock until
the receipt is saved. Products with Has Variants enabled can receive additional
size/color variants through the same dialog without visiting Configuration.

Opening stock is a separate mode for goods already on hand when adopting the
tool. It does not create a supplier purchase. It records date, quantities, costs,
tax configuration, product/variant name snapshots and serials. Ledger movements
use existing ADJUSTMENT_IN semantics with reference_type OPENING_STOCK, so existing
stock views remain compatible. Existing adjustment-inclusive inflow summaries
include these quantities; dedicated financial/report separation remains Phase 7.
The selected receipt date is retained; ledger creation timestamps reflect posting.

The Advanced section retains existing purchase orders and partial deliveries.
Supplier creation and discounts use existing setup/advanced purchase screens.
Configuration module pages, editors and styles have not been changed.

## Manual database deployment

1. Stop StockFlow and back up the application database. Confirm migrations
   through 025 have been applied; do not rerun completed scripts.
2. Manually execute the entire
   `database/migrations/026_guided_stock_receiving.sql` once as PostgreSQL superuser.
   It adds stock_intakes and stock_intake_items, tenant/location foreign keys,
   row security, runtime grants and the unique retry identifier. Existing stock,
   catalog records and permissions are unchanged. No migration runs at startup.
3. Restart the rebuilt app. Assign the permissions below and test one receipt.
4. Optionally run `database/queries/guided_receiving_checks.sql` for read-only
   receipt, opening-stock and ledger reconciliation.

If migration execution fails, roll back that SQL session and investigate before
retrying. Do not use the new posting screen until migration 026 succeeds.

## Permissions

- STOCK_INFLOW / View opens the receiving page; Receive posts supplier receipts.
- STOCK_ADJUSTMENT / View + Adjust permits opening stock.
- PRODUCT_VARIANTS / View permits item lookup; SCAN / View + Scan permits scanning.
- SUPPLIERS / View is needed for supplier receipts.
- PRODUCTS / View + Create and CATEGORIES / View enable new product registration.
- PRODUCTS / View and PRODUCT_VARIANTS / View + Create enable inline variants.
- PURCHASES grants still control advanced orders and viewing purchase details.

Guided receipt posting uses Receive, without requiring Purchase Create/Approve.
Opening-only users still need STOCK_INFLOW / View to open the common screen.

## Retry and atomicity

The browser saves the exact posting request in session storage before sending it.
Network/server failures preserve it and freeze editing. Reloading the same tab
offers Retry saved receipt. It is scoped to business, user and location. Keep that
tab and its storage until the result is confirmed; a new identifier represents a
new receipt. Do not manually re-enter goods while a previous result is uncertain.

The server serializes retries and commits the intake, purchase/receipt (or opening
stock), physical units, snapshots and ledger together. Identical concurrent retries
return the original result; altered payloads are rejected. Invalid quantities or
existing serials roll back the whole posting. A failed registration read retains
the saved product ID so retrying that dialog does not recreate it.

## Verification

- Build and run the default StockFlow.MasterDataChecks suite.
- `--receiving-checks` verifies pages, permissions, CSRF and validation, and writes
  browser fixtures using fake repositories.
- `tests/StockFlow.UiChecks/receiving-offline.cjs` verifies scans, inline registration,
  variants, saved-request retry/reload, serial opening stock and four screen widths.
- `--receiving-database-test` requires an explicitly started disposable PostgreSQL
  cluster on localhost:55441. It creates and removes a receiving_test_* database,
  applies the schema there, and verifies receipt retries, concurrency, serials,
  rollback and store isolation. It never reads the application connection string.

Development verification used an isolated temporary PostgreSQL cluster; no SQL
was applied to the application's database. Physical scanner output still requires
a manual check in the store.

The broader multi-store database suite exposes an existing transfer-dispatch
permission failure (`42501: Transfer action is not permitted`). It was reproduced
with migrations only through 025, excluding 026. Its database transfer helper still
uses legacy grants while migration 025 moved action grants. This separate transfer
issue is not fixed by guided receiving. Reproduce with
`--multistore-database-test --baseline-through-025` on the disposable cluster.
