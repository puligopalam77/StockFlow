# StockFlow database scripts

## WhatsApp Business notifications (076)

After 075, stop writers and back up, then manually run the entire
[076_whatsapp_business_notifications.sql](migrations/076_whatsapp_business_notifications.sql)
once as PostgreSQL superuser. It creates disabled-by-default account/queue support,
without historical messages or financial changes. Configure Meta separately using
the [WhatsApp integration guide](../docs/whatsapp-business-integration.md).
StockFlow does not apply this migration automatically.

## Barcode label sizes and A4 templates (062)

After 061, back up and stop application/workers, manually apply
[062_barcode_label_sizes.sql](migrations/062_barcode_label_sizes.sql) as PostgreSQL
superuser, then restart. Adds saved 50 x 30, 40 x 25 and scan-tested 30 x 20 mm
thermal formats, plus measured A4 sticker templates. Existing print history, codes
and stock are retained. See [barcode management](../docs/barcode-management.md).

## Barcode management and print requests (061)

After 060, back up and stop application/workers, then manually apply
[061_barcode_print_requests.sql](migrations/061_barcode_print_requests.sql) as
PostgreSQL superuser and restart. This adds tenant/location-scoped print-request
history with retry identifiers, selected barcodes, copy counts, format and actor.
It does not regenerate codes, change stock or claim physical printer confirmation.
See [barcode management](../docs/barcode-management.md).

## Purchase barcode lots (060)

After 059, back up and stop application/workers, then manually apply
[060_purchase_barcode_lots.sql](migrations/060_purchase_barcode_lots.sql) as PostgreSQL
superuser and restart. Purchases now includes a Barcodes submenu for received lots.
The migration adds persistent purchase-receipt labels without changing existing
codes or stock, and moves the existing BARCODES navigation entry without granting
new action permissions. See [purchase barcode lots](../docs/purchase-barcode-lots.md).

## Existing stock barcode workflow (059)

After 058, back up, stop application/workers, and manually apply
[059_stock_barcode_workflow.sql](migrations/059_stock_barcode_workflow.sql) as
PostgreSQL superuser, then restart. This enables unique labels for existing stock
without guided receipt records. Previously printed codes, manufacturer barcodes,
stock balances and transaction history are retained. The application never runs
this migration automatically. See [barcode workflow](../docs/stock-barcode-workflow.md).

## Brand registration details (058)

After 057, back up and stop application/workers, then manually apply
[058_brand_registration_details.sql](migrations/058_brand_registration_details.sql)
as PostgreSQL superuser and restart StockFlow. This enables editable brand codes,
optional websites and logos. Existing brand IDs, codes, category/product links and
history remain intact. See [brand registration](../docs/brand-registration.md) and
the [read-only acceptance queries](queries/brand_registration_details_checks.sql).

## Cascading category registration (057)

After 056, back up and stop application/workers, then manually apply
[057_category_registration_details.sql](migrations/057_category_registration_details.sql)
as PostgreSQL superuser. Restart the updated application. Main/Subcategory navigation
works before this migration; 057 enables editable category codes and persisted display
order. Existing IDs, codes, hierarchy and product/history relationships are retained.
The script is not executed automatically.

## Category configuration (056)

After 055, manually apply [056_category_registration_tabs.sql](migrations/056_category_registration_tabs.sql)
as PostgreSQL superuser with a backup and writers stopped, then restart StockFlow.
It adds validated category inventory defaults and an atomic category/configuration
save routine. Existing category records retain their current behavior. See the
[workflow and deployment guide](../docs/category-registration-tabs.md) and
[read-only checks](queries/category_registration_tab_checks.sql). The application does not run it.

## Parent/child categories and inherited fields (055)

After 054, manually apply [055_category_hierarchy_inheritance.sql](migrations/055_category_hierarchy_inheritance.sql)
as PostgreSQL superuser with a backup and writers stopped, then restart StockFlow.
It preserves existing category/product/brand identities, enables field inheritance
for unused/new categories, and safely organizes unused Bike category subtrees.
Populated categories retain their direct field rules and require review before
moving. See [deployment and workflow](../docs/category-hierarchy.md) and
[read-only checks](queries/category_hierarchy_checks.sql). The application does not run it.

## Unique received-item labels (054)

After 053, manually apply [054_unique_stock_item_labels.sql](migrations/054_unique_stock_item_labels.sql)
as PostgreSQL superuser with a backup and writers stopped. Restart the updated app.
Quantity stock can then receive distinct saved labels for each received piece;
specific/all reprints preserve those codes. Stock and existing codes are unchanged.
See [workflow and verification](../docs/unique-stock-item-labels.md) and
[read-only checks](queries/unique_stock_item_label_checks.sql). The application
does not run this migration.

## Multiple product images (053)

After 052, manually apply [053_product_image_gallery.sql](migrations/053_product_image_gallery.sql)
as PostgreSQL superuser with a backup and writers stopped. Existing single images
remain unchanged. It enables up to five uploaded images, a main image and thumbnails.
Use the [deployment and verification guide](../docs/product-image-gallery.md) and
[read-only verification queries](queries/product_image_gallery_verification.sql).
The application does not run this migration.

