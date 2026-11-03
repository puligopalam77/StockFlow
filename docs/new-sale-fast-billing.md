# New Sale: scan-first billing and Quick Add Item

Initially implemented 1 November 2026; claims corrected 2 November 2026. This extends the current checkout, quotation,
invoice, GST and returns workflows. No business database migration was executed
during development; database verification used disposable clones only.

## Counter workflow

- Customer selection is required for new invoices. The picker searches name or
  mobile, retains the cart, supports keyboard selection, and shows Change after
  selection. Historical walk-in invoices remain readable and recoverable.
- Add Customer uses the existing registration API and duplicate-identifier guard.
  Name and mobile are required in checkout; address and GSTIN are collapsed under
  Additional Details. Saving selects the customer and returns focus to scanning.
  General Customer Management still supports its existing optional-mobile policy.
- Registered products retain exact-barcode scanning, text/variant search,
  compatible repeat-scan quantity increments and physical-unit selection.
  Search results now show saved MRP alongside selling price when available.
- **+ Quick Add Item** is a secondary action immediately below barcode search.
  Its modal requires name, quantity and selling price. For ordinary/unregistered
  billing GST classification is optional and defaults to No GST, adding zero tax.
  GST-enabled invoicing still requires a current reviewed rule. MRP, percentage
  discount and note are optional. UQC and HSN come from the selected rule; No GST
  uses the ordinary unit and does not invent an HSN or exemption. The preview
  calls the existing server quote API. Manual rows can
  be edited or removed, and separate manual entries never merge automatically.
  Live preview validates silently without displaying initial error banners or
  highlighting untouched fields. Add to Bill stays disabled until the inputs
  and authoritative preview are valid. GST uses a searchable text field with
  reviewed-rule suggestions; arbitrary percentages/text cannot create a tax
  classification. Blank or No GST remains available for ordinary billing.
- Cart rows distinguish MRP, selling price and percentage discount. Saved bills
  with old fixed discounts retain an explicitly labelled fixed-amount editor.
  Percentage discounts are calculated server-side, with existing reasons,
  permissions, configurable thresholds and approval hashes preserved.
- Totals update automatically. Narrow cart panes retain the columns with
  contained horizontal scrolling. Last-confirmed totals remain provisional until
  a valid server quote arrives. Ordinary row tax is read-only.
- New checkout collects exactly one method: Cash, UPI, Debit Card, Credit Card or
  Bank Transfer. Electronic collection permits a blank reference and requires the cashier's
  explicit receipt confirmation. This is an attestation after checking the bank
  or payment device, not an integration that verifies settlement automatically.
  Reused electronic references are rejected within the shop/profile.
- Hold/resume preserves customer, cart, manual rule identity, entered MRP,
  percentage or legacy fixed discounts, notes and payment selection. Resuming
  revalidates stock, prices, tax classification and pricing mode. A held bill
  does not reserve inventory or consume an invoice number.

## Persistence, invoices and reporting

Manual lines use the existing `sale_items` table with explicit `is_manual=true`
and NULL product, variant and physical-unit identifiers. They capture the name,
note, MRP, unit, reviewed tax-rule or explicit ordinary no-GST snapshot, discount type and percentage along
with the existing posted quantity, price, discount amount, tax rate and total.
The existing request snapshot preserves the original entered pricing values.
Historical fixed discounts are not converted to percentages.

Manual lines create no product, SKU, barcode, warranty or inventory movement.
Mixed invoices post inventory only for registered lines. MRP validation compares
the discounted customer-payable line amount including tax against quantity × MRP.

Invoice details, sales search, A4/thermal printing and browser Save as PDF include
manual lines and their saved details. Mapped GST invoices include them in the
original transaction-bound document capture, component calculations, sales tax
register and HSN reporting. Ordinary No GST bills for unregistered shops do not
become GST source documents or HSN rows and are not labelled missing GST snapshots.
Their normal sales/payment/refund history remains available. No missing legacy
GST snapshots are fabricated.

