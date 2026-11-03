# Common inventory: deployment and operation

The application now tracks stock by product variant. Quantity stock has no physical-unit rows; serialized stock has one row per physical unit. Both can exist under one product. Existing product IDs, sale prices, invoice snapshots, warranty links, and positive movement quantities are retained.

## Execute these SQL files manually

No application-database migration was run by the development work. Integration tests used a separate disposable PostgreSQL cluster on port 55441.

1. Stop the application and back up the database. Confirm migrations through `017_administration_security.sql` have already been applied.
2. Run [preflight queries](../database/queries/variant_inventory_preflight.sql). Resolve any SKU/barcode collisions ignoring case and surrounding spaces. Keep the totals for comparison.
3. Execute the whole [018_common_inventory_variants.sql](../database/migrations/018_common_inventory_variants.sql) file. It creates variants and barcode mappings, backfills a DEFAULT variant for every product, and links existing records. It verifies that stock totals are unchanged before committing.
4. Execute the whole [019_purchase_receipts.sql](../database/migrations/019_purchase_receipts.sql) file. It adds partial receipts and marks historical completed purchases as fully received without creating stock movements.
5. Run [validation queries](../database/queries/variant_inventory_validation.sql), compare totals, then start the updated application.

Both migrations are transactional, one-time scripts. If a script fails, issue `ROLLBACK` in that query session, correct the reported source-data issue, and rerun that script. Do not rerun a successfully committed script. Do not run the updated application until both migrations succeed. Rollback after deployment requires a coordinated database backup restore and previous application version, because new transactions can contain additional variants and partial receipts.

## Screens and workflows

- **Products → Variants and labels** (`/Products/Variants`): create or edit size/color/model variants with independent SKU, prices, tax, warranty term, minimum stock, active status, and tracking. Enter attributes as one `Name=Value` per line. Attribute names are unique ignoring case; each has one value. The application stores the flexible attributes as a JSON object rather than introducing clothing-specific columns.
- Existing product-editor commercial fields edit the DEFAULT variant. New products automatically receive a DEFAULT variant. Tracking cannot change after a variant has purchase/stock/unit history. Deactivate old variants rather than repurposing them.
- **Purchases**: choose exact variants, or use Bulk variant quantities to add a size/color grid. Purchase line discounts are separate from invoice discounts and catalog selling prices. Prices exclude tax; line discount applies before tax, and the existing invoice discount applies after line tax.
- **Receiving**: save an order as DRAFT without stock. On its details page, enter the quantities physically arriving and the receipt date/invoice. A partial delivery sets PARTIALLY_RECEIVED; the last delivery sets COMPLETED. Receipt records retain quantities, serials, costs, tax, and proportionally allocated line discounts. Posted receipt/order lines cannot be rewritten. “Complete purchase” is the shortcut for receiving the entire draft immediately. Historical completed purchases remain complete and show as legacy receipts.
- **Serialized receiving**: exactly one distinct serial per received unit. Each stock receipt, its physical units, and ledger entries commit together. A serial belonging to a previous purchase cannot be received as a new unit.
- **Labels**: quantity variants can reuse manufacturer barcodes or generate stable `SFV…` internal codes. Print 1–500 copies using the same Code 128 barcode; reprints do not generate a new identifier. Purchase details link to labels using received quantity. Internal generation is optional.
- **Individual unit identifiers**: on Variants and labels, scan an existing serial to store a separate unique manufacturer-unit barcode and/or generate a stable `SFU…` code. Serial, manufacturer barcode, and internal barcode all locate the same physical unit. Shared model barcodes belong on the variant, not on every physical unit. Existing legacy unit barcodes remain searchable.
- **Billing**: repeated quantity-variant scans increase that cart line's quantity. Different variants of one product remain separate. Serialized sales select the exact unit. Unit/variant correspondence, stock and availability are checked transactionally. Variant names, actual prices, taxes, and warranty terms are captured on the sale for historical invoices.
- **Inventory and reports**: stock/low-stock balances, movement history, and unit selection work by variant; product/category dashboard summaries aggregate these balances. Search inventory by name, SKU, barcode, category, brand, or attribute value. Select a balance row to filter its history and units. Inflow/outflow lists show variant names and retain date/movement filters. Stock Outflow remains a monitoring screen.
- **Service and warranty**: service intake retains the selected variant, spare parts consume the exact variant, and replacements require an available unit of the original variant. Existing unit history remains linked.

All existing view/create/edit/approve permissions and CSRF protections apply. Barcode generation requires PRODUCTS/EDIT; receiving (including immediate completion through create/edit) requires PURCHASES/APPROVE. Adjustment permission remains INVENTORY/APPROVE. Catalog price changes require product edit permission. Sales retain their existing transaction-price editing behavior.

Barcode lookup trims surrounding whitespace and ignores case. It rejects a code matching multiple distinct variants/units rather than choosing an arbitrary result. Use the validation query to inspect historical conflicts. Barcode aliases are retained when new aliases are added, preserving labels already in circulation.

## Compatibility and remaining roadmap items

The default-variant fallback is retained for existing integrations that submit only `productId`. New browser flows submit `variantId`. For physical-unit operations the migration's compatibility trigger derives the variant from the unit and rejects mismatched product/variant IDs. Product-level stock is now an aggregate view. Apply migrations in a maintenance window; running old writers alongside the new application is not supported.

This implements the common inventory model and the existing modules. The proposal's future extensions—multi-branch/location transfers, tenant isolation for unrelated businesses, batch/expiry tracking, unit-of-measure conversions, promotions, and dedicated customer/supplier-return inspection workflows—remain separate future work. This version continues to operate as one business with one stock location; do not use its aggregate balances as independent branch balances. Flexible attributes do not yet have a centrally managed attribute-definition/value dictionary.

## Verification

- `dotnet build StockFlow.sln --no-restore`
- `dotnet run --project tests/StockFlow.MasterDataChecks` checks existing application validation, authentication, permissions and CSRF without database writes.
- `dotnet run --project tests/StockFlow.MasterDataChecks -- --variant-database-test` requires an explicitly started disposable PostgreSQL server at `127.0.0.1:55441`, user `postgres`. It creates a uniquely named `variants_test_*` database and tests migration/backfill, receipts, retries, stock races, serials, taxes, invoice snapshots, adjustments, service parts, warranty replacements, and ambiguous scans. It never uses the application's configured connection.
- `dotnet run --project tests/StockFlow.MasterDataChecks -- --variant-ui-fixtures`, then run `tests/StockFlow.UiChecks/variants-offline.cjs` with the Playwright-bundled Node executable. These browser checks render real Razor pages with mocked APIs, including attributes, printable labels, variant purchase prices, repeated billing scans, and partial receipt submission.

Printer hardware and physical scanner output still require an on-site label test. Browser automation checks rendering and request payloads, not printer calibration.