## Product, category and brand registration (052)

After 051, manually apply [052_catalog_registration_improvements.sql](migrations/052_catalog_registration_improvements.sql)
as PostgreSQL superuser with a backup and writers stopped. It enables optional brand
links/aliases, category field policies, fitment lists, supplier item identifiers and
product status, preserving the original catalog and ledger. Use the
[deployment and acceptance guide](../docs/catalog-registration-review.md), compare
[preservation snapshots](queries/profile_data_preservation_snapshot.sql), and run
[registration verification](queries/catalog_registration_verification.sql).
The application never applies migrations automatically.

## Selected-profile transaction visibility (051)

After 050, manually apply [051_profile_data_visibility.sql](migrations/051_profile_data_visibility.sql)
as PostgreSQL superuser with a backup and all writers stopped. It keeps existing data
and filters documents, payments and totals by the selected setup. Compare the
[preservation snapshots](queries/profile_data_preservation_snapshot.sql) before/after;
then run [runtime visibility verification](queries/profile_data_visibility_verification.sql).
See the [deployment and UI verification steps](../docs/business-profile-data-visibility.md).
Application startup never runs the migration.

## Bike Accessories demo setup (050)

After 049, manually apply [050_bike_catalog_setup_switch.sql](migrations/050_bike_catalog_setup_switch.sql)
as PostgreSQL superuser, with a backup and writers stopped. Then restart, select
Bike Accessories in Shop Business Profile and apply the setup. Original Electrical
catalog and history are retained. See the [demo guide](../docs/bike-accessories-demo-setup.md)
and [read-only preservation checks](queries/bike_demo_setup_verification.sql).

## Multi-business Phases 5 and 6: retail transactions and verification (049)

After 048, manually apply [049_retail_variant_document_snapshots.sql](migrations/049_retail_variant_document_snapshots.sql)
as PostgreSQL superuser with a backup and writers stopped. It captures attributes,
SKU and unit/name details for new documents without backfilling historical values
or changing stock. Use the [read-only checks](queries/retail_phase56_verification.sql)
and [deployment/acceptance guide](../docs/multi-business-phase5-6-retail-transactions.md).
No Phase 6 SQL script is required and startup never applies migrations.

## Multi-business Phases 3 and 4: category fields and registration (048)

After 047, manually apply [048_category_attribute_schemas.sql](migrations/048_category_attribute_schemas.sql)
as PostgreSQL superuser with writers stopped and a backup. Restart the application,
then run the [read-only verification queries](queries/category_attributes_phase34_verification.sql).
Existing products and stock are preserved. See the [setup and acceptance steps](../docs/multi-business-phase3-4-category-registration.md).
Application startup never applies this script.

## Multi-business Phase 2: shop profiles (047)

Manually apply [047_shop_business_profiles.sql](migrations/047_shop_business_profiles.sql)
after 046 as PostgreSQL superuser. Existing tenant profiles remain Electrical.
See the [deployment and acceptance guide](../docs/multi-business-phase2-shop-profiles.md)
and [read-only verification](queries/business_profile_phase2_verification.sql).
The app and automated Phase 2 checks do not execute database scripts.

## Multi-business Phase 1: electrical baseline (046)

Manually apply [046_electrical_business_profile_baseline.sql](migrations/046_electrical_business_profile_baseline.sql)
after the existing migrations through 045. It records `ELECTRICAL` for current
businesses without rewriting products, identifiers, stock or transactions. The app
continues the legacy electrical experience before the script is installed.
See the [deployment and verification steps](../docs/multi-business-phase1-electrical-baseline.md),
[before/after baseline](queries/electrical_phase1_baseline.sql) and
[profile verification queries](queries/electrical_phase1_verification.sql).
The application and Phase 1 test runner do not execute this migration.

## Staff help guides

Start with [StockFlow staff help](../docs/help/README.md) for products, receiving
stock, billing, customer returns, warranty and common problems. Phase 1 adds
documentation; Phase 2 adds the signed-in **Need help?** panel with local search,
screen questions and permitted screen links. No new database scripts are required,
and no migrations are executed for this work. Phase 3 adds local help translation
packs for all 22 Indian languages, following the existing header language choice.
No additional SQL is needed for help translations when migration 036 is already applied.

Phase 4 adds optional [AI guide selection](../docs/help/ai-setup.md) through a
configured Ollama provider. It uses the existing guides and permissions without
new tables or migrations. AI is disabled by default until a model is configured.

Phase 5 adds [live stock enquiries from Help](../docs/help/business-enquiries.md)
using the existing inventory repository and tenant/location policies. This also
requires no new SQL and performs no stock changes.

## Reading the application code

The application JavaScript uses separate statements, explicit blocks and
descriptive local names. Receiving and billing use names such as `pendingReceipt`,
`isSavingSale` and `getElement` to make their purpose visible. C# methods use
blocks, and most classes show their dependencies as fields and constructor
parameters. `.editorconfig` and `.prettierrc.json` describe the formatting rules.

For a receiving action, follow the code in this order:

