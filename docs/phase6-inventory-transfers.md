# Phase 6: Inventory control and transfers

Inventory now supports product, variant attributes (size/color), SKU, barcode and individual serial searches within the current location. It shows matching variants, low-stock counts and items with no stock. The low-stock screen shows available quantity, minimum and the quantity needed to reach that minimum. Existing minimum-stock settings remain unchanged.

Movement history opens from a stock row or the Show all movement history button. It shows signed quantities, serial identity, reason, user and reference. Transfer references open the corresponding transfer. Source history retains serial identity after a unit moves into transit or another store.

Stock adjustments require both Inventory view and **Stock adjustment / ADJUST** access. Incoming serialized adjustments require new serials; outgoing or damaged adjustments use available unit checkboxes or scan their existing/internal labels. An adjustment records the reason and audit receipt. Insufficient stock and unavailable serials are rejected. Repeating a saved request returns the original receipt without adding another stock movement.

Transfers use the current location automatically. Employees have their assigned location; owner/admin accounts retain authorized review controls. Send from this location pins the source. Request goods for this location pins the destination. Only the other endpoint is selected. The transfer list offers active, incoming, outgoing and recent filters, showing the latest 200 accessible transfers.

1. Create a draft with items and quantities, then submit the request.
2. An authorized user at the source approves and dispatches it. Serialized goods are selected or scanned by their labels, rather than entering internal unit IDs.
3. An authorized user at the destination records the quantity physically received, selecting/scanning serials where applicable. Enter zero or leave serials unselected to skip an item in a partial receipt.
4. For damage/loss, select that condition and enter a reason, then use Record damage / loss. Post good goods with Receive goods separately; skip lines that belong to a different resolution in that action.

The detail shows requested, dispatched, received, damaged, lost and remaining transit quantities. Dispatch deducts source stock and credits transit. Receipt removes transit stock and credits the destination. Damage/loss closes only the resolved quantity, without adding saleable stock. Transfers create neither purchase headers nor sales/invoices. Cancellation is available at the source before dispatch.

## Manual database deployment

The application does not apply SQL automatically. Your application database was not accessed or modified for this development work.

1. With existing migrations through **026** applied, stop StockFlow and back up the database.
2. Connect to the application database as a PostgreSQL superuser and execute [027_inventory_transfer_actions.sql](../database/migrations/027_inventory_transfer_actions.sql). It contains its own transaction.
3. Restart StockFlow. Optionally execute [inventory_transfer_checks.sql](../database/queries/inventory_transfer_checks.sql) as administrator; it is read-only. Its overview/permission results are informational. Integrity queries should return no rows; review any historical exceptions.

Migration 027 aligns database transfer authorization with the action permissions introduced in 025. It adds a serial snapshot to movement records, backfills that metadata from existing units, and supplies an authorized transfer unit-label lookup. It does not grant permissions or change stock quantities. Existing action permissions remain effective: Stock transfers VIEW, CREATE, APPROVE, DISPATCH, RECEIVE and CANCEL. APPROVE also authorizes recording damage/loss. An active VIEW grant is required with each action. Use existing Administration controls to assign these actions when necessary.

Approve/dispatch/cancel require the current source location. Receipt/discrepancy require the current destination. Consolidated views are read-only. Permission revocation takes effect on subsequent requests and cannot be bypassed by old module permission rows.

## Interrupted requests

Adjustments and transfer actions save their exact payload and request ID in browser session storage, scoped by tenant, user and location. An unconfirmed network/server/authentication result freezes further entry and offers Retry saved adjustment/action. A retry submits the same request; permission failures do not silently discard it. Do not re-enter the quantity as another action while confirmation is pending.

An older unscoped pending action from the previous screen blocks new posting. Reconcile its result against movement/action history before clearing that older browser entry; automatic resubmission cannot safely infer the original store/user. Saved requests are limited to that browser tab/session, so confirm an uncertain result before closing or clearing it.

Configuration pages, scripts, styles and registration logic were not modified.

## Verification

- Build the checks project: `dotnet build tests/StockFlow.MasterDataChecks --no-restore --artifacts-path tests/bin/store-assignment-build`.
- Run its built DLL with `--inventory-control-checks` for real Razor screens and HTTP permission guards; this creates browser fixtures without database access.
- Run `tests/StockFlow.UiChecks/inventory-control-offline.cjs` using the project's Playwright Node binary for stock/low-stock screens, serial scanning, adjustments, dispatch/receipt, exact retries and mobile layouts.
- Run the checks DLL with `--multistore-database-test` against the disposable PostgreSQL cluster at `127.0.0.1:55441`. It creates and removes only its uniquely named test database, applying the complete stored baseline through 027. It verifies RLS isolation, action revocation, partial receipts, damage/loss, serial transfer, source history snapshots, inventory barcode search, adjustment idempotency, insufficient stock and transfer separation from purchases/sales.

Database tests use disposable databases; they do not establish which migrations are applied to your running installation.
