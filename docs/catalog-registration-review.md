# Product, category and brand registration improvements (052)

## Format 1: tab-based Add New Product

The Variants tab follows the reference: pink variants switch, Size/Color/Model/
Material/Style attribute choices, dropdown option chips, combination count, Generate
Variants, and a compact Variant/SKU/Barcode/Actions table. Edit reveals per-variant
price controls; Delete removes a generated row. Select-all controls inclusion.
Categories with no configured axes offer Size and Color by default (052 enabled).
Configured required axes remain fixed, and common product attributes cannot become
variant axes through this form. Variant prices
follow Pricing & Stock defaults until explicitly edited in the variant table.
Compatibility includes explicit specific/universal/not-applicable choices, make,
model, generation, from/to years, position, optional notes and a removable vehicle
list. Not-applicable is disabled when the category requires fitment. These values
use existing variant attributes; no migration is needed.

Pricing & Stock has Pricing Information, Inventory Settings and Supplier Information
sections. “Entered prices include GST” converts entered purchase/selling amounts to
their before-GST values at submission; MRP is retained. The global reorder level
and optional current-shop override are separate inputs. Supplier code rows are
optional but must have both supplier and code when partially entered.

Media includes an automatic main-image preview, a description synchronized with
Basic Details, a summary of configured specifications, and editable additional
specifications. Multiple-image uploads are available after manual migration 053:
see the [gallery guide](product-image-gallery.md). Reorder overrides apply to the
current shop; editing several branches together remains separate. Product status changes
still happen after registration; new products start Active. The previous two-button
Back/Next, Previous/Next, Previous/Submit footer is retained.

This UI update requires no additional migration. Existing migration 052 still
enables its catalog enhancements; the tab layout also works before 052. No saved
products, stock, prices, history or business configuration are changed by the UI.

The shared Add New Product form now groups existing controls into Basic Details,
Variants & Attributes, Compatibility, Pricing & Stock, and Media & Specs. Category
configuration supplies the attribute fields and variant axes. Compatibility appears
only when visible fitment fields are configured. Legacy electrical specification
templates and warranty controls remain in Media & Specs. Tracking follows existing
shop permissions; unsupported batch/expiry tracking is not introduced by this layout.

### Verify the tab layout

1. Restart the updated application and refresh Products with Ctrl+F5. Click Add
   Product: Basic Details should open with the existing SKU and barcode tools.
2. Enter a product name, category and SKU. The category search appears only inside
   its open dropdown. Click Next: required Basic Details, duplicate product identity,
   SKU and barcode must be checked before advancing, with no separate duplicate button.
   Move between tabs without losing values. The dialog must retain its size and
   position while its content scrolls internally.
3. Choose a Bike category configured for vehicle compatibility. Its Compatibility
   tab should appear. Add two verified vehicle fitments and check both remain under
   the same product. Categories without fitment fields should hide that tab.
4. In Variants & Attributes, check Has Variants and preview configured size/color
   combinations. Review each SKU/barcode and price. Changing category after creating
   a combination preview must ask before clearing that preview; Cancel keeps it.
5. Basic Details uses compact Product Information and Additional Details cards.
   Manufacturer part number, HSN and short description are in Additional Details.
   New products display Active; change their status after registration. Set prices,
   GST, pack size, reorder level and supplier codes in Pricing & Stock. Product
   images and additional specifications belong in Media & Specs. Generate buttons
   sit beside SKU/barcode inputs, and text uses a smaller desktop size.
6. Check the footer: Basic Details has Back (closes the form) and Next; middle tabs
   have Previous and Next; Media & Specs has Previous and Submit. Next validates
   the current section. Submit validates all applicable sections. An invalid selling
   price must block saving, mark Pricing and display the field needing correction.
7. Submit a test product and check it appears in Products. Open Add Product again:
   it must start in Basic Details with cleared values and a fresh SKU.
8. Repeat with an Electrical configuration: serial tracking, battery/inverter/geyser
   specification templates, warranty and image upload must still work. Check at a
   narrow browser width that tabs scroll and the footer remains accessible.

Validation: application/test build and offline browser checks cover Electrical,
Bike, Clothing and Footwear registration, selected variants, cross-tab errors,
keyboard navigation, value retention and responsive widths. These use mock APIs;
live database acceptance remains manual. The broader `--registration-checks` command
currently stops at the pre-existing store-assignment assertion `cashier sees assigned
store without selector`, before its product checks run.

The three existing registration forms remain in place. New behavior is enabled by
manual migration **052**, after **051**. Electrical configurations, original product
and variant IDs, prices, serials, stock and saved transactions remain intact.

## Manual deployment

1. Finish pending registrations, stop the application and notification workers, and
   back up the database. Confirm migrations through 051 have been applied.
2. Run/export [profile_data_preservation_snapshot.sql](../database/queries/profile_data_preservation_snapshot.sql)
   as superuser. The updated query excludes newly introduced alias/status metadata
   when comparing original columns, while retaining original quantities and amounts.