1. `src/StockFlow.Web/Pages/Receiving/Index.cshtml` defines the screen and input IDs.
2. `src/StockFlow.Web/wwwroot/js/receiving.js` handles scans, the receipt and saving.
3. `src/StockFlow.Web/Controllers/ReceivingController.cs` receives the API request.
4. `src/StockFlow.Application/Common/StockIntakeRules.cs` checks the request.
5. `src/StockFlow.Infrastructure/Repositories/StockIntakeRepository.cs` saves it.

`const` declares a variable that will not be reassigned; `let` allows reassignment.
`async` functions can wait for server responses with `await`. A callback is a
function passed to another function, for example the handler for a button click.
`getElement('input-id')` finds a field on the page. `?.` reads a property only if
its object exists, and `??` supplies a fallback for a missing value. These operators
remain where they keep the code clear.

Saving stock and bills still needs transaction, permission and retry handling.
The comments explain these steps; keep them when changing a workflow. In particular,
a retry uses the same request ID so a lost response does not create duplicate stock
or a second bill. The scanner queues lookups so quick scans are processed in order.

## Field validation across forms

Forms show an alert and messages beside invalid fields when Save / Submit is
clicked. The shared helper is `wwwroot/js/common/form-validation.js`; it also
handles fields created by JavaScript. Required text cannot be empty or just spaces.
Disabled and read-only fields do not block a save. Billing and product registration
keep their workflow-specific checks.

Phone entry accepts exactly 10 ASCII digits, including a leading zero. Do not enter
spaces, letters, separators or a country prefix. Optional phone fields may be blank.
This rule applies to customers, suppliers, staff, stores and shop settings, and is
also checked by the server. Existing Indian supplier numbers keep their `+91`
storage format; editing shows the local 10 digits.

Other checks include minimum/maximum text lengths, email format, 15-character GSTIN
format, supplier country-specific postal codes, password confirmation, numeric
ranges and whole-number counts. Money, stock, dates, serial counts, duplicate records
and permissions also retain their existing server/business checks. A correctly
formatted phone, email or GSTIN does not prove that it is registered or reachable.

Run `dotnet run --project tests/StockFlow.MasterDataChecks -- --field-validation-checks`
for request-field boundary checks and rendered form fixtures, then run
`tests/StockFlow.UiChecks/field-validation-offline.cjs` with Node for browser checks.
The browser check writes `tests/bin/field-limit-audit.json` with the field limits
found on 23 pages. These checks use mocked APIs and repositories, without database
writes. The normal full suite also includes the server field checks.

## Clearer wording across shop screens

Receiving, billing, stock, transfers, repairs, warranty, reports, product setup,
staff access and account screens use simpler captions and instructions. Stock
corrections explain their effect before saving. Status labels such as “Waiting
for a part” retain their original API values. Navigation captions change only
for display; module codes, routes and permissions stay the same. No database
migration is needed for these wording changes.

Validation includes the application checks with mocked repositories, JavaScript
syntax checks and browser checks using rendered pages and mocked API responses.
Physical scanner testing and a usability trial with shop staff remain separate.

## Counter billing improvements (migration 034)

Stop all instances, back up, manually run [`034_billing_workflow.sql`](migrations/034_billing_workflow.sql)
once as PostgreSQL superuser after 033, then restart the updated application. This migration
is not applied automatically. It adds saved bills, discount policy, customer credit limits,
and financial approval requests. It preserves existing completed sales.

* Cash received calculates change; only the invoice's cash portion is recorded as payment.
  Cash tendered and change are retained on the invoice when an amount is entered.
  Leave cash received empty for exact cash. Split payments calculate change against their
  combined cash portion. A cash shortfall blocks completion.
* Bills save immediately on the current device and synchronize to PostgreSQL after 800 ms.
  Hold this bill starts a fresh bill; Saved bills restores bills belonging to the signed-in
  user at the active location, including from another device after signing in. Drafts expire
  after seven days, never deduct stock, and are removed when their sale completes. Expired
  drafts are purged on subsequent draft saves. Failed sync shows a warning and retains the
  device backup. Refresh restores the current tab's backup; after opening a new browser use
  Saved bills. An unconfirmed submitted sale continues to use the separate safe retry flow.
* Restored carts recheck stock, serial availability, model, warranty and catalog prices.
  Unmodified catalog prices follow price updates; authorized custom prices remain overrides.
  Prices exclude tax; line totals include tax. Alt+S focuses scanning, Alt+P payment, Alt+H hold.
* SALES permissions PRICE_OVERRIDE, DISCOUNT and DISCOUNT_APPROVE are separate. Migration
  grants these new permissions to tenant owners only; assign staff permissions explicitly
  in user/role administration. Price changes and discretionary discounts need a reason.
  Reductions above 10% of catalog value require approval by default. Users with company
  settings edit permission can change this threshold under Billing discount policy.
* A cashier requests approval under Discounts, notes and split payments. An approver uses
  Review approval requests at the same location, reviews the exact amounts and approves.
  Approval lasts 30 minutes and is consumed at completion. Changes to customer, items,
  prices, discounts, reason or trade-in invalidate it. Approval and completion are audited.
