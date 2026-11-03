# GST implementation: impact analysis and delivery record

Scope: stages 1–3 of the supplied GST specification. Return preparation, GSTR-2B import, filing, IRN generation and external provider connections remain out of scope.

Current evidence correction, 2 November 2026: GST registration/mapping and working
registers below are implemented. Source-linked e-way preparation was subsequently
added in manual migration 084; official generation is still unavailable. Current
checkout preserves saved GST snapshots and allocated invoice discounts. The next
section describes the earlier baseline, not missing current functionality.

## Historical source-of-truth audit before implementation

- Company settings already store legal name, address, state, PIN, PAN and GSTIN, but are not an effective-dated multi-registration ledger.
- Operational shop isolation uses tenant, business profile and store/location. GST mappings must retain all three identities; profiles must never be inferred from category names.
- Product registration already supplies HSN, unit and percentage. Customer and supplier registration already provide optional GSTIN and address; supplier state exists. No generated GSTINs or catalog identifiers are appropriate.
- SaleRules.CalculateLines is authoritative for line values, inclusive/exclusive pricing and rounding. Modern allocated invoice discounts reduce the taxable base in their saved snapshots; legacy after-tax allowances retain their original semantics. Historical documents must not be recalculated with today's defaults.
- SalesRepository finalizes invoices, payments and stock in one transaction, after idempotent-request recovery. Company/customer snapshots exist; saved HSN, place of supply and separated tax components need additional snapshots.
- Purchase orders, stock intakes and partial GRNs are operational records, not automatically distinct supplier tax invoices. Purchase-tax recording needs explicit supplier invoice identity and duplicate protection.
- Returns/refunds are operational events. A refund is not automatically a GST credit note. Explicit linked tax adjustment documents must preserve the original and must not post stock or payments again.
- Existing permission actions, forced RLS, audit history, shared pagination and CSV-export conventions will be reused.

## Financial-table impact before implementation

Use additive GST tables, effective-dated configuration and immutable document/line snapshots. Do not backfill unknown legacy tax context from current masters, change existing amounts, renumber invoices, regenerate SKUs/barcodes, post stock, or alter payments. Configuration remains opt-in: existing unmapped shops continue unchanged. A mapped shop must provide a supported, consistent tax context before GST capture can succeed. Registered configuration must not claim portal verification.

Registration-level tax access must be explicitly permissioned and tenant-bounded. It may read GST snapshots from mapped profiles/locations, not unrestricted operational tables. Cross-tenant ownership consolidation requires a separate design and is not an authorization shortcut.

## Regulatory references checked 2026-11-01

- CBIC Rule 46 invoice particulars: https://taxinformation.cbic.gov.in/content-page/explore-rules/1000136/1000001
- GST Portal GSTR-2B FAQ (auto-drafted ITC statement, not a return): https://tutorial.gst.gov.in/userguide/returns/FAQ_gstr2b.htm
- GST Council circular 170/02/2022: https://gstcouncil.gov.in/node/4891

Working registers and templates require GST-practitioner acceptance before production. No export or manual review establishes filing, portal verification or ITC eligibility.

## Delivered working scope

- `/Admin/Gst`: five sections for legal details, registrations, profile/store mappings, effective tax classifications and configuration audit history. Existing company details can initialize a linked legal-business record; the company master is not overwritten.
- `/Reports/Gst`: overview, sales/purchase registers, HSN/SAC groups, linked credit/debit notes, document drilldowns, append-only review history and accountant CSV exports with scope, period, actor, version and hash history. School ERP shared pagination is used for report rows.
- `070_gst_foundation.sql` and `071_gst_source_documents.sql` are manual additive migrations. The original delivery did not apply them to the business database; later audit metadata found them locally. Neither statement establishes the current production migration baseline.
- The existing SaleRules calculator remains authoritative. Mapped shops consume the reviewed effective product rate; unregistered/composition issuers collect zero GST. GST capture occurs in the existing invoice transaction, with an additional deferred commit guard, immutable source/line facts and no second stock/payment posting.
- POS place of supply is explicit, not inferred from the customer's GSTIN. Registered/high-value/requested recipient particulars need a named customer, billing address and delivery address. New optional request fields are omitted when null to preserve legacy idempotency payloads.
- Invoice printing reads saved GST particulars, HSN/UQC and components; it does not replace old invoices with current GST masters. Old documents without these snapshots remain unclassified legacy coverage, not reconstructed tax records.
- Supplier invoices are explicitly recorded with evidence references and one or more authorized purchase links. They are not inferred from GRNs. Duplicate identity is protected by registration, supplier, normalized number and financial year, in addition to the request ID. Purchase details link to the source-entry workflow.
- Sales notes are issuer notes; purchase notes represent supplier-issued notes. Refunds and buyer returns alone do not automatically create GST tax notes. Source notes do not post stock or payments again. Credit limits prevent cumulative quantities/components/value exceeding the original recorded source.
- `GST_SETTINGS` and `GST_REPORTS` use distinct backend/RLS permissions. Only existing tenant owners receive the new actions on migration; staff need explicit assignment. Registration-wide reporting reads tax snapshots, not unrestricted operational rows or payment payloads.

## Explicit limits and later stages