3. Manually execute the complete [052_catalog_registration_improvements.sql](../database/migrations/052_catalog_registration_improvements.sql)
   as PostgreSQL superuser. Startup does not apply it. If an error occurs, issue
   `ROLLBACK`, resolve it and retry. Do not rerun a successfully installed migration.
4. Before restarting writers, run the preservation snapshot again. Every original
   row count and hash must match. The migration extends only unused Bike category
   templates and records that schema change in the audit log.
5. Restart and refresh with Ctrl+F5. The Product popup should generate an editable
   SKU and show Manufacturer part number plus Save & Add Another. Brand registration
   should show optional preferred categories and aliases. If these controls are
   absent, verify the application uses the database where 052 was installed.
6. Run [catalog_registration_verification.sql](../database/queries/catalog_registration_verification.sql)
   and the existing [profile visibility verification](../database/queries/profile_data_visibility_verification.sql).
   Preserve the outputs with the backup. Investigate old duplicates before onboarding;
   this release retains them rather than merging historical stock identities.

## What changed

Products generate an editable internal SKU, retain automatic internal barcodes and
offer explicit barcode generation/camera actions. Manufacturer part number, product
model, supplier codes, vehicle fitment and internal SKU are separate concepts. The
existing code field is correctly labeled Barcode value. HSN/GST, price, barcode and
SKU validation remain authoritative on the server/database.

Check matching products is an explicit button; typing does not open suggestions.
Saving rejects normalized exact product duplicates and duplicate manufacturer part
numbers within a brand/profile. Variant SKU/barcode checks remain independent.

Select a category by path or filter the category dropdown. Attribute controls load
from its schema. Multiple vehicle fitments belong to one product/SKU; helmet size or
color combinations remain separate stock variants. Default Bike templates add
vehicle compatibility, position, helmet type and certification fields only
to unused categories. Required fitment stays required where the previous model field was required. They contain no assumed fitment, certification or stock values.

Use a multiple-value (`TEXT_LIST`) field for compatibility. Enter one complete verified
fitment per entry, separated by semicolons, for example:

`Honda / Activa 6G / verified generation and years / Rear; Yamaha / FZ / verified generation and years / Rear`

Use Add a verified compatible vehicle to enter make, model, generation/year and position separately and append each fitment to the list. Keep these details together in each entry. These are
illustrative formats, not a claim that any particular part fits those vehicles.
There may be up to 30 entries, 100 characters each, and 1,000 characters total.
Configured searchable values are searched in Products, stock, variant selection and
global search. Legacy attributes retain their existing search behavior.

Category Fields & Variants adds Required, Searchable, Visible, Default value and
Creates variants policies. Hidden required fields need valid defaults; hidden fields
cannot generate variants. Visibility controls registration fields, while transaction
snapshots preserve saved item information. Used category definitions remain locked
to protect existing product identities; configure a new category to change its schema.
Moving a category/subtree containing products is blocked, including inactive products.
Duplicate siblings, circular hierarchies and unsafe deactivation checks remain.

Brands can be saved before categories exist. Preferred category links are optional,
searchable and many-to-many. Unassigned brands are available in registration; Show all
brands includes other active brands within the selected setup. Case-insensitive name
and alias collisions are rejected. Inline category creation retains its success state
and clearly states that a created category survives cancellation of the Brand form.

Supplier-specific item codes can be added to a single item on creation and to each
variant when editing. Choose a supplier by name. One supplier code maps to one variant;
the same variant may have codes from several suppliers. Supplier view permission is
required. Existing generic Supplier SKU metadata remains unchanged. For several newly
generated variants, assign their separate supplier codes after creating them.

Reorder settings can be saved for the current shop independently of the global
default. Receive Stock offers an explicit pack-to-stock-unit quantity conversion for
items with a pack quantity above one. Purchase cost remains the cost per stock unit;
check the converted quantity and receipt value before confirming.

Product status supports Active, Inactive and Discontinued. Inactive/discontinued
products retain their existing variants and history; reactivate before editing item
configurations. Status changes require product deactivation permission.

## CSV catalog onboarding

Open Products → Catalog import, export & product history.

1. Download the template. Use an exact category path or category code, and optionally
   a brand name/code/alias. Numeric `categoryId`/`brandId` columns are also accepted
   for existing integration files. Ambiguous names must be resolved explicitly.
2. Use one row per variant and a unique SKU. Rows with the same product name,
   category, brand and product model are grouped into one product. Use the exact
   configured attribute codes in the `attributes` JSON column, with string values.
   Example for a helmet: `{"helmet_size":"M","color":"Black"}`. Use the actual
   codes/options shown in Category Fields & Variants; custom schemas may differ.
3. Optional columns support cost, selling price, MRP, GST, HSN, description, pack
   quantity, default reorder level, warranty, barcode, additional barcodes (separated
   by `|`), manufacturer part number, supplier SKU and image URL. Advanced supplier
   mappings use `supplierIdentifiers` JSON; normally use the supplier-name editor.
   Uploaded image binaries and branch stock quantities are handled separately.
