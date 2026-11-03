# Multi-business Phases 5 and 6: retail transactions and rollout verification

Phase 5 connects the category/variant work to receiving, billing, inventory and
transfers. Phase 6 supplies regression checks and manual rollout acceptance. No
separate Phase 6 feature was defined in the saved multi-business documentation;
this release uses Phase 6 for verification and deployment readiness.

The four profiles remain Electrical, Bike Accessories, Clothing and Footwear.
There is one stock ledger. Quantities are still whole sale units: Pair means one
pair, and Box/Pack Size remains the existing metadata. No box-to-piece or pair-to-
individual-footwear conversion is introduced.

## What changed

- Receiving search includes registered attribute values. Suggestions, receipt
  lines and bulk variant inputs show readable attributes/SKU/unit. Bulk quantities
  are all validated before any line is added, preventing a partly added cart when
  a later quantity is invalid. Registration still opens the existing Products page.
- Barcode scans include SKU, attributes and unit, matching manual catalog lookup.
  Billing shows the same identity for scanned and manually chosen items. Repeated
  scans increase only the exact variant line. Other variants remain separate.
- New shop profiles must explicitly choose the variant for multi-variant products
  when purchasing, quoting, selling or adjusting stock. Product/variant membership
  is checked before those API writes. Transactional variant resolution also rejects
  ambiguous product-only requests for receiving, opening stock and purchases/sales.
  Electrical integrations retain their existing DEFAULT-variant fallback.
- Existing retry/request-ID processing is retained. Identity checks include inactive
  rows so an otherwise valid retry is not rejected merely because the catalog item
  was deactivated after a successful post. Stock writers still validate activity
  for new transactions after checking their saved request IDs.
- Inventory and transfer selection show attributes and units. Inventory search
  already supported attribute values; that search behavior is retained. Current
  stock views use current catalog attributes; saved financial documents use snapshots.
- Migration 049 captures attributes and SKU when new sale/purchase items are inserted.
  Sales also capture unit; purchases capture product name and keep the existing
  purchase unit field. Snapshots come from the server catalog, not browser values.
  Updates cannot rewrite those details or change an item's product/variant/tenant.
- Invoices and sales details display saved attributes/SKU/unit. Purchase details and
  purchase receipt history show the purchase item snapshot. Receipt history uses
  the saved purchase variant name rather than a subsequently renamed catalog variant.
  Existing invoice names, prices, taxes and totals keep their original behavior.

Old documents are not backfilled with today's attributes. Their new attribute
snapshots stay empty and SKU stays null. Existing legacy-name fallbacks remain.
Opening-stock/return/count history does not gain retrospective attribute snapshots
in this release. Return quantities, transfer stock operations, taxes, discounts,
warranty/service and reports continue to use the existing engine and permissions.

## Database scripts: execute manually

No database scripts or application-database writes were executed during development.
The application and automated runner never apply these scripts.

1. Confirm migrations through **048** are applied. Back up the intended database.
   Stop application writers. In
   [retail_phase56_verification.sql](../database/queries/retail_phase56_verification.sql),
   execute/export the section labelled **counts/totals BEFORE 049**. It uses only
   existing columns. Do not execute its later snapshot queries before the migration.
2. As PostgreSQL superuser, execute the entire
   [049_retail_variant_document_snapshots.sql](../database/migrations/049_retail_variant_document_snapshots.sql).
   It adds columns and two triggers. It does not change document money, ledger
   quantities, catalog identities, tax settings, menus or permissions.
3. Run the full read-only verification file. Expect snapshot columns on sale and
   purchase items and two enabled `zz_retail_variant_snapshot` triggers. While
   writers remain stopped, compare counts, financial totals and movement groups
   against the before export. They must match. First deployment has no captured
   SKU snapshots until new documents are created.
4. Restart the updated application and follow the test-company steps below.
   No additional Phase 6 migration is required.

The updated reader tolerates a database without 049, but new document snapshots
are only captured after 049 is applied. Deploy SQL before allowing new transactions.
Do not remove the snapshot columns/triggers after adoption. A rollback requires
coordinated application/database recovery from the deployment backup.

## Steps to verify in a test database