Authorized manual returns use the existing refund limits and idempotency flow,
with a NON_STOCK disposition and no inventory posting. GST credit/debit notes
remain separate from refunds. The existing report entry now offers **Use original
invoice lines**, including manual source-line identities; server checks limit
cumulative credits to the original source values and quantities.

Product sales/profit reports include a separate manual-items group with unknown
cost and profit. Profit summaries identify known amounts and show incomplete
overall margin when cost is missing. Financial quantity reconciliation explicitly
separates non-stock invoice quantities from stock-ledger quantities.

New single-payment/customer checks run after completed-request lookup, preserving
matching historical walk-in, generic Card, split-payment and credit invoice
replays. Recovery retrieves the original saved payload; it does not create a new
invoice under the historical policy. Existing receivable collections remain.

## Database installation and permissions

Apply `database/migrations/074_fast_billing_manual_items.sql` once, after 073.
Follow its header: back up, stop application writers, and run the entire
transaction as PostgreSQL superuser. The migration adds line snapshots and
identity/return guards, permits NULL catalog references only for manual lines,
extends the existing GST capture, and adds narrow cashier tax-rule access.
It does not rewrite prior invoices, discounts, SKU identities, payments or stock.

For optional GST, also apply `database/migrations/075_optional_gst_manual_items.sql`
once after 074. If 074 was already applied, run only 075. It allows the explicit
No GST snapshot only where the current shop has no GST mapping or is explicitly
UNREGISTERED, and retains GST invoice guards for regular/composition registrations.
Existing reviewed rules and posted snapshots are unchanged. It applies no tax
waiver to GST invoices and does not introduce a separate non-GST mode for a
GST-enabled shop.

Deploy the matching application build and restart it after migration. Checkout
returns an actionable migration message when 074 is absent. Existing shop owners
receive SALES:MANUAL_ITEM; staff need that action assigned explicitly. Existing
SALES:CREATE, DISCOUNT, DISCOUNT_APPROVE, PRICE_OVERRIDE, RETURN, customer creation
and invoice permissions continue to control their respective actions. Cashiers
do not need GST Settings administration access to choose reviewed manual rules.

## Changed files

| Area | Files |
| --- | --- |
| SQL | `database/migrations/074_fast_billing_manual_items.sql`, `075_optional_gst_manual_items.sql` |
| Models and validation | `src/StockFlow.Application/DTOs/Sales/SaleModels.cs`, `DTOs/Gst/GstDocuments.cs`, `DTOs/Operations/OperationModels.cs`, `DTOs/Reports/ActivityReport.cs`, `Common/SaleRules.cs`, `Common/GstDocumentRules.cs`, `Common/PermissionCatalog.cs`, `Services/SalesService.cs`; `src/StockFlow.Domain/Entities/SaleItem.cs` |
| Persistence | `src/StockFlow.Infrastructure/Repositories/SalesRepository.cs`, `SalesManualItems.cs`, `InvoiceRepository.cs`, `OperationsRepository.cs`, `ReportingRepository.cs`, `GstDocuments.cs` |
| HTTP | `src/StockFlow.Web/Controllers/SalesController.cs`, `CustomerController.cs` |
| Checkout and invoices | `src/StockFlow.Web/Pages/Sales/Create.cshtml`, `Invoice.cshtml`; `wwwroot/js/sales-create.js`, `billing-layout.js`, `billing-customer.js`, `sales-details.js`, `shop-operations.js`, `gst-reports.js`, `product-sales-report.js`; `wwwroot/css/billing.css` |
| Checks | `tests/StockFlow.MasterDataChecks/FastBillingChecks.cs`, `BillingChecks.cs`, `SalesChecks.cs`, `QuickCustomerChecks.cs`, `NewSaleIntegrationChecks.cs`, `Program.cs`; `tests/StockFlow.UiChecks/fast-billing-offline.cjs`, `fast-billing-print.cjs`, `billing-offline.cjs`, `gst-offline.cjs` |