* Selecting a customer shows outstanding credit across all tenant locations. Users with
  customer edit permission can set a limit under Customer credit limit; zero disables new
  credit and empty means unlimited. Existing customers remain unlimited until configured.
  Completion checks the limit again while locking the customer, preventing concurrent
  tills from collectively exceeding it. Payments and returns reduce outstanding balances.
* Receive trade-ins in the billing form with SALES FULFIL permission. The allowance is
  automatically included in the after-tax invoice discount, labelled on the invoice, and
  the received item is saved atomically with the sale. Do not enter the allowance again
  as a discretionary discount. Safe retries cannot create a duplicate trade-in.
* Invoice printing supports A4 or an 80 mm receipt, retaining serials, warranty and payments.
  Select 80 mm paper in the printer driver and print at 100% with headers/footers disabled.
  New bill returns directly to the counter. Hardware printing requires a local printer check.

## Shared login sessions and security (migration 033)

Stop all StockFlow instances, back up the application database, and manually execute
[`033_shared_login_sessions.sql`](migrations/033_shared_login_sessions.sql) once as
PostgreSQL superuser after migration 032. Restart the updated instances together.
This migration is not applied automatically. Older memory-based login cookies will
require a new sign-in. New sessions and cookie encryption keys persist in the same
PostgreSQL database across application restarts and multiple instances.

Logins expire after 15 minutes without user input. Pointer movement, clicks,
typing (including keyboard barcode scanners), wheel scrolling and touch input
restart the timer. A warning appears during the final minute. Work in any tab
keeps the same browser login active; logout is shared across those tabs. Different
browser/device logins have independent timers. Background polling and page
refreshes do not extend inactivity. Remember Me does not bypass the timeout.
Expired sessions cannot be revived by the first input after sleep or by calling
the activity endpoint. Activity/logout POSTs require antiforgery tokens and the
current session identifier. Old tabs cannot sign out a newer login.

Server deadlines live in the tenant/user-isolated `auth_sessions` table. Only
explicit activity renews the idle deadline; an eight-hour maximum lifetime never
renews. Browser timers can pause during sleep; the server still rejects expired
sessions and the browser checks on wake/focus. The timeout never submits an
unfinished sale or receipt. Normal logout also checks the submitted session ID,
so an old tab cannot sign out a newer login.

Open **My profile → My active sessions** to view your browser/device sessions,
IP addresses, sign-in and last activity times, and maximum expiry time. You can
sign out another session or this device. Only the signed-in user's sessions are
listed or revocable, including across tenants. A remotely revoked device loses
server access immediately and its screen locks at its next request or interaction.
Device names are inferred from browser-reported user-agent information; they are
descriptive labels, not verified device identities.

Audit Logs record `SESSION_STARTED` and `SESSION_ENDED`, the user, time, IP/device,
and reasons such as `LOGOUT`, `IDLE_TIMEOUT`, `ABSOLUTE_TIMEOUT`, `USER_REVOKED`,
`PASSWORD_CHANGE`, and `SECURITY_CHANGED`. Audit correlation identifiers are hashes;
cookies, credentials and raw session IDs are not written into these events. A
minute-based worker audits expiry even when a browser closes or its cookie has
already expired. Multiple workers use row locking to audit each transition once.
Revoked session rows are retained for 30 days; audit history follows the existing
audit-log retention policy.

`SessionSecurity` settings default to `IdleMinutes=15`, `AbsoluteHours=8`,
`LoginAttemptsPerIp=30`, `LoginWindowMinutes=10`, and
`GlobalLoginAttemptsPerMinute=300`. Sign-in and authenticator POSTs are throttled
in PostgreSQL across all instances and return HTTP 429 with `Retry-After`. Existing
per-account password and authenticator lockouts remain enforced. Adjust limits for
stores sharing a public IP. Caller-supplied forwarding headers are ignored unless
you configure explicit IP addresses in `SessionSecurity:TrustedProxies`.

All instances must use the same application database and Data Protection
application name (`StockFlow`). Shared key XML is stored behind restricted database
functions. For production, configure the same PFX certificate on every instance
with `SessionSecurity:KeyCertificatePath` and provide its password through the
secret environment setting `SessionSecurity__KeyCertificatePassword`; this encrypts
new keys at rest. Keep the certificate and database backups protected and retain
old certificates during rotation to decrypt existing keys and authenticator
secrets. Without a configured certificate, shared key XML is not encrypted at rest.
During the initial move from the previous local key ring, existing encrypted
authenticator secrets also depend on those old keys and their application
discriminator. Before rollout, ensure enrolled users have their recovery codes;
use the existing admin authenticator-reset flow to re-enrol them under the new
shared key ring if the legacy keys/purposes have not been migrated. Recovery codes
remain usable even when an old authenticator secret cannot be decrypted. This is
a deployment requirement, not an exemption from authenticator verification.
An unavailable session database prevents authentication/session renewal; there is
no fallback to per-server memory. The Admin MFA exemption is unchanged.

## Shared product registration

