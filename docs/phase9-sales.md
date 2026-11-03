# Phase 9: sales, stock-out and payments

## Manual database setup

With baseline tables and migrations 007–009 already installed, execute these scripts in the application database using pgAdmin:

1. `database/migrations/010_phase8_scanning.sql` — normalized barcode and serial uniqueness, if not already applied.
2. `database/migrations/011_phase9_invoice_sequence.sql` — invoice sequence; rerunning does not reset it.
3. `database/migrations/012_phase9_sales.sql` — request IDs for safe retries, historical invoice-name snapshots, indexes and value constraints.

This is the historical Phase 9 setup record, not the current migration baseline (current source includes migrations through 084). Follow the target deployment acceptance plan. The initial application also required `013_phase10_invoices.sql` for expanded invoice snapshots and company settings. See [Phase 10 setup](phase10-invoices.md).

Migration 012 is transactional and repeatable. Existing invalid sale/payment records cause it to fail instead of rewriting history. If a script fails, issue `ROLLBACK;` before resolving the reported records and retrying. No application startup migration is performed. These migrations were tested only against a disposable database, not applied to your application database.

## Using sales

Current New Sale requires a registered customer. Search by name/mobile or create and select one when permitted. Historical walk-in invoices remain readable; customer management links to invoice history.

Scan a product barcode, serial number or unit barcode, or search products manually. Repeated quantity-product scans increment quantity. Serialized model barcodes open an available-unit picker; a physical serial/unit barcode adds that exact unit with quantity one. Duplicate or unavailable units are rejected. The picker shows up to 100 units; use the serial filter to narrow larger inventories. Keyboard scanners should send Enter. Manual code entry works too.

Prices and line discounts are editable. Tax comes from saved catalog defaults or reviewed effective GST mappings. Authorized ordinary-bill corrections require their separate action and recorded reason; arbitrary overrides cannot replace reviewed GST classifications. The quote API calculates displayed totals on the server. Prices/discounts/payment amounts round to two decimal places, midpoint away from zero. Line discounts apply before tax; each line tax rounds separately. Modern allocated invoice discounts reduce the saved taxable base; legacy after-tax documents retain their original semantics. Header subtotal is the sum after line discounts. Completion recalculates everything from current product tax records and rejects a mismatched payment total.

Current checkout collects one full payment: Cash, UPI, Debit Card, Credit Card or Bank Transfer. Electronic references are optional; receipt confirmation is required. New credit and split checkout are blocked. Older split allocations and customer-credit collections remain supported through existing operations APIs. These current rules supersede the initial Phase 9 allocation examples and dated tests below.

**Complete sale** posts the invoice, items, payments, SALE movements and audit row together. Physical units become SOLD and link to the sale. Serialized products with positive warranty months also get a warranty record linked to the sold item/unit. Invoice detail shows serials, payments and warranty dates/status. Product and customer name snapshots preserve the new invoice's labels after later master-data edits; legacy invoices fall back to their existing linked records.

Completion locks products in ascending ID order, then physical units. Stock checks aggregate all lines for each product. Purchases and adjustments use compatible product locks, so concurrent operations cannot oversell stock. Unknown movement types, invalid quantities and serialized ledger discrepancies must be reviewed before selling. Invoice numbers use the sequence (`INV-year-number`); rollback gaps are normal.

Each new sale carries a UUID request ID. Retries of identical content by the same user return the original sale. The browser saves the pending request in session storage before posting and restores it on reload. After an uncertain network failure, use **Retry previous sale** rather than starting a new invoice. Completed sales have no edit/delete endpoint. Cancellation and returns require later reversal workflows.

## API

All routes require Admin or Sales. POST requests require the login cookie and `X-CSRF-TOKEN`.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/sales?term=...&customerId=...&offset=0` | Search invoices/customer history, 100 records per page |
| GET | `/api/sales/{id}` | Invoice header, lines, payments and warranty dates |
| POST | `/api/sales/quote` | Calculate current totals without writing stock or payments |
| POST | `/api/sales` | Atomically complete a sale; returns `saleId` and detail Location |

Request fields: `requestId` (required for completion), optional `customerId`, `discountAmount`, `notes`, `items` and `payments`. Each item contains `productId`, optional `productUnitId`, `quantity`, `sellingPrice` and `discountAmount`. Payments contain `paymentMode`, `amount` and optional `referenceNumber`. The server supplies user identity, invoice number, date and tax rates. Limits: 500 lines, 10,000 units per line, 20 payment entries and amounts within NUMERIC(12,2).

## Verification

`dotnet run --project tests/StockFlow.MasterDataChecks` covers HTTP roles, page access, validation, quote/create routes, CSRF and absence of hard deletion alongside the existing phase checks.

`-- --sales-database-test` requires `STOCKFLOW_TEST_CONNECTION` pointing to a fresh disposable database named `phase9_test` at `127.0.0.1:55439`. It applies scripts only there and verifies decimal totals, split payments, warranties, historical names, stock reduction, retries, duplicate/sold/damaged serials, aggregate overselling, concurrent withdrawals and complete rollback after a forced payment failure. It creates test-only accounts for browser checks.

Browser tests: run the app against that disposable database at `http://localhost:5232`. Set `STOCKFLOW_UI_URL` to that URL, `STOCKFLOW_UI_DISPOSABLE_DATABASE=phase9_test`, `STOCKFLOW_UI_USERNAME=test`, and the fixture's test-only password. Run `dotnet run --project tests/StockFlow.UiChecks -- --sales`. This mode intentionally writes test sales/customers and refuses other URLs unless the disposable flag matches. It checks cart scanning, unit selection, customer creation, split payments, warranty display, responsive layout and recovery when the server commits but its response is lost.