## Verification

Executed during this change:

- Application/test builds: zero warnings and errors.
- `--fast-billing-checks`: real manual/mixed checkout, percentage discounts,
  missing/invalid MRP, reviewed-rule validation, required customer, single-method
  collection, electronic confirmation/reference reuse, concurrent idempotency,
  stock exclusion, manual refund/replay, financial reconciliation, unknown cost,
  shop/permission rejection, GST/HSN registers, linked credit limits and Razor
  invoice rendering. This creates and drops a disposable database cloned from
  the established 069 fixture and applies migrations 070–075 there. Optional GST
  tests cover ordinary billing without a rule, zero tax, exact retry, an explicitly
  unregistered shop, exclusion from missing-snapshot warnings, and rejection of
  omitted classification for GST invoicing, including stale ordinary drafts.
- `--new-sale-integration-checks`: catalog/variant/barcode resolution, price
  permissions, last-unit contention, serialized sale/return, historical generic
  Card/split/credit replay and collections, refunds, history/report aggregates,
  request replay and location/tenant/profile isolation on a disposable clone.
  Historical-only payment arrangements are seeded explicitly in that test clone.
- `--billing-checks`, `--quick-customer-checks`: HTTP validation, permission,
  customer duplicate checks, mobile requirement, existing calculator and legacy
  amount compatibility, with rendered checkout fixtures.
- `--gst-catalog-checks`: existing reviewed-rule assignment and catalog regression
  checks, including chunked 1,000-product mapping.
- `fast-billing-offline.cjs`: rendered Razor/local assets with mocked APIs;
  scan-repeat, customer requirement and quick registration, percentage/MRP,
  manual add/edit/remove, hold/resume, single confirmed electronic collection and
  no browser runtime errors. It also checks No GST with an empty rule catalog,
  editing a no-GST item, and required classification for GST invoicing.
  Viewports: 1920×1080, 1366×768, 1280×720, 1280×650,
  1280×600, 1024×768, 768×1024, 390×844, 375×667 and 320×640.
- `fast-billing-print.cjs`: A4 and thermal print media and generated browser PDFs
  using the invoice rendered from an actual saved manual GST invoice.
- `gst-offline.cjs`: report layouts, document drilldown and original manual-line
  loading/submission for linked notes, using mocked browser APIs.

Browser screenshots, rendered manual invoice and generated test PDFs are under
`tests/bin/fast-billing-ui-fixtures/`. These use synthetic business data.

## Limits and acceptance checks

- The existing POS supports whole-number quantities. Fractional-weight manual
  sales need a separate change to that engine.
- A zero-payable invoice still cannot complete through the existing positive
  collected-payment workflow. A 100% line discount is supported when the rest
  of the bill leaves a positive amount payable.
- Manual rule selection retains current POS goods support. Services, zero-rated
  supplies, reverse charge and cess issuance remain excluded. Expired, draft,
  foreign-shop or incompatible pricing rules cannot be finalized. Leaving the
  rule blank is supported only for ordinary/unregistered billing.
- Physical Windows 150% scaling, real scanners/printers and actual bank/UPI
  settlement were not tested. Viewport emulation and browser-generated PDFs do
  not replace those checks.
- Verification is the executed suites above, not a claim that every item in the
  specification's 75-case checklist was run end to end. `billing-offline.cjs`
  delegates to the current browser suite by default. Its retained earlier flow
  requires an explicit `STOCKFLOW_PRE_074_FIXTURE=1` and pre-074 HTML fixtures.
- Fresh-install replay from an empty database was not verified: the repository's
  049 migration is empty. Tests use the existing 069 fixture rather than inventing
  that missing schema history.
- GST outputs retain their working-register status and existing practitioner
  review requirement before production use.