New Stock and Setup → Configuration → Products use the same registration fields,
validation and save logic in `_ProductRegistrationForm.cshtml` and
`product-registration-form.js`. Both save the product and its initial item
configuration through `/api/products`. Products registered from either location
appear in the Setup product list and are available for receiving stock. Setup
refreshes its list after registration; New Stock adds the configured item to the
current receipt. Registration alone does not create stock or individual units.
Existing product edits support additional variants, each with its own SKU,
barcode, prices and specifications. This form change requires no additional
database migration; category-specific brands still require migration 032 below.

## Category-specific brands (migration 032)

Stop StockFlow, back up the application database, and manually execute
[`032_brand_categories.sql`](migrations/032_brand_categories.sql) once as PostgreSQL
superuser after migration 031, then restart the updated application.
The application does not run this migration automatically. It backfills category/brand
assignments from existing products. Brands without existing products need category
assignments in Brand registration. Assign every applicable category, including each
subcategory; parent assignments do not automatically include descendants.
Brand registration supports multiple categories and creating a category in the same
dialog for users with category creation permission. Creating a category saves it
immediately. Product registration and receipt registration filter the brand dropdown
by category; changing category clears an incompatible selection. Server validation
and database constraints reject incompatible pairs and removal of assignments used
by active products. Existing product history is retained.

## Administration and pilot rollout (shop workflow Phase 8)

Phase 8 uses the baseline through 027; no new migration is required. See [administration changes and one-store pilot](../docs/phase8-administration-rollout.md), [pending physical-device sign-off](../docs/phase8-pilot-signoff.md), and optional [read-only administration checks](queries/administration_rollout_checks.sql). No users or permissions are changed automatically.

## Period reporting (shop workflow Phase 7)

Phase 7 uses the existing baseline through 027; no new migration is required. Restart the rebuilt application. See [report definitions and permissions](../docs/phase7-period-reporting.md) and optional [manual read-only period checks](queries/period_reporting_checks.sql).

## Guided stock receiving (migration 026)

Stop StockFlow, back up the application database, and manually run
[`026_guided_stock_receiving.sql`](migrations/026_guided_stock_receiving.sql)
once as PostgreSQL superuser after migration 025. Restart the rebuilt app afterward.
No application migration is executed automatically. This adds atomic guided receipts,
separate opening-stock records and retry identifiers without backfilling stock.
See [deployment and permissions](../docs/phase3-guided-receiving.md) and optional
[read-only checks](queries/guided_receiving_checks.sql).

## Phase 1: automatic store assignment (no new migration)

The staff store-locking update reuses the schema through migration 025. Run
[`phase1_store_assignment_checks.sql`](queries/phase1_store_assignment_checks.sql)
manually for a read-only schema and assignment review. No new migration or automatic
database change is introduced. See [operation and validation](../docs/phase1-store-assignment.md).

## Administration, action permissions and authenticators (migration 025)

Stop StockFlow and back up the database and the application's ASP.NET Core Data
Protection key ring. Manually run
[`025_admin_security_actions.sql`](migrations/025_admin_security_actions.sql)
once as a PostgreSQL superuser after migration 024, then restart the updated app.
The script has not been executed automatically. New-tenant provisioning now also
requires migration 025. Resolve any case-insensitive duplicate role names first.

The migration converts saved role/user grants to supported module actions.
Legacy fixed-column permission tables remain for reference but no longer control
access. Dispatch/receive/cancel grants move under Stock Transfers. New variants,
barcode, adjustment and role-management actions derive from their previous
permissions; review the converted grants with
[`admin_security_checks.sql`](queries/admin_security_checks.sql).
User grants remain authoritative snapshots: changing a role template does not
change existing users. The UI exposes actions for implemented workflows; it does
not introduce purchase reversals, sales returns, or a separate stock-adjustment
approval workflow. Those require their own business implementations.

All existing sessions end after migration. Store Manager roles require
authenticator enrollment at their next login. Admin sign-in is currently exempt
from MFA enrollment and verification, including accounts already enrolled or
covered by user/business MFA requirements. Saved MFA settings and secrets remain
in place; this exemption does not apply to other roles. Existing passwords still work;
new/reset passwords use a 12-128 character policy, configurable upward per tenant.
The small local common-password blocklist is not a comprehensive breach check.
Email addresses can be shared within a business; usernames remain tenant-unique.

Users first change any temporary password, then enroll their own authenticator
and verify a code. Ten recovery codes are shown once, stored only as hashes, and
consumed once each. Secrets are encrypted with Data Protection and bound to tenant
and user. Preserve the key ring across deployments and share it securely between
app instances; losing those keys prevents existing authenticator verification.
Authenticator attempts use a five-attempt, 15-minute lockout. Password-login
attempt limits and lockout duration can be configured in Security Settings.
The implementation follows [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238).

Authenticator reset requires its own permission, the administrator's current
authenticator/recovery code, an identity-verification confirmation and a reason.
It ends target sessions and requires enrollment again; it cannot disable a
mandatory role or business policy. An administrator cannot reset their own
authenticator this way; use their recovery codes. Credentials, OTP values and
recovery codes are excluded from audit events.

