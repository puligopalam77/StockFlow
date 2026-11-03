# Brand registration

Brand Management is a flat table with Brand (name/code/logo), Applicable Categories,
Products, Status and View/Edit/Deactivate actions. Summary cards show total brands,
active brands and linked products in the current authorized shop/setup. Search also
matches aliases and associated categories. Status filtering includes inactive records.

The Add/Edit form has Basic Brand Information, Applicable Categories and Additional
Information. Brand names remain limited to 100 characters; editable codes use 2 to
20 uppercase letters, numbers or hyphens. Descriptions retain the existing 500-character
limit. Aliases remain available so existing alternate brand names are preserved.

Register each brand once and select multiple categories as needed. Applicable to all
categories saves no recommendation links. Otherwise, selecting a main category with
no specific subcategories covers its descendants. Selecting specific subcategories
saves those links only, and covers their descendants. The category UI cannot assign
a subcategory outside its selected main category. Existing inactive links can be
retained or removed; new inactive links are rejected. The brand/category tenant FKs,
business-profile visibility and existing product identities remain authoritative.

Product registration uses server-derived applicableCategoryIds to recommend brands
for a category or its descendants; unassigned brands remain available for all categories.
Show all brands allows legitimate brands outside their recommended categories.
Older setups before 052 retain exact-category restrictions.

Website values must be HTTP/HTTPS URLs. Logo uploads allow JPG, PNG and WebP up to
2 MB, with server-side signature and size checks. A logo is stored in the database
and returned only through an authenticated, brand-view-protected endpoint. List and
detail responses expose a URL, never base64 payloads. Omitted uploads preserve the
existing logo; Remove logo explicitly clears it. Website/code/logo fields remain
disabled or automatic before the storage migration is applied.

## Deployment

Back up and stop application/workers; manually apply
[058_brand_registration_details.sql](../database/migrations/058_brand_registration_details.sql)
after 057 as PostgreSQL superuser, then restart StockFlow. The application does not
execute migrations. Existing brand IDs, generated code values, category mappings,
product links and transaction history are preserved. Existing tenant-scoped code
uniqueness and RLS remain enabled. New/renamed names and aliases are checked across
the shop under a tenant advisory lock, ignoring case and whitespace. Pre-existing
cross-profile duplicates are retained when their identities are unchanged; the
migration does not merge brands or broaden profile visibility.

The existing creation/update actor stamps remain active. Brand audit entries keep
logo metadata and a change fingerprint instead of repeating image bytes on every edit.
Ordinary category registration remains unchanged and requires no new configuration.

## Validation

Build and run MasterDataChecks with --brand-registration-checks. It covers migration
gating, normalized duplicate names/codes, multi-category descendant recommendations,
URL/logo validation, authenticated logo access, legacy update preservation and removal.
Browser checks: brand-registration-offline.cjs and brand-categories-offline.cjs.
These use real Razor/JS/CSS with mocked APIs and fake repositories. PostgreSQL
transactions, migration execution and cross-shop RLS acceptance remain manual;
use the read-only query file after deployment.
