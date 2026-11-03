# Multi-business Phases 3 and 4: category fields and product registration

Phase 3 adds reusable, typed category attributes scoped to the authenticated company
and shop profile. Phase 4 uses them in the existing Products registration and edit
screens. Bike Accessories, Clothing and Footwear share the same implementation.
Existing Electrical templates, tracking, warranty and transaction paths remain.
Receive Goods → Add new product still navigates to Products.

## Included behavior

- Categories → **Category Fields & Variants** (`/Categories/Attributes`) supports
  text, number, date, yes/no and selection attributes. Number fields can have limits.
  Category VIEW permits review; EDIT permits configuration changes.
- Codes are stable lowercase storage identifiers. Explicitly assign fields to each
  current-profile category; no automatic inheritance applies. Required selection
  fields can become variant axes, with at most three axes per category.
- Optional shop suggestions are editable: clothing size/color/fabric, footwear UK
  size/color/material, or bike size/color/vehicle fitment. Load suggestions into an
  empty attribute list, then assign and save them. Use separate size definitions for
  categories with different sizing systems. Existing Electrical templates are retained.
- Products → Add Product displays the category's typed fields. Switching categories
  retains completed values if you switch back and clears the combination preview.
  Incomplete custom specifications must be completed or removed before switching.
- With Has Variants enabled, select options and preview combinations. Preview up to
  200 rows, then select 1–30 to save. Each selected variant has a unique SKU, optional
  manufacturer barcode, purchase price and selling price. Suggested SKUs use the
  entered prefix and preview row number; review them before saving. Blank manufacturer
  codes use the existing internal barcode process. The preview's barcode columns
  replace the single item's Existing Code/Other barcodes fields.
- Common attributes must match across all variants, including inactive variants.
  Tax, MRP, pack size and reorder defaults are copied from the form. Existing variant
  commercial rules still apply, including selling price not exceeding a supplied MRP.
- Product edits restore variant IDs and values. Saved axis values and configured
  product categories are locked: add a new variant/product for a different identity.
  Common fields shown in each edit card must be changed consistently across all
  variants; conflicting values are rejected. Saved variants are never deleted here.
- Validation applies to registration and direct variant APIs. Database constraints
  validate the final transaction state, allowing the existing automatic DEFAULT row
  to be filled during registration without permitting incomplete committed records.
- Used category assignments and referenced definitions lock once any product exists
  in the category, including inactive products and other branches. Unused categories
  remain configurable. Create a new category for a different setup; do not delete
  products to unlock configuration.
- Settings use version checks, the existing tenant catalog transaction lock,
  permission checks and audit entries. Runtime table access is SELECT only; the
  database checks permission again inside the settings save function.

Definitions are stored as one versioned JSON document per company/profile. Values
use the existing variant `attributes` JSON as strings, with typed controls and
server/database validation. This preserves compatibility with existing receiving,
search and stock paths. Common attributes are copied consistently into variants.
These phases do not introduce a separate stock engine or convert existing data.

## Manual database deployment

No SQL was executed during development. Startup does not run migration 048.

1. Back up the intended database and record the Electrical baseline using the Phase 1
   read-only queries. Stop application writers during deployment.
2. Ensure migrations through **047** are applied. Do not rerun 046 over a database
   using other profiles: its Electrical-only constraint belongs to the historical
   Phase 1 deployment.
3. As PostgreSQL superuser, manually execute
   [048_category_attribute_schemas.sql](../database/migrations/048_category_attribute_schemas.sql).
   This adds configuration storage, narrow permissions and validation triggers.
   It does not seed fields, alter products or post stock. No separate Phase 4 SQL
   script is needed.
4. Restart the updated application. Run the
   [read-only verification queries](../database/queries/category_attributes_phase34_verification.sql)
   as administrator. Expect the table, RLS policy, checked functions and two enabled
   deferred triggers. Runtime table privileges must be SELECT only.
5. Compare Electrical product/variant/stock/history totals with the baseline. Before
   any settings save, the schema table should be empty on a first deployment.