User creation saves the account, store/warehouse assignments, permissions and
audit events in one transaction. Choose a default among assigned stores; warehouse
access is separate. Assigners can select only locations visible to their business
scope. Administrative changes retain optimistic version checks and a transaction
lock protecting the final active administrator. SQL behavior must be verified
after applying the manual migration; automated checks use fake repositories.

## Category and brand registration (migration 024)

Stop StockFlow, back up the database, and manually run
[`024_category_brand_registration.sql`](migrations/024_category_brand_registration.sql)
as a PostgreSQL superuser after migration 023. Run it once, then restart the
updated application. No script is executed automatically.

Existing categories become root categories. Category and brand codes are generated
from their existing IDs and remain unchanged on rename, shared across the tenant's
stores. Category descriptions expand to 500 characters; brands gain an optional
500-character description. Existing tenant ownership and permissions are retained.

Category names are unique within the same tenant and parent (including root
categories); brand names stay unique within the tenant. Categories support three
levels, with cycle checks and full paths in product selection. Deactivation is
blocked while active products use a category/brand, or while a category has active
children. Reassign or deactivate these records first. No historical relationships
are deleted. Future creation/update actors and changes are audited; legacy actor
fields remain null rather than inventing historical attribution.

Names require 2-100 characters and meaningful text. Existing exceptions remain
untouched by the migration but must be corrected when edited. Review them using
[`category_brand_registration_checks.sql`](queries/category_brand_registration_checks.sql).

## Supplier registration (migration 023)

Stop StockFlow, back up the database, and manually run
[`023_supplier_registration.sql`](migrations/023_supplier_registration.sql)
as a PostgreSQL superuser after migration 022, then restart the updated application.
Run this script once. It is not executed automatically.

This adds immutable generated supplier codes, supplier type, alternate phone,
country/address fields, payment terms, notes, and supplier audit history.
Supplier codes use the supplier ID and are unique within each tenant; numbering
does not restart for each tenant. Existing suppliers receive country `ZZ`
(unknown); choose their actual country when editing them.

New purchases retain supplier details and payment terms. Drafts refresh supplier
details when submitted; completed purchase snapshots are preserved when the
supplier changes later. Older completed purchases are not backfilled with
invented historical details; the screen identifies their current supplier data.
Optional read-only review queries are in
[`supplier_registration_checks.sql`](queries/supplier_registration_checks.sql).

## Product registration and variants (migration 022)

Stop StockFlow, back up the application database, and manually run
[`022_product_registration_defaults.sql`](migrations/022_product_registration_defaults.sql)
as a PostgreSQL superuser after migrations through 021. Run it once, then restart
the updated application. The application never executes this file automatically.
Default prices may remain zero while configuring the catalog, but a sale line
must have a positive selling price before the sale can be completed.

This adds product description, Has Variants, warranty provider, and optional
variant/location minimum-stock overrides. Tracking Type uses the existing
`is_serial_tracked` column. Existing products, variants, SKUs, barcodes and
transaction history are retained. Existing products with non-default variants
are marked Has Variants. Existing warranty providers remain unset until edited.

Product tax/warranty values now serve as defaults for new variants. Editing
registration metadata does not overwrite variant prices or existing tax/warranty
overrides. Tracking changes are rejected once the product has purchase, sales,
stock or physical-unit history at any location. Every new product still receives
its DEFAULT variant; edit that variant for a single-variant product. Enable Has
Variants to add others. Registration never adds stock.

Location minimums apply to the selected individual stock location in inventory
and low-stock dashboards; consolidated views use the variant default. Blank
location minimum means inherit. Existing tenant-specific SKU/barcode uniqueness
and scan-identifier protections from migration 020 remain in effect. The tighter
variant SKU/warranty/minimum limits apply to new or edited rows; legacy rows are
not rewritten. Review legacy exceptions using the optional read-only
[`product_registration_checks.sql`](queries/product_registration_checks.sql).

For common variant inventory and partial receiving, manually apply [018](migrations/018_common_inventory_variants.sql) and then [019](migrations/019_purchase_receipts.sql), after migration 017. Stop the application while applying them. See [deployment instructions and verification](../docs/common-inventory.md); the application never executes these migrations automatically.

For Phase 13 dashboard enhancements, migrations through 015 are required. [Migration 016](migrations/016_phase13_dashboard_indexes.sql) adds optional performance indexes only. See [Phase 13 setup](../docs/phase13-dashboard.md) and [read-only dashboard verification queries](queries/phase13_dashboard_queries.sql).

For Phase 12 repair/service management, manually execute [migration 015](migrations/015_phase12_service.sql) after 014 and restart the app. See [Phase 12 setup](../docs/phase12-service.md) and [optional read-only queries](queries/phase12_service_queries.sql).

For Phase 11 warranty management, manually execute [migration 014](migrations/014_phase11_warranty.sql) after 013, then restart the application. Optional read-only checks are in [Phase 11 queries](queries/phase11_warranty_queries.sql). See [Phase 11 setup and verification](../docs/phase11-warranty.md). No SQL is executed automatically.

For authentication and the first admin account, see [Phase 4 setup](PHASE4.md).

For master data modules and their manual constraint migration, see [Phase 5 setup](PHASE5.md).

