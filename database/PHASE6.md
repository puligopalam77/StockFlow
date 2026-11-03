# Phase 6 purchases and stock receipt

Run `migrations/008_phase6_purchases.sql` manually in **StockFlow_DB**, after migration 007. It adds draft serial storage, a serial-tracking snapshot, the purchase-item link on product units, foreign keys, validation constraints, and case-insensitive serial uniqueness. Existing duplicate serials or invalid references cause the migration to stop without committing. No application startup migration is performed.

Use **Purchases** in the Admin navigation. Add an active supplier, products, quantities, prices, and tax. Scan a product barcode to add a line, or select a product. For serialized products, enter one serial per line or scan into that item's serial scanner and press Enter. Serial numbers are trimmed and must be unique across the entire receipt, ignoring case.

- **Save draft** persists the header and items without stock movements. Serial capture may be incomplete.
- **Edit draft** updates the draft; completed purchases cannot be edited or deleted.
- **Complete purchase** requires exactly one serial per serialized unit. Quantity-only items must not include serial numbers.
- Completion inserts physical units only for serialized products, posts PURCHASE inventory movements, and writes a completion audit row in the same database transaction.
- Retrying completion of the same purchase is safe; a database row lock prevents concurrent double posting. A failed direct completion leaves no new purchase, items, units, movements, or audit rows. A failed draft completion leaves its original draft intact.

The server calculates totals using decimal arithmetic: prices and tax percentages are rounded to two places, line taxes are rounded away from zero, and the header sums rounded line amounts. Discount applies after tax and cannot exceed subtotal plus tax. Missing received dates default to the purchase date on completion. User IDs come from authentication claims rather than the request body.

Purchase numbers use the existing PostgreSQL BIGSERIAL sequence: `PUR-year-id`. Gaps after rollback are normal. Do not reset the sequence or manually submit purchase IDs. Reposting a new create request is a new purchase; after an uncertain network response check the purchase list before submitting another receipt.

## APIs

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/purchases` | List purchases |
| GET | `/api/purchases/{id}` | Header, items, and captured serials |
| POST | `/api/purchases` | Save draft; set `completeImmediately: true` for atomic completion |
| PUT | `/api/purchases/{id}` | Replace draft; optionally complete in the same transaction |
| POST | `/api/purchases/{id}/complete` | Complete a saved draft, safely retryable |

All routes and pages require Admin. Writes also require the anti-forgery token. There is no DELETE endpoint, cancellation, or reversal workflow in this phase.

## Verification

The test suite's `--purchase-database-test` mode requires an explicitly configured, disposable PostgreSQL database on `127.0.0.1:55439` named `phase6_test`, using `STOCKFLOW_TEST_CONNECTION`. It refuses the application database. It applies schemas and migrations only to that isolated database, then checks mixed receipts, monetary totals, duplicate/count validation, complete rollback, draft behavior, concurrent completion, and unique purchase numbering. Use a fresh test database for each run.

The regular `tests/StockFlow.MasterDataChecks` suite checks Swagger discovery, purchase page rendering, anonymous/role restrictions, and empty-request validation without database writes.

Phase 7 SQL is supplied separately in `migrations/009_phase7_inventory.sql` and `queries/phase7_inventory_queries.sql`. The inventory screens and adjustment APIs are now available; see [Phase 7 inventory](../docs/phase7-inventory.md).