Without 048, existing registration keeps its previous templates/custom specifications;
category setup explains that configuration is unavailable. Do not remove 048 after
adoption: removing it removes integrity checks. Rollback needs a coordinated plan.

## Steps to verify after deployment

Use isolated test companies/database for registration, receiving and sales. Keep
the existing Electrical company unchanged. Populated companies cannot switch shop
profile; use an empty company for each of the three other shop types when needed.

1. In Clothing, create a **new unused category**, such as Phase34 Shirts. Open
   Categories → Category Fields & Variants. Load suggestions if the reusable list
   is empty, or add individual fields. Assign Clothing size and Color as required
   variant axes and Fabric as a required common field. Save.
2. Open Products → Add Product and select Phase34 Shirts. Expect Size, Color and
   Fabric, without battery/geyser templates or electrical warranty fields. Saving
   without Fabric must fail. To test other types, configure Number with limits,
   Date and Yes/no on another unused category before registering its first product.
3. Enter a name, SKU prefix `SHIRT`, default prices and Fabric `Cotton`. Enable Has
   Variants. Choose M/L and Black/Blue. Preview four combinations, deselect one,
   review the remaining SKUs/barcodes, and change one row's selling price. Save.
   Expect one product with exactly three variants, separate prices and internal
   barcodes where manufacturer codes were blank. Registration must not add stock.
4. Try duplicate SKUs/barcodes and an existing database SKU conflict. Expect an
   error, retained preview/values and no partial product or stock changes. Duplicate
   option combinations through the API must fail. More than 30 selected variants
   must fail before saving; narrow previews larger than 200 combinations.
5. Edit the saved product. Confirm three variants reload and category/axis values
   are locked. Change a price and add a new unused combination with Add size / model.
   Original IDs, barcodes and stock must remain unchanged. Direct API attempts to
   relabel a saved axis/category must fail as well.
6. Reopen category setup. Used assignments and referenced definitions must be locked,
   including after deactivation or registration in another branch. Unused category
   settings remain editable. Save in one of two open setup tabs, then save from the
   older tab: expect a stale-version error and a request to reload.
7. Repeat in Footwear with UK size/color/material and unit Pair where appropriate.
   Repeat Bike Accessories with helmet size/color/vehicle fitment. Only the active
   profile's categories and definitions should appear. Check that another company
   cannot read the first company's settings.
8. Check a read-only category user: review works, save does not. Missing VIEW blocks
   page/API; missing EDIT or CSRF blocks save. Existing Product and Product Variant
   creation/edit permissions must still apply.
9. Switch categories before saving a new product. Field sets must change, returning
   must restore completed values, and previews must be regenerated. Check desktop
   and phone layouts.
10. In Electrical, verify existing battery/inverter/geyser registration and edits,
    templates, serial tracking and warranty. In the test database, receive and sell
    a serialized item. For each new profile, receive/sell one selected variant and
    confirm the other variants' stock stays unchanged. This checks existing paths;
    new Phase 5 transaction features are outside these phases.
11. Run the read-only verification queries again. Missing required configured values
    must return zero rows. Review Category attributes audit entries and compare
    unaffected Electrical stock/history with the baseline.

## Automated checks without SQL

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-MultiBusinessPhase34Checks.ps1
```

The runner builds separately from IDE-loaded DLLs, runs typed validation and
authenticated API checks with fake repositories, the Phase 2 profile suite, the
Electrical regression suite and Edge/Playwright tests with mocked APIs. Logs and
results are in `tests/bin/category-attributes-results`.

Checks cover all three new profiles, dynamic fields, subset previews, individual
prices, category value retention, failed-save retention, edit identity guards,
setup/suggestions/mappings, permissions, CSRF and responsive layouts. They do not
execute SQL or certify PostgreSQL RLS, concurrency, deferred triggers or real database
persistence. Those remain pending until 048 and the manual checks are executed.
Physical scanner/printer validation remains a shop acceptance task.