This is a working domestic-retail-goods foundation, not a complete statutory compliance release. Service place-of-supply, exports/SEZ/zero-rated issuance, reverse-charge and cess invoice calculation, and alternative `OTHER` registration issuance are blocked rather than guessed. Their configuration metadata can be reviewed/stored in preparation for separately verified workflows. Product-specific mixed inclusive/exclusive modes within one invoice are not supported by the existing calculator; mismatches are rejected.

GST rates are not hardcoded slabs. Effective rates use the existing POS's two-decimal persisted rate precision. HSN/SAC/UQC receive format checks and explicit review references, not a claim of authoritative classification validation; turnover-specific digit requirements and maintained official reference imports remain future work. Rule 46 recipient checks currently use a conservative gross-invoice threshold of INR 50,000 and the customer's explicit request; a practitioner must confirm each applicable invoice scenario.

The current invoice-level discount remains after tax and is flagged for review; it is not automatically reclassified as a statutory taxable-value reduction. Tax-register figures preserve issuance/recording facts. Source cancellation events are visible, but must be reconciled with controlled notes; these figures are not a final payable calculation. Purchase ITC eligibility, reverse-charge liability and final tax position are deliberately not computed.

Source entry records supported document amounts and references; it does not import/verify a supplier's external invoice, replace purchase costing, or claim that every book invoice has been captured. Purchase-register completeness needs an accountant's source-document reconciliation. No historical GST-component backfill is performed.

Shop identity follows existing tenant/profile/store/location boundaries. Existing catalog-sharing behavior within one tenant/profile is unchanged. Cross-profile registration reporting is supported within the tenant; cross-tenant consolidation is not implemented. Independent tenant businesses are never merged because their owner or GSTIN text matches.

Stages 4–6 are not exposed as functional modules: no GSTR-1/3B preparation approval, official return JSON, GSTR-2B import/matching, provider connection, filing, IRN or e-way-bill generation. GSTR-2B remains an auto-drafted statement, not a return. CSV exports and internal accountant review do not change any filing/ITC status.

## Verification and environment limitation

The solution and isolated test harness build with zero warnings/errors. `--gst-template-checks` exercises migrations 070/071 on a GUID-named disposable clone of `product_edit_069_test` on loopback port 55443, then drops only that clone. It checks checksum/state rules, mapping and tax-version overlap, immutable snapshots, intrastate/interstate tax allocation, document totals/HSN grouping, supplier-invoice retries/duplicates, notes, reviews, exports, permissions, profile aggregation, separate GSTINs, unregistered no-tax behavior and real POS finalization/replay with one stock deduction.

`--gst-http-checks` verifies permission routing, denied access, anti-forgery and Razor rendering. Existing `--billing-checks` verifies payment compatibility, fully paid checkout, discount/inclusive rounding behavior and legacy recovery. `tests/StockFlow.UiChecks/gst-offline.cjs` uses mocked APIs to test both new screens at 1920×1080, 1366×768, 1280×650, 1024×768, 768×1024, 390×844 and 320×640, failed-save field retention, drilldown, shared paging and computed compact action sizes. Browser mocks do not establish database correctness or Windows display-scaling behavior on physical hardware.

Fresh migration replay is currently blocked by a pre-existing repository issue: `049_retail_variant_document_snapshots.sql` contains only two bytes, and migration 050 explicitly requires the function it should install. It was not replaced with an invented stub. Restore/review the genuine migration before clean-install validation or production rollout. Tests against the preserved migration-069 disposable template do not remove this prerequisite.

No complete application-wide regression, live shop audit, physical printer/scanner check or practitioner sign-off is claimed. Production rollout remains pending those checks and recovery planning. Do not activate a mapping until classifications, statutory particulars and supported issuance scenarios are reviewed. Once a shop has mapping history, missing/expired/inactive effective registration fails closed; it does not silently revert to legacy unmapped billing.

Rollout must stop writers, verify a restorable backup, resolve the genuine migration-049 prerequisite, apply 070 then 071 as postgres, and deploy the accompanying application before reopening mapped billing. Do not drop GST history or remove the commit guard to roll back an active installation. After mapped transactions exist, use a forward fix or a separately rehearsed, reconciled restore plan; reverting to an older application alone can correctly block billing because it cannot produce the required snapshots.

## Main implementation files

- Application: `Common/GstRules.cs`, `Common/GstDocumentRules.cs`, `Common/PermissionCatalog.cs`, `DTOs/Gst/*`, `Interfaces/IGstRepository.cs`, sales optional GST request fields and invoice GST DTOs.
- Infrastructure: `Repositories/GstRepository.cs`, `Repositories/GstDocuments.cs`, sales quote/finalization integration and invoice snapshot reading.
- Web: `Controllers/GstController.cs`, POS issuing context, `Filters/GstExceptionFilter.cs`, permission route mapping and DI; `Pages/Admin/Gst.cshtml`, `Pages/Reports/Gst.cshtml`, POS/invoice/purchase/sales links; scoped `gst.css`, `gst-settings.js`, `gst-reports.js` and POS draft preservation.
- Tests: `GstChecks.cs`, `GstHttpChecks.cs`, test command routing/permission fixture and `gst-offline.cjs`.
