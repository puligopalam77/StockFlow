# Phase 7 SQL for manual execution

1. Complete Phase 6 setup by running `migrations/008_phase6_purchases.sql` in **StockFlow_DB**.
2. Run `migrations/009_phase7_inventory.sql` to add the inventory indexes and `vw_current_stock` view.
3. Open `queries/phase7_inventory_queries.sql` and run the individual SELECT queries for balances, low stock, history, available units, serial lookup, product search, and serial/ledger reconciliation.

The migration does not change existing movement history. It refuses ambiguous legacy `ADJUSTMENT` or `SERVICE` types, and other unknown types, instead of silently treating those movements as zero. Review their intended direction before using the new view. It does not automatically rename unit statuses or rewrite stock balances.

Balances use BIGINT to avoid integer overflow. If an older `vw_current_stock` already exists with an INT result column, PostgreSQL will reject changing its type via CREATE OR REPLACE; review dependent objects before replacing that older view. No objects are dropped automatically.

Adjustments, damage, and returns must eventually be performed by transactional application operations that lock and validate available stock, update serialized-unit state where applicable, and append movements. Standalone INSERT examples from the attachment are intentionally not supplied as a batch to run: doing so without those checks could corrupt stock history.

The application screens and APIs are now implemented; see [Phase 7 inventory](../docs/phase7-inventory.md). These SQL files remain the database setup and standalone read queries. Neither migration has been executed against your application database by the assistant.