For purchases and stock receipt, see [Phase 6 setup](PHASE6.md). Inventory setup is described in [Phase 7 SQL](PHASE7_SQL.md), and the screens and APIs in [Phase 7 inventory](../docs/phase7-inventory.md).

For scanning and sales, see [Phase 8 scanning](../docs/phase8-scanning.md) and [Phase 9 sales](../docs/phase9-sales.md). With earlier migrations applied, manually run migrations 010, 011 and 012 in order. The application does not apply migrations automatically.

For printable invoices and browser PDF, manually run `migrations/013_phase10_invoices.sql` after 012, then configure **Company / invoice settings** as Admin. See [Phase 10 invoices](../docs/phase10-invoices.md).

These files preserve the SQL from the supplied Phase 2 roadmap. The scripts have already been executed manually according to the project owner; saving them here does not execute or verify them against the live database.

For a new database, run the scripts in this order:

1. `scripts/001_create_database.sql` — connect to a maintenance database such as `postgres`; run outside a transaction.
2. Connect to `stockflow_db` for all remaining scripts.
3. `scripts/002_create_master_tables.sql` — roles, users, categories, brands, suppliers, customers, products, product units, audit logs, and their indexes.
4. `scripts/003_create_purchase_tables.sql` — purchases, purchase items, inventory transactions, and their indexes.
5. `scripts/004_create_sales_tables.sql` — sales, sale items, payments, and the invoice index.
6. `scripts/005_create_warranty_service_tables.sql` — warranties, service requests, service history, and the service status index.
7. `scripts/006_seed_data.sql` — Admin/Sales/Service roles and the five product categories.

Scripts 002-005 use `IF NOT EXISTS` for tables and indexes; script 006 skips existing role/category names with `ON CONFLICT DO NOTHING`. They can be rerun to create missing objects without replacing existing ones. Existing objects are not checked or upgraded to match these definitions; store schema changes in `migrations/`. Script 001 must still be skipped when the database exists because PostgreSQL does not support `CREATE DATABASE IF NOT EXISTS`.

The supplied definitions are retained, including the optional unique SKU index. No additional constraints or schema changes are introduced. In particular, `product_units.purchase_id` and `product_units.sale_id` remain columns without foreign key constraints, as in the roadmap. Status values remain unconstrained strings.

No database connection, repository code, or application user credentials are added by these scripts.

## Separate insert scripts

The `inserts/` folder contains separate scripts for the same role and category seed data. Use these as an alternative to `scripts/006_seed_data.sql` after creating the tables. They skip existing role/category names using `ON CONFLICT DO NOTHING`, so they can also be used when the seed data already exists without changing existing rows.

## Daily cash closing and shop readiness (migration 035)

With migrations through 034 applied, stop StockFlow, take a database backup, and
manually apply [035_cash_closing.sql](migrations/035_cash_closing.sql) as a PostgreSQL
superuser. Restart the updated application. StockFlow does not apply SQL automatically.

Owners and admins with Reports access can open **Reports → Daily cash closing**.
Select one shop location, refresh the day's totals, enter opening cash, count the
drawer, and save. Expected cash is opening cash plus cash from sales and repairs,
minus cash refunds and supplier payments, plus extra cash added, minus extra cash
removed. UPI, cards, bank payments and unpaid credit do not enter the drawer total.
Do not include the displayed refunds or supplier payments again as extra cash removed.

A reason is mandatory for a difference, extra movement or correction. Corrections
create new versions; previous closings remain available. A closing saves a snapshot
and permits later payments. If later activity changes the total, refresh and recount.
After an interrupted save, use **Retry saved closing**; the same request is retained
in the same browser tab and cannot create a duplicate closing.

The business day defaults to `Asia/Kolkata`. To configure it, add
`"ShopTime": { "TimeZoneId": "Asia/Kolkata" }` to deployment configuration.
Legacy sales payment timestamps use the database session time zone; keep that zone
consistent with the zone used when those records were written. Review historical
timestamps before changing the database time zone.

### Backup, restore test and daily schedule

The scripts in [maintenance](maintenance) use PostgreSQL's installed command-line
tools. Set `-PostgresBin` to your installation if it differs from PostgreSQL 18.
Use a trusted database maintenance account that can read the complete database.
For the restore test, that account must also create/drop databases and restore
object ownership. The examples below use placeholders; replace every placeholder
with the actual deployment setting.