Repeat steps 1–8 separately for Clothing, Footwear and Bike Accessories using the
Phase 3/4 registered variants. Use new/empty test companies for different profiles;
do not switch a populated Electrical company or delete products to unlock profiles.

1. Register one product with two variants and distinct SKUs/barcodes, for example
   Clothing M/Black and L/Black, Footwear UK 7/Black and UK 8/Black (unit Pair), or
   helmet M/Black and L/Black with vehicle fitment. Registration must add no stock.
2. Receive 2 units of A and 3 of B using Add Variants. First try an invalid quantity
   on B: A must not be partially added. Correct B, review and confirm. Verify exact
   variant IDs, attributes and unit are visible and stock becomes A=2, B=3.
   Search by fabric, material or vehicle fitment to verify attribute lookup.
3. Receive another test delivery with damage. Check accepted = received − damaged,
   only accepted stock is available, and damaged quantities stay separately recorded.
   Existing supplier-invoice pricing includes damaged deliveries until a return or
   credit is recorded; this phase does not change that financial rule.
4. Bill A twice by scanning its barcode, then B once. Expect two cart lines with
   quantities 2 and 1, not a combined product-level line. Confirm payment and check
   A=0, B=2. Attempt another sale of A: it must fail for insufficient stock. Compare
   invoice item IDs, prices, tax, discount, payment and whole-unit quantities.
5. Open both A4 invoice and 80 mm receipt. Confirm attributes/SKU and Pair for
   footwear. Edit an allowed catalog detail such as a common attribute consistently
   across variants, SKU, selling price or product unit. Reopen the saved invoice and
   purchase details: their captured identity/unit/prices must remain unchanged.
   Try updating snapshot columns directly only in the test database: expect rejection.
6. Interrupt a response after a completed receipt/sale, then retry the saved request.
   The original request ID must return the original result without a second stock
   movement. Also test after deactivating that variant. Existing hardware/scanner
   retries must retain their normal behavior.
7. Try product-only requests for the multi-variant product through purchase, sale
   quote, sale, stock adjustment and opening-stock APIs. Expect rejection. A variant
   from another product/company must fail. Exact IDs should post through the existing
   stock rules. A single-configuration product keeps its normal fallback.
8. Adjust/count only B, and transfer only B between two authorized locations. Confirm
   A stays unchanged, source/destination balances reconcile, and each operation
   appears in existing stock reports. Test a return against its original sale line
   and check that only that exact variant returns to available stock.
9. Verify company and location isolation with two users. A user without purchase
   APPROVE cannot receive. Missing sales/stock/transfer permissions remain denied;
   hidden buttons must not permit direct API writes. New shop profiles must continue
   to hide/deny Electrical warranty and service routes.
10. In Electrical, run battery/inverter/geyser registration, serial receipt and sale,
    warranty lookup, service parts and a serialized transfer in the test database.
    Existing serial matching and warranty links must remain intact. Compare the
    Electrical baseline and ledger groups against the deployment export.
11. Check desktop and phone widths, a real shop scanner and both printer formats.
    Run the read-only verification queries and record the evidence below before
    deciding that a shop is ready for live use.

## Phase 6 regression runner and acceptance evidence

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-MultiBusinessPhase56Checks.ps1
```

The runner builds to an isolated output folder. It runs retail transaction API/DTO
checks, Phase 3/4 category checks, Phase 2 profile checks, Electrical regressions and
browser suites for registration, receiving, purchase orders, billing, inventory,
transfers and service. Results/logs are in `tests/bin/retail-phase56-results`.

The new browser scenarios cover each new profile from bulk receipt to barcode
billing, invoice and stock/transfer display using a mock ledger. They verify UI
payload IDs and behavior; they do not certify PostgreSQL posting or concurrency.
The result JSON explicitly keeps database and physical acceptance **PENDING**.

For each shop/profile, record the application build, database backup reference,
048/049 execution evidence, test document IDs and stock reconciliation, authorized
location/user checks, retry/rollback evidence, scanner/printer results, reviewer and
date. Do not treat automated success as a completed live-shop sign-off. Database
snapshot/trigger behavior, RLS, concurrent stock writers, real returns/transfers,
and physical equipment remain manual acceptance work until those steps are performed.
