# Phase 5: counter billing and customers

Open Sell / Billing → New sale (`/Sales/Create`). The counter screen puts the
scanner and cart next to customer selection and payment. On smaller screens the
same sections stack in the order Scan items → Customer → Payment.

Scan an existing manufacturer code, generated variant label, individual serial,
or generated unit label. Registered details and selling prices fill automatically.
Repeated quantity-item scans increase quantity; the same individual serial cannot
appear twice. Clothing size/color attributes and battery serials are shown in the
cart. A shared battery model code opens individual-unit selection instead of
silently choosing a battery. Manual item search is available under an optional
section. Staff bill from their assigned location with no store picker; the
existing owner/admin review selector remains in the shell.

Current policy (reviewed 2 November 2026): server quotes remain authoritative.
New checkout requires a registered customer and exactly one fully collected method:
Cash, UPI, Debit Card, Credit Card or Bank Transfer. Electronic references are
optional; explicit receipt confirmation is mandatory. This is manual observation,
not bank/provider settlement verification. New customer credit, split checkout and
payment due dates are unavailable. Older credit/split/walk-in records remain
readable; authorized later collections use the original invoice workflow.

Item/invoice discounts follow the existing versioned policy and approvals. Modern
allocated discounts reduce the saved taxable base; legacy after-tax allowances
retain their original semantics. Do not reprice historical invoices.
Complete sale records stock and payments together, then opens the invoice for
users with INVOICES/PRINT permission. Users without invoice permission see a saved
sale confirmation and can start the next sale. Customer/lookup/unit controls are
shown only with their respective module permissions. Customers/CREATE is required
for quick creation, separately from customer lookup. Invoice details continue to
use saved customer/item snapshots.

## Stock protection and interrupted sales

Existing transactional sale protection is retained: product locks precede unit
locks, variant stock is checked independently, sold/unavailable units are rejected,
serials must belong to the chosen variant, and invalid payments roll back the sale.
Two tills cannot sell the last quantity item or the same physical serial twice.
Browser stock checks are an early indication; the server is authoritative when
other tills are selling concurrently.

Before posting, the browser saves the exact request and a UUID in session storage
scoped to business, cashier and location. Lost/network/server responses preserve
that request. Verification/recovery uses the original saved snapshot; an allowed posting retry keeps its identity, so the existing database
idempotency guard returns one invoice. Authentication/access/location-conflict
responses also retain an uncertain request. Confirmed validation failures allow
editing. Browsers on ordinary LAN HTTP use secure random bytes when randomUUID is
unavailable. Browser storage must be available before a sale is sent.

An old `stockflow-pending-sale` draft from the previous billing screen is recovered
through `/api/sales/recover`. This endpoint only retrieves an existing completed
invoice for the same cashier, location and exact request. It never creates stock
movements, sales or payments. If that invoice cannot be found, reconcile the old
draft before re-entering the sale. Check the saved request ID against invoices as
an administrator; do not clear/re-enter an uncertain draft solely because a retry
fails. The new flow leaves it available for further recovery.

## Database and deployment

**No Phase 5 migration is required.** Retain the existing database baseline through
`026_guided_stock_receiving.sql`. No application database scripts were executed
automatically. Deploy/restart the updated application. Optional read-only integrity
checks are in `database/queries/counter_billing_checks.sql`: payment totals, duplicate
completed serial sales, walk-in credit, negative location stock and request IDs.

## Verification

Build with `dotnet build tests/StockFlow.MasterDataChecks --no-restore --artifacts-path tests/bin/store-assignment-build`.
The built check DLL supports `--billing-checks` for page/action permissions and
validation, producing real Razor HTML fixtures. Run
`tests/StockFlow.UiChecks/billing-offline.cjs` using the Playwright bundled Node
executable for scanner/cart, authoritative quote rendering, default full payment,
customer lookup/creation, optional discounts/splits, credit validation, invoice
navigation, pending retry/reload and 1440/768/390/320 layouts.

`--billing-database-test` creates and removes a fresh disposable database on local
test PostgreSQL port 55441. It applies the stored full baseline and verifies actual
quantity/serial sales, tax and discounts, customer snapshots, split payments,
idempotency, legacy recovery, concurrent last-item/serial competition, per-variant
stock protection, store isolation and the read-only checks. It never uses the
application connection string. Existing store-assignment and navigation checks
continue to cover pinned staff context and protected modules.