4. Preview validates the file without creating products or stock. Fix errors before
   confirming. Limit each file to 1,000 variant rows / 100 products, 30 variants per
   product, 2 MB. Category/brand lookup permissions are needed for name-based files.
5. Confirm explicitly. Each product and its variants save in one transaction; the
   entire file is **not** one transaction. An interruption may leave completed products.
   Keep the result list, reconcile existing SKUs, remove completed rows from the file,
   then preview the remaining rows before retrying. Existing records are never overwritten.
6. Export requires Products export and variant view permission. It exports the
   selected profile's catalog with the available metadata. Spreadsheet formula-like
   values are prefixed for safe CSV viewing; review such values before reimporting.

## Opening stock for two shops

Register products once for the company, then enter opening stock separately for each
shop. Never put Shop 1/Shop 2 quantities on a product registration record.

1. Select Shop 1's stock location and open Receive Stock → Opening stock.
2. In Add items, expand Import opening stock CSV and download its template. It
   contains the current location ID to prevent using a file for the wrong shop.
3. Fill SKU, locationId, quantity, purchasePrice, taxPercentage and serialNumbers.
   Quantity means individual **stock units**, not purchase packs. Separate serials
   with `|`; serial-tracked items need exactly one distinct serial per unit.
4. Validate & add to review. Unknown/inactive SKUs, duplicate rows/serials, a wrong
   location and invalid quantities/prices are rejected before the cart is changed.
   Use up to 200 items per receipt. An existing cart must be finished or cleared first.
5. Review quantities, cost per unit and date, then confirm. Only this confirmation
   posts stock through the existing ledger. Identical imported content has a stable
   company/profile/location request ID. Use the saved receipt for retries; importing
   the same balances again is not a stock correction. Record later corrections with
   stock counts/adjustments, and reconcile any uncertain result before posting again.
6. Select Shop 2 and repeat using its location ID and actual quantities. Transfers,
   sales and reports use these independent ledger balances.

## Acceptance checks on a restored demo copy

1. Create a brand without categories. Add an alias and attempt another brand using
   that alias with different capitalization: expect rejection. Filter its preferred
   categories; create one inline, cancel Brand, and confirm the category remains saved.
2. Create an unused Brake Parts category. Configure searchable vehicle compatibility
   as TEXT_LIST and a hidden required field with a valid default. Register one part
   with two verified fitment entries and an auto SKU; confirm the default is stored.
   Search a unique model from either entry in Products, stock and billing selection.
   A hidden/non-searchable unique value should not match an attribute search.
3. Register the same product again with different capitalization/spacing: expect
   rejection. Try the same SKU/barcode on another item, and a duplicate manufacturer
   part number for the same brand: expect rejection without stock changes.
4. Register a helmet with M/L variants using the category's axes. Each must retain
   its own SKU/barcode. Assign two suppliers with different item codes to a variant;
   check reload and supplier-code searches. A supplier code already mapped to another
   variant must fail without committing a partial product/variant update.
5. Verify Save & Add Another saves once and opens a fresh form with a different SKU.
   Verify Save & Add to Receipt remains a separate action in receiving registration.
6. Try moving a category containing a registered product or used descendant: expect
   rejection. Test duplicate sibling names, circular parents and in-use deactivation.
7. Preview catalog CSV: counts must stay unchanged. Confirm valid rows, then preview
   the same file again: existing rows must be reported rather than duplicated. Test
   bad JSON, duplicate SKUs, mixed units/tracking in one group, stale profile version
   and denied create/export permissions.
8. Receive opening balances M=5/L=3 at Shop 1 and M=2/L=4 at Shop 2. Sell one M and
   one L from Shop 1: expect Shop 1 M=4/L=2 and Shop 2 M=2/L=4. Check movement history,
   transfers, receipt details and sales reports. Repeat the original opening request:
   stock must not increase again. A wrong-shop CSV must fail before posting.
9. Set a shop-specific reorder level and verify the other shop's level stays intact.
   Test pack quantity 12 and 2 purchase packs: review must show 24 stock units; the
   unit cost must remain the entered per-stock-unit cost.
10. Mark a product Discontinued: it should disappear from active selling/receiving
    choices while its stock history remains. Reactivate and confirm the same product
    and variant IDs return. Switch back to Electrical and test existing serial,
    warranty, receipt and billing workflows without altering their saved history.

Run `tests/Run-CatalogRegistrationReviewChecks.ps1` for automated checks. It uses
server fakes and offline browser mocks; it never runs database scripts. PostgreSQL
trigger behavior, concurrency, real stock balances, scanners and printers remain
manual acceptance work after installation.

Branch-specific selling prices, merging duplicate historical products and a full lot
tracking workflow remain separate inventory phases. Existing quantity and Electrical
serial/warranty workflows continue to be supported.
