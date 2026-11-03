# Phase 10: invoices, printing and PDF

## Manual database setup

Run `database/migrations/013_phase10_invoices.sql` in the StockFlow application database **after migration 012**. It is transactional and repeatable. It creates the single company-settings record and adds invoice snapshot columns and supporting indexes. It does not change stock, payments, historical amounts or invoice numbers. No migration runs automatically at application startup. Tests applied this script only to a disposable database.

After applying the script, restart the app and sign in as Admin. Open **Company / invoice settings** and enter the real company name, address, phone, email, GST number (if applicable), and invoice footer. The migration seeds only the existing StockFlow name and a thank-you footer, without fictitious contact or registration details.

## Functional flow

1. Configure company details as Admin.
2. Create and complete a sale as Admin or Sales.
3. The application opens `/Sales/Invoice/{saleId}` automatically.
4. Review company/customer details, items, brand/model, serials, quantities, rates, discounts, tax, totals, payments and warranty summary.
5. Click **Print invoice** to print. Click **Save as PDF**, then choose the browser's **Save as PDF** destination. Both actions open the browser print dialog. For clean output, disable browser-generated headers and footers.
6. Reopen invoices through **Sales / invoices**. Search by invoice number, customer name or mobile; optionally filter from/to dates (inclusive). Existing sale-detail pages also link to the printable invoice.
7. Scanning a sold unit offers **View sale invoice / warranty**, including for Service users.

The print layout uses A4 portrait, hides application navigation/buttons, wraps long product/serial values, repeats table headings across pages, and keeps totals and short warranty entries together. Screen tables scroll horizontally on small phones. Browser PDF needs no additional library or server PDF endpoint.

## Historical data

New sales capture company settings, customer name/mobile/address/GST, product name/brand/model/serial and warranty term at completion. Later master-data or company-settings edits do not change those saved details, including fields that were blank at sale time. Rates, discounts, tax percentages, line totals and header totals always come from the recorded transaction; invoice viewing does not reprice the sale.

Older sales retain their Phase 9 name snapshots where available and use current records for details not previously captured. A screen notice identifies this limitation; the migration does not invent historical data or backfill today's details as if they were original. Warranty dates/status come from existing warranty registrations. Quantity-only products can display their recorded warranty term without implying that a physical-unit warranty was registered.

Completed and cancelled invoices remain readable. Cancelled invoices show a prominent **CANCELLED** status; original payment entries are not presented as refund confirmation. Draft sales do not produce issued invoices. Invoice viewing/printing is read-only and does not create stock movements, payments, audit records, or new invoice numbers.

Amounts are shown in INR. Tax remains the existing generic percentage and amount; this phase does not add CGST/SGST/IGST splits, HSN/SAC or e-invoicing. CREDIT is displayed separately from money received and represents the amount recorded at sale, not a live receivables balance.

## Access and API

Admin, Sales and Service can view/print invoices. Only Admin can edit company settings; saves require Razor anti-forgery protection and record an audit entry. Invoice responses use `Cache-Control: no-store`.

- `GET /api/invoices/{saleId}` — invoice DTO.
- `GET /api/sales/{saleId}/invoice` — same invoice data.
- `GET /Sales/Invoice/{id}` — printable Razor page.
- `GET /Settings/Company` and its form POST — Admin company settings.
- `/api/sales` accepts `term`, `customerId`, `offset`, `from` and `to` for sales/invoice search (Admin/Sales).

Missing and draft invoices return 404. There are no invoice create/edit/delete endpoints; a completed sale is the source record. The PDF button uses browser printing; there is no `/pdf` server-download endpoint in this phase.

## Verification

`dotnet run --project tests/StockFlow.MasterDataChecks` covers authorization, missing/cancelled invoice rendering, HTML encoding, private-cache headers and company-settings CSRF/access alongside earlier phase checks.

The existing `--sales-database-test` mode now applies migration 013 twice to a fresh disposable `phase9_test` database at `127.0.0.1:55439`. It tests snapshot preservation after master/company edits, null-field preservation, historical mobile/date search, legacy fallback, draft/cancelled behavior, read-only invoice retrieval and the unchanged sales transaction rules. It also writes invoice IDs for browser fixtures to `tests/bin/phase10-invoice-ids.txt`.

With the app connected to that disposable fixture, use the existing `STOCKFLOW_UI_URL`, `STOCKFLOW_UI_USERNAME` and `STOCKFLOW_UI_PASSWORD` test variables, then run `dotnet run --project tests/StockFlow.UiChecks -- --invoices`. It checks four viewport sizes, print buttons/CSS, single and multi-page PDFs, invoice search and scanning links. Generated test PDFs are saved to `tests/bin/invoice-phase10.pdf` and `tests/bin/invoice-phase10-multipage.pdf`; screenshots are under `tests/bin/ui-screens`.