Keep a PostgreSQL password file outside the repository, readable only by the Windows
account running the backup. Its line format is
`host:port:database:username:password`; use a separate `postgres` maintenance-database
entry for the restore test, or a database wildcard for that account. Escape `:` and
`\` in passwords according to the password-file format. Do not put passwords in
command arguments or commit the password file. Set `PGPASSFILE` before running:

```powershell
$env:PGPASSFILE = 'D:\StockFlowPrivate\pgpass.conf'
& .\database\maintenance\Backup-StockFlow.ps1 -DatabaseName '<database>' -BackupDirectory 'D:\StockFlowBackups' -ServerHost '<host>' -Port 5432 -DatabaseUser '<maintenance-user>'
& .\database\maintenance\Test-StockFlowRestore.ps1 -Archive 'D:\StockFlowBackups\<archive>.dump' -ServerHost '<host>' -Port 5432 -DatabaseUser '<maintenance-user>'
& .\database\maintenance\Install-StockFlowBackupTask.ps1 -DatabaseName '<database>' -BackupDirectory 'D:\StockFlowBackups' -PasswordFile 'D:\StockFlowPrivate\pgpass.conf' -ServerHost '<host>' -DatabaseUser '<maintenance-user>' -DailyAt '21:30'
```

Backup writes a custom archive, checks that PostgreSQL can read it, and records a
SHA-256 manifest. Only complete archives count as successful backups. By default,
older archives for that database are retained for 30 days. Copy successful archives
and manifests to a separate protected device or off-site destination; the script
does not perform that copy or encryption.

Restore testing checks the checksum first, then creates its own uniquely named
temporary database. It checks application tables, indexes and cash-closing row
security, saves a `.restore-test.json` report, and removes its temporary database.
It never restores over the shop database. The current script tests recovery on the
same server, with existing roles and object owners. Recovery onto a new server also
requires provisioning those roles/owners, deployment configuration and external
files or certificates; a database dump does not include those resources.

The installer creates a Windows Task Scheduler job under the current Windows
account, runs without an interactive password prompt, and refuses to replace an
existing job. It uses S4U and therefore requires local password/backup paths. Check
directory permissions and run the task once; confirm `LastTaskResult` is 0 and a new
archive/manifest exists. A restricted system may require an administrator to register
the task. Its PowerShell execution-policy setting applies only to that job process,
so protect the runner and repository scripts against edits by other accounts.
No task is installed merely by building or starting StockFlow.

### Language choice and shop acceptance

After migration 035, manually apply [036_screen_languages.sql](migrations/036_screen_languages.sql)
with StockFlow stopped, then restart the updated app. Language settings are now
stored per business in the database instead of in a browser preference.

If `42P01: relation "screen_language_settings" does not exist` appears, check the
database selected by `ConnectionStrings:DefaultConnection`, including Development
or environment overrides. Migration 036 must be applied to that database, after
035. The application now checks whether the table is available before reading it:
ordinary screens use English and the header shows a setup message if saving fails. The application still does not apply migrations at startup.

Choose a language from the **top-bar language dropdown**.
Owners/admins need Company settings Edit permission to change the business language.
The selection saves automatically; there is no Configuration language page or Save button. The business-wide choice applies to every screen:
known captions and validation messages display **English (translation)**.
The current page updates immediately; other tabs update automatically on focus,
and active pages on other devices check for changes every 30 seconds.
No rebuild or selected-screen preview is needed after saving.

English and all 22 scheduled Indian languages remain selectable. Every listed
language has a bundled caption and validation-message pack. The app loads the
selected pack locally; it does not send screen content or business data to a
translation service. New wording without a translation stays English. Product and
customer names, typed values and saved codes stay as entered.

The simplified setting uses the existing migration 036 table with the reserved
`application` key. No new database script is needed when 036 is already applied.
Older per-screen rows are retained for history but no longer control the UI.

Owners/admins can open **Setup → Shop pilot checklist** at `/Admin/Pilot`.
Record the scanner, printer, build and backup evidence, perform every check at the
shop, and record the result. Sign-off requires all checks to pass with evidence.
Any edit invalidates the previous sign-off. The checklist is stored locally in the
browser; export its JSON and keep it with the owner. It is an operator's record,
not a centrally stored approval or automatic hardware certification.

Run `tests/Run-ShopPilotChecks.ps1` for automated checks. Add `-WithDatabase` only
with a disposable PostgreSQL test server on loopback port 55441. Physical scanner,
printer and staff-understanding checks remain pending until performed at the shop.
## Inventory control and transfers (shop workflow Phase 6)

After existing migrations through 026, manually apply [027_inventory_transfer_actions.sql](migrations/027_inventory_transfer_actions.sql) as PostgreSQL superuser with StockFlow stopped, then restart. It repairs transfer action authorization and preserves movement serial history without changing stock quantities or granting permissions. See [Phase 6 deployment and workflow](../docs/phase6-inventory-transfers.md) and the optional [read-only inventory/transfer checks](queries/inventory_transfer_checks.sql). No SQL is applied automatically.

## Stock receipt history (migration 031)

After migration 030, stop StockFlow, back up the database, and manually apply
[031_stock_receipt_history.sql](migrations/031_stock_receipt_history.sql) as a
PostgreSQL superuser. It creates permanent GRN numbers and receipt status
metadata, backfills documentary receipts for legacy completed purchase movements,
and renames the database-driven navigation item. It does not post or recalculate
stock. Restart the updated application, then run the read-only
[stock receipt history checks](queries/stock_receipt_history_checks.sql).

## Customer payment dates (migration 037)

After 036, manually run
[037_customer_payment_due_dates.sql](migrations/037_customer_payment_due_dates.sql)
as the database administrator and restart StockFlow. Counter billing can then
record an optional agreed payment due date for named-customer credit sales.
StockBot classifies only dated unpaid invoices as overdue; existing invoices are
left without dates. The application does not execute this script automatically.

