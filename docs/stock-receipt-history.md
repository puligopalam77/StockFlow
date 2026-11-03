# Stock receipt history

Migration `031_stock_receipt_history.sql` adds permanent `GRN-yyyy-nnnnnn` identifiers to purchase receipts, permanent identifiers to opening-stock intakes, receipt status metadata, history indexes, and the database navigation name **Stock receipt history**. Apply it manually after migration 030 while StockFlow is stopped.

Completed legacy purchases that have purchase ledger movements but no receipt header receive one documentary GRN during the migration. Existing ledger quantities are linked to that GRN; no new inventory movement is posted.

`/StockInflow` is a single-active-store view. Database row-level security continues to enforce the signed-in user's active store; the screen does not expose a store selector or transfer movements. The Receipts tab groups purchase receipts, customer returns, opening stock and approved positive adjustments into one document row. The Item movements tab provides product-level investigation for those same sources. Service-customer property, service movements and inter-store transfers are excluded.

Purchase receipts use their GRN as the primary history identifier and retain the related purchase order number in details. This allows one purchase order to have multiple receipt records for partial deliveries. Completed records remain immutable in the UI. The status schema reserves `REVERSED` for a future authorized reversal workflow; migration 031 does not add a reversal action.

Run `database/queries/stock_receipt_history_checks.sql` after the migration. Every check before the final summary must return no rows. The final query reports the available receipt sources for reconciliation.
