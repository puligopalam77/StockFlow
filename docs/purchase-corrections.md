# Purchase editing and corrections

Apply **063_purchase_corrections.sql manually after 062**, with the application/workers stopped and a backup, then restart the application. The migration preserves document/item IDs, existing receipts, payments, stock and barcodes. It adds optimistic edit versions, internal references and an append-only purchase change ledger. It does not rewrite historical purchases.

Purchase Orders lists View and Edit for authorized users. Purchase Details provides:

| Status | Available action |
| --- | --- |
| Draft (including unreceived purchase orders) | Edit Purchase opens the full existing order form. Review the changes and enter a reason. Drafts with receipts, payments or returns cannot use the full replacement editor. |
| Partially received | Edit Purchase changes invoice number/date, notes, internal reference and attachment. Edit Remaining Order changes ordered quantities without replacing receipt-linked item IDs; quantities cannot fall below good plus damaged already received. |
| Completed | Edit Purchase changes safe details. Correct Purchase offers prices/charges and stock/damage adjustments, subject to permissions and dependencies. |
| Cancelled | View only. |

All edits require the explicit PURCHASES EDIT permission. PAY does not grant editing. Financial corrections also require PURCHASES APPROVE. Linked stock corrections require PURCHASES EDIT, PURCHASES APPROVE and STOCK_ADJUSTMENT ADJUST. Existing view, location, shop and business-profile access checks remain in force. Serial selection additionally uses the existing Inventory read access.

The correction dialog requires a reason and shows previous/new values before saving. Each correction records the signed-in actor, time, old/new snapshots, financial delta and reason. Replaced invoice files are retained in the history storage. Existing received quantities, batch/serial identities and barcode values are not directly overwritten. History shows the latest 100 changes; the ledger retains earlier entries as well.

Financial corrections recalculate line totals, subtotal, tax, invoice discount, delivery/other charges and supplier outstanding within one transaction. They retain original receipt valuation snapshots. Corrections are blocked if supplier returns exist, if the new total is below recorded payments plus returns, or if relevant variants have outbound movements since purchase creation. Such cases require an accounting review rather than restating historical sales/stock costs. There is no automatic retroactive cost rewrite or supplier refund.

Stock corrections use the existing adjustment ledger and audit ID in the same transaction as the purchase history entry. Reduce stock, add missing stock or mark stock damaged. Outgoing quantities must be available and cannot exceed that purchase item's net received quantity after corrections and returns. Serialized outgoing adjustments require available units from the selected purchase item; incoming adjustments require unique new serials. Original receipt quantities stay visible, and the amount summary separately shows net stock corrections. Stock corrections do not change invoice totals. Financial corrections are a separate reviewed action. Product/batch/serial replacement is not offered as an unrestricted field edit.

Concurrent changes are protected by a purchase version and row locks. Receipts advance the purchase version. Correction and stock-adjustment retries reuse their request ID and payload. Server-side dependency and stock checks run again when saving, regardless of what the dialog displayed.

Endpoints: GET/POST `/api/purchases/{id}/changes`; existing PUT `/api/purchases/{id}` for complete draft edits; existing POST `/api/inventory/adjustments` with a purchase/item link for stock corrections. History is append-only for the runtime role. Original invoice download and receipt/barcode workflows remain available.

Validation:

- Build `StockFlow.MasterDataChecks`, then run `--purchase-correction-checks` and `--purchase-receiving-checks` for API permission/CSRF checks and current Razor fixtures.
- Run `purchase-details-offline.cjs` and `purchase-receiving-offline.cjs` with the Playwright Node runtime for review/save dialogs, retained request IDs, history, order edits, existing receiving and responsive/print behavior. Browser APIs are mocked.
- `--purchase-correction-database-checks` accepts only `STOCKFLOW_PURCHASE_CORRECTION_TEST_CONNECTION` pointing at the disposable local `purchase_corrections_test` database on port 55439. It checks the real migration/repositories, recalculation, replay, concurrency versions, tenant/location rejection, stock availability, partial-order bounds, draft audits and attachment retention. It never reads application database configuration.
