# Unique labels for received items

For the current receiving/manufacturer barcode flow and existing stock support,
see [stock barcode workflow](stock-barcode-workflow.md), including manual migration
059. The original receipt-only behavior described below is retained for reference.

Stop the application/workers, back up the database, then manually apply
[054_unique_stock_item_labels.sql](../database/migrations/054_unique_stock_item_labels.sql)
as PostgreSQL superuser after 053. Restart the updated application. This script
has not been run against the application database.

For quantity stock, open **Labels** on a variant, or **Prepare unique item labels**
on the receipt's Barcode labels page. Each received piece gets a saved identity
such as `SFI48X1`, `SFI48X2`, `SFI48X3`. Generation is idempotent: retrying retains
those identities, including across browser reloads. Existing variant/model and
serialized-unit barcodes remain unchanged.

Use the item checkboxes for specific labels, **Select all** for all labels, and
**Clear selection** to start over. Print up to 500 per batch. **Reprint** reads the
saved identifiers without allocating new ones. The receipt number and item number
help identify which label to replace. The original copy count input is hidden
for quantity item labels.

Labels are tied to saved receipt lines in the current tenant, profile and stock
location. They do not create stock or serial numbers. Scanning a quantity item
label resolves its variant; quantity sales still use aggregate stock, rather than
recording which particular labeled piece was sold. A label is not proof that its
piece remains in stock. Transferred stock uses the receiving location's existing
stock workflow; these label identities remain linked to their original receipt.

The generated namespace `SFI<receipt-line-id>X<piece-number>` is reserved in the
scan identifier tables. Migration 054 refuses to install if existing identifiers
already use that namespace. Existing rows are not rewritten. Label rows have
tenant/location/profile RLS and no runtime update/delete grant. Generation needs
BARCODES:GENERATE, reading needs VIEW, printing needs PRINT, and reprinting needs
REPRINT. Variant print requests validate the selected label IDs against the
variant/location and audit their IDs.

Validation performed with fake repositories and mocked browser APIs: distinct
codes for three pieces, repeated generation/reload, specific/all selection,
unchanged reprint codes, permission/CSRF enforcement and A4/thermal layouts.
SQL, real scanner lookup and concurrent generation still require manual acceptance
after migration. Use the read-only
[verification queries](../database/queries/unique_stock_item_label_checks.sql).
