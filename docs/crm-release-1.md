# Retail CRM Release 1

Implemented 2026-11-01. Scope: Customer 360, enquiries, staff follow-ups and internal reminders, ordinary-checkout conversion links, and a basic dashboard. No production database was changed by this work.

## Capability audit

| Existing capability | Finding | Release 1 integration |
| --- | --- | --- |
| Customer registration, edit and search | Already implemented; optional phone/email/address/GSTIN and duplicate-aware quick create | Reuses the existing customer IDs, service, API and quick-create popup; no customer copies |
| New Sale customer picker and quick create | Already implemented, with cart-preservation and uncertain-save handling | Adds an optional enquiry reference and customer preselection; existing bills are preserved |
| Sales History and invoice details | Already implemented | Customer profile uses the existing SalesRepository list/settlement service and links to existing sale details |
| Returns, refunds, historical due collections | Already implemented | Net purchase values subtract recorded returns; outstanding dues come from the existing settlement aggregation |
| Product/variant/SKU registration and location stock | Already implemented | Read-only active-product/variant lookup, current selected-location stock and permission-limited indicative catalog prices |
| SalesEnquiryRepository | Sales-period reporting, not a customer enquiry workflow | Preserved; separate CRM enquiry repository added |
| Users, permissions and shop selector | Already implemented | New action grants, active-shop staff checks and database RLS reuse existing authenticated context |
| Notifications | Existing notification/outbox system includes communication consent | Internal CRM due/overdue views implemented; no scheduled/outbound notification producer added |
| StockBot | Existing assistant, no verified CRM data capability | Preserved; CRM works independently and exposes no new AI tools |
| Customer profile, customer notes, requests, activity history | Missing | New screens and additive CRM tables |
| Marketing, loyalty and configurable segmentation | Outside this release | No menu links, placeholder balances, campaigns, or sending integrations |

## Screens and workflow

- **Customer Relationships → Customer Overview** (`/Crm`): summary cards, trimmed literal partial search, status/type/inactivity/follow-up filters, compact actions, shared pagination hidden at ten or fewer records.
- **Customer 360** within Customer Overview: contact/tax identity from the existing master; overview, purchase history, enquiries, follow-up history, notes and preferences. Administration customer names link here. Authorized users edit through the existing Customer Registration screen.
- **Enquiries & Follow-ups** (`/Crm/Enquiries`): status, due/overdue/upcoming/completed, priority, staff, enquiry type, product, created-date range and safe sorting. New/edit enquiry and follow-up dialogs retain input after errors and block dismissal during save.
- New enquiries require an active linked customer, type, subject and authorized assignee. Product/variant and quantity are optional. Scheduled Call/WhatsApp/Email follow-ups require a usable corresponding contact; In Person/Other do not require a mobile number.
- The shared quick customer popup creates through `/api/customers?preventIdentifierDuplicates=true`. Selecting an existing match or new customer links that same ID to the current CRM shop. It preserves the enquiry fields. Existing consent records are not interpreted as marketing opt-in.
- Follow-ups append activities with actor, actual contact time, separate server recording timestamp, outcome, notes, next action and status. Notes and preferences are shop-specific; preference edits keep an append-only audit.
- **Create Sale** opens the existing New Sale screen and preselects the customer on an empty bill. Products are deliberately added through normal POS lookup, with live stock, price, tax, serial and permission checks. An existing unrelated bill is preserved and the cashier is instructed to hold/clear it first.
- A completed checkout records an immutable link in the same transaction as the existing sale. Opening a draft creates no conversion. Request replay retains the original invoice; a different second checkout for the same enquiry rolls back. Enquiry resolution and reminder scheduling remain explicit staff actions, rather than being silently changed by opening or completing a bill. Cancelled source invoices are excluded from successful-conversion dashboard counts; the historical link remains.

## Database and rollout

New manual migration: `database/migrations/072_retail_crm.sql`, after **071**. Run the entire file once as a PostgreSQL superuser, with a backup and writers stopped. On an error, `ROLLBACK` and investigate; do not continue with later statements. Deploy the accompanying application before reopening it. Nothing here applies migrations automatically.

New tables: `crm_customers` (relationship/preferences, not another customer master), `crm_enquiries`, `crm_activities`, `crm_customer_notes`, `crm_sale_links`, `crm_preference_history`. Existing customers, invoices, payment identifiers, taxes, SKUs, barcodes and inventory algorithms are not rewritten. A narrow customer-insert trigger establishes the current authorized shop relationship for newly registered customers.

Existing customers are linked only to shops evidenced by their sales headers. Unsold legacy contacts are not assigned to guessed shops. Authorized users can explicitly link an existing contact using the Customer Overview action. The underlying customer-master sharing behavior remains unchanged; CRM enquiries, notes and financial activity remain separate for each tenant, business profile and store. Financial/history views additionally respect the selected authorized stock location, matching the existing Sales History service. This release does not provide consolidated CRM figures across stores or tenants.

Migration 049 in this checkout is empty. Full fresh-install replay remains blocked until the genuine migration is recovered. Tests below used the preserved through-069 disposable test template, then applied 070–072; they do not prove a clean replay of every repository migration.

## Authorization

New **CRM** module: `VIEW`, `CREATE`, `EDIT`, `ASSIGN`, `FOLLOW_UP`, `CLOSE`, `VIEW_TEAM`, `VIEW_HISTORY`, `VIEW_FINANCIAL`. Migration grants new rights only to existing verified tenant owners; other staff need explicit grants through existing permission administration.

