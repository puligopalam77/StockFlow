# GST Settings guided setup

The GST Settings page now opens with a five-step setup for a new business and a saved configuration overview for an existing business. Forms open through explicit Add/Edit actions. GST Reports keeps its existing workflow.

## Database requirements

No additional SQL migration is required. This change reuses migration 070 (GST foundation) and 071 (source documents). Migration 072 is the separate CRM release. No migrations were applied to the application database during this work.

Activation changes an existing shop assignment from INACTIVE to ACTIVE after server-side readiness checks. It does not create a new activation table, verify GST Portal status or provide statutory approval. Existing active assignments remain active.

Important existing behavior: any GST mapping history starts the checkout GST guards, even if the assignment is inactive. Finish configuration before billing that shop. The assignment form and confirmation explain this behavior.

## Implementation

- `Pages/Admin/Gst.cshtml`, scoped `gst-settings.css`, and `gst-setup.js`: guided steps, saved cards, edit forms, company-copy preview, explicit shop-assignment confirmation, readiness review, responsive layout and separately filtered audit history.
- Drafts remain in memory across steps and failed saves. Shared unsaved-change protection guards departure; company copy replaces entered values only when explicitly selected.
- `GstSetup.cs`: server-side readiness, configuration fingerprint, serialized activation, identical-create reuse, audit pagination and identity masking for read-only settings users. Existing permissions, row security, effective-date guards and audit triggers remain authoritative.
- Readiness checks saved business/registration identity and effective dates, selected shop/profile assignment, active-product tax coverage, pricing and supported issuance conditions. Activation rejects incomplete or stale reviews. Retrying activation does not create another audit event.
- Audit date, registration, direct shop-assignment, staff-ID and action filters use the existing audit records. Registration filtering excludes shared product tax changes; shop filtering applies to assignment changes. The UI states these limits.
- No changes to posting calculators, payment collection, invoice numbering, product identifiers or inventory transactions.

## Validation

- Isolated .NET build: zero warnings or errors.
- Disposable PostgreSQL clone: concurrent identical creates, retry reuse, incomplete/stale activation rejection, safe activation retries, audit filters/pagination, read-only restrictions, shop/tenant isolation and unchanged sales/stock/payment counts passed.
- Existing GST database regression checks passed: tax snapshots, adjustments, reports, effective mappings, permissions, POS idempotency and stock deduction.
- HTTP/Razor rendering checks passed.
- Mocked Edge browser checks cover overview, forms, audit and wizard at 1280×720, 1280×650, 1280×600, 1024×768, 768×1024, 390×844, 375×667 and 320×640 CSS pixels. Separate GST Reports browser regression covers seven viewports, document drilldown, compact actions and pagination.
- Browser tests use mocked APIs; database checks run separately. This is not a production deployment or a physical Windows display-scaling test.

The database checks clone an existing through-069 test template. A complete fresh-install replay cannot be claimed because the repository's migration 049 file is empty. No replacement migration was fabricated.

GST working reports and document templates still require the existing practitioner review before production use.
