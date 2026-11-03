# Phase 7 inventory

Open **Inventory** in the sidebar (`/Inventory`). Admin, Sales and Service can search active products by name, SKU or barcode, filter low stock, include inactive products, inspect movement history by date/product, and browse physical units by serial or barcode. History and units use pages of 100 records. Serialized ledger discrepancies are highlighted. Low stock includes balances equal to the minimum stock level.

Admin can post **Adjustment in**, **Adjustment out**, and **Damaged** movements, with a required reason. Quantity-only products use a quantity. Serialized incoming adjustments require one new unique serial per unit; outgoing adjustments require the IDs of available units from the physical-unit list. Incoming corrections do not attach a purchase or invent supplier history. Use Purchases for supplier receipts. Damaged units become `DAMAGED`; other outgoing corrections become `ADJUSTED_OUT`. Neither is available for sale.

Adjustments lock the product and affected units, reject insufficient stock, reject ambiguous legacy movement types, and require serialized units to agree with the ledger. Unit changes, movements and the audit receipt commit together. Concurrent withdrawals cannot overdraw stock. Product locks also coordinate with existing purchase completion. Future sale/return writers must follow the same product locking convention.

Each adjustment requires a client-generated UUID `requestId`. Repeating the same request with the same user returns the original audit receipt without posting again; changed content is rejected. The browser retains an unconfirmed request in session storage for safe retry after a network interruption or reload. Do not delete inventory audit receipts: they provide retry protection as well as history. Posted movements are never edited or deleted through this module.

## Setup

Apply baseline scripts and migrations 007–009 in order when setting up a new database. Migration 009 supplies `vw_current_stock` and normalized serial uniqueness. Existing Phase 7 installations need no additional migration for these screens/APIs. The configured application database passed read-only balance/history/unit checks during this implementation; no application records or schema were changed.

## API

| Method | Route | Parameters / access |
| --- | --- | --- |
| GET | `/api/inventory` | `term`, `lowStockOnly`, `includeInactive`; Admin/Sales/Service |
| GET | `/api/inventory/history` | `productId`, `from`, `to` (inclusive dates), `offset`; Admin/Sales/Service |
| GET | `/api/inventory/units` | `productId`, `term`, `availableOnly` (default true), `offset`; Admin/Sales/Service |
| POST | `/api/inventory/adjustments` | Admin and `X-CSRF-TOKEN`; returns `adjustmentId` audit receipt |

Adjustment JSON contains `requestId`, `productId`, `transactionType` (`ADJUSTMENT_IN`, `ADJUSTMENT_OUT`, `DAMAGED`), `quantity` (1–10000), `reason`, `serialNumbers` and `productUnitIds`. Leave unused lists empty. User identity comes from the login cookie.

Returns and service transfers remain part of their document workflows in later phases; arbitrary movement types cannot be posted through the adjustment API.

## Checks

`dotnet run --project tests/StockFlow.MasterDataChecks` checks page access, role restrictions, validation, and CSRF protection. Add `-- --database-readonly` to exercise real inventory queries without writes.

The existing `--purchase-database-test` mode also checks inventory retry safety, concurrent withdrawals, insufficient stock, serial reconciliation, damage, rollback on duplicate serials, pagination, filtering and inactive products. It requires a fresh disposable database named `phase6_test` on `127.0.0.1:55439` through `STOCKFLOW_TEST_CONNECTION`; it refuses the application database.
