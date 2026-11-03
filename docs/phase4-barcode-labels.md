# Phase 4: barcode and label workflow

The original phase described below used repeated model labels for quantity stock.
The current quantity-label workflow requires manual migration 054 and uses saved
identities for each received piece. See [unique stock-item labels](unique-stock-item-labels.md)
for preparation, specific/all reprinting, deployment and current verification.

Receiving registration offers **Use existing barcode** or **Generate internal
barcode** for the product/model. Unknown scanned values stay in the registration
field, including leading zeroes. Choosing an internal identifier keeps the
existing barcode as a usable alias. Serial numbers identify individual units;
internal labels for those units are prepared after the receipt is saved.

Product lookup groups variants and offers Size/Color filters when those attributes
exist. Catalog registration and Configuration pages retain their existing behavior.
Configuration files have not been changed for this phase.

After saving goods or opening stock, **Barcode labels (optional)** opens
`/Labels?intakeId=<saved number>`. This is a separate step: skipping printing or
failing to prepare a label does not undo or resubmit stock. Receive Stock also
contains a Barcode labels entry; load a saved intake number to reprint later.
This screen covers guided receipts created in Phase 3. Existing catalog printing
remains available for other products and older receipts.

Select only items that need labels. Quantity stock uses one code per variant
(size/color), with adjustable copies. Serialized stock offers each received unit
separately, with its serial/manufacturer code or its own internal code. Shared
battery model barcodes are never used as individual unit labels. A unique existing
unit barcode can also be scanned and saved here; existing manufacturer identities
cannot be replaced. All serial, manufacturer and internal aliases remain scannable.

Generating calls the existing generators: `SFV` for variants and `SFU` for units.
Repeated generation reuses the stored internal identifier. Preview does not write
stock or identifiers. Print/Reprint re-read identifiers and never generate new ones.
Their audit entries record requests to prepare printing; browser cancellation or
printer completion cannot be determined by the server.

The locally rendered Code 128 labels preserve leading zeroes and case. Select
**A4 sheet** (three 63 × 38 mm labels per row, 9 mm page margins, 1.5 mm column gaps)
or **Thermal** (one 63 × 38 mm label per page). Print at 100%, disable browser
headers/footers, and select matching paper in the printer driver. Labels include
the item, size/color or serial, identifier, and current selling price before tax.
Long/non-ASCII existing codes remain valid in storage but cannot be represented
reliably on this Code 128 layout; keep their original label or choose an internal
code. Print up to 500 labels per batch. Other label sizes require a future layout.

Access uses existing BARCODES actions: VIEW for this page/manifest, GENERATE for
identifier creation or attaching a manufacturer unit code, PRINT for printing,
and REPRINT for reprinting. The menu/buttons follow these permissions, and APIs
enforce them with CSRF protection. Manifests require the current location and
cannot read another store's intake. Staff continue using their pinned store.

## Database and deployment

**No new migration is required for Phase 4.** Existing migrations through
`026_guided_stock_receiving.sql` must already be applied. No application database
changes were executed automatically. Optional read-only checks are in
`database/queries/barcode_label_checks.sql`; these checks do not generate labels
or change stock. Deploy/restart the updated application after the Phase 3 baseline.

## Verification

Build: `dotnet build tests/StockFlow.MasterDataChecks --no-restore --artifacts-path tests/bin/store-assignment-build`.
Run the built check DLL with `--label-checks` and `--receiving-checks` to verify
permissions, page rendering and produce Razor fixtures. Run
`tests/StockFlow.UiChecks/labels-offline.cjs` and `receiving-offline.cjs` using the
Playwright bundled Node executable. Browser checks cover registration choices,
size/color selection, leading zeroes, optional labels, unit generation, reload,
stable reprinting, print denial, responsive screens and A4/thermal PDF pagination.

`--receiving-database-test` uses only a new disposable database on test PostgreSQL
port 55441. It checks actual stored generation, existing/generated lookup,
individual unit manifests, retries, opening stock, immutable unit identity,
print/reprint stock invariance and store isolation. It also executes both read-only
reconciliation scripts. The application database is not used by this test.

Check one printed clothes label and one battery unit label with your physical
scanner before shop rollout; automated database lookup and PDF layout checks do
not verify your printer's calibration or scanner hardware.