- VIEW shows current-shop CRM customers and the user's assigned enquiries.
- VIEW_TEAM broadens enquiry/activity visibility within that same shop, never across shops.
- CREATE creates enquiries and explicitly links existing contacts; customer creation additionally requires existing CUSTOMERS CREATE/VIEW access.
- EDIT changes enquiry details and CRM preferences/notes. Changing another assignee also requires ASSIGN. Closing/reopening via enquiry edit additionally requires CLOSE; a terminal status selected during a follow-up requires FOLLOW_UP and CLOSE.
- VIEW_HISTORY permits invoice-history rows. VIEW_FINANCIAL independently permits monetary summaries and dues; without it monetary values are withheld. Existing sale/invoice-detail links still require those screens' own permissions.
- Create Sale requires existing Sales CREATE, customer lookup access, and CRM VIEW; normal checkout authorization remains in force.

Backend guards validate active assignee membership and product/variant relationships. Row-level policies bind CRM records to tenant/profile/store and authorized location. Stable customer/enquiry identities cannot be reassigned by update. Staff cannot use an unassigned enquiry ID to view another user's activity without team access.

## Calculation and time rules

- Purchase count: completed invoice headers, not line count or payment count; a returned invoice remains an historical purchase, with its return reflected in net value.
- Net purchases: completed invoice total minus its separately aggregated recorded returns. No payment collection is treated as revenue or another purchase.
- Dues/collected/refunded/payment status: existing SalesRepository settlement aggregation over payments and returns, preserving its established credit-adjustment rules.
- Repeat customer: more than one completed invoice within current financial scope. This is a simple factual count, not a configurable marketing segment.
- New customers this month: existing master creation date, not CRM linking date. Lifetime cards are labelled separately.
- Due Today uses the configured `ShopTimeOptions` midnight boundaries. Overdue means the scheduled instant has passed, including earlier today. Upcoming is tomorrow onward. Today and overdue can therefore overlap.
- Internal schedules store UTC instants. Input and display use the application-configured shop timezone (currently shared `ShopTimeOptions`; no new per-store timezone setting). Nonexistent daylight-saving local times are rejected.

## Actual verification

Build: `dotnet build tests/StockFlow.MasterDataChecks --verbosity quiet --nologo -p:OutDir=E:\StockFlow\.artifacts\crm-checks\` — zero warnings/errors.

Executed checks:

- `--crm-checks`: applies 070–072 to a disposable clone. Tests customer API identity reuse, duplicate enquiry/follow-up requests, changed-payload replay rejection, stale versions, status/reopening rules, notes/preferences/audit, invalid staff/pagination, actual stock lookup, timezone midnight/overdue, shop/profile isolation, normal checkout/retry, one invoice link and stock deduction, second-conversion rollback, actual return/refund settlement, later collection against an older invoice, append-only history, assigned-only staff access, separate history/financial permissions, unauthorized assignment/close and forged-tenant rejection.
- `--crm-http-checks`: real Razor rendering, middleware permission routing, denied CRM access, distinct follow-up rights and CSRF enforcement.
- Existing `--billing-checks` and `--quick-customer-checks`: payment/discount/full-coverage and customer API regressions.
- Existing `--gst-template-checks`: GST configuration, tax snapshots, reports, source-document retries, ordinary checkout, rollback, invoice rendering and registration isolation still pass with migration 072 installed.
- `tests/StockFlow.UiChecks/crm-offline.cjs`: Edge/Playwright with mocked APIs, both list screens, enquiry form and customer profile/history at **1920×1080, 1366×768, 1280×650, 1024×768, 768×1024, 390×844, 375×667, 320×640**. Checks contained tables, modal/save reachability, actual computed 26×26 buttons/13–14px icons, existing sprite glyphs, pagination threshold, Escape, pending-save protection, error/input retention, retry keys, profile tabs, existing quick-create reuse and checkout link.
- Existing `billing-offline.cjs`: original billing regression suite, including customer/cart preservation, payment confirmations, retries, discounts, cash change and responsive editors.
- `CRM_ONLY=1` with `billing-offline.cjs`: customer preselection, no automatic sale on opening an enquiry, link/customer/notes restoration after refresh, final request linkage and preserving an unrelated existing bill.

Browser screenshots: `.artifacts/crm-responsive/`. Browser fixtures use mocked APIs; database tests exercise repositories separately. These are not production end-to-end tests, physical HP hardware/Windows scaling tests or a complete application-wide regression run. Production rollout and shop-user acceptance have not been performed.

## Deferred or bounded behavior

No configurable segments, loyalty, campaigns, bulk messaging, customer-facing reminders, stock-arrival automation, background notification jobs or AI CRM features. No automatic inventory reservation, purchase order, tax recalculation or invoice finalization from an enquiry. Existing customer communication-consent records remain authoritative for their existing purposes; this release adds a contact preference, not a separate marketing-consent model.

Profiles show the most recent 100 notes/authorized activities/enquiries; the main enquiry list is paginated and enquiry drilldown retains its complete activity history. No customer merge, full structured-address redesign, advanced analytics, historical data reassignment or bulk CRM migration UI is introduced. Closed/resolved/cancelled enquiries reopen as In Progress with CLOSE authorization before further follow-up. Assignment and closure controls supplement edit/follow-up rights; they are not standalone bulk-action interfaces.
