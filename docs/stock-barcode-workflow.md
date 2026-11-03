# Stock barcodes, printing and sale scanning

Back up, stop application/workers, and manually apply
[059_stock_barcode_workflow.sql](../database/migrations/059_stock_barcode_workflow.sql)
as PostgreSQL superuser after 058. Restart the updated application. This migration
has not been run against the application database.

## Receiving and supplied barcodes

Scan an already registered manufacturer barcode in Receive Stock to select its
product and size / model. Unknown codes can be retained during inline product or
variant registration. Codes are text: leading zeroes are preserved. Saving stock
does not replace supplied codes with newly generated model barcodes.

Open Barcode labels after saving a receipt. Supplied manufacturer codes appear
immediately and may be printed without generating replacements. Manufacturer
codes commonly identify a model and can legitimately repeat across pieces.

## Missing codes and unique labels

In Sizes, models & barcode labels, open Labels and click **Generate unique barcodes
for stock**. Opening this dialog only reads saved codes. Where a manufacturer code
is available, choose either the existing code or Unique item barcodes.

Received pieces retain the existing `SFI<receipt-line-id>X<piece-number>` format.
Existing stock without guided receipts receives `SFL<label-id>` codes. Generation
allocates persistent slots for current stock not covered by receipt quantities,
using a tenant/location lock and unique indexes to make retries and concurrent
requests safe. Older saved labels remain available for reprinting even after stock
falls. No fake receipt or inventory movement is created. At most 10,000 uncovered
pieces per variant can be prepared; use receipt records for larger quantities.

On a receipt, **Prepare unique item labels** explicitly generates unique codes.
Once prepared, those unique labels are displayed instead of repeated model labels.

Select specific labels or Select all. Print at most 500 labels per batch. Reprint
reads the same saved identifiers without generation. For manufacturer codes, the
copy count prints copies of that existing model code; it does not create unique
identities. Serialized stock retains its separate individual-unit label workflow.

## New Sale

Scan a manufacturer barcode or a generated label in New Sale. Both resolve the
correct product, variant, price, tax and available stock. Quantity labels resolve
the variant; sales continue to decrement aggregate stock. They do not track which
particular labeled piece was sold, and rescanning adds another quantity subject
to the normal stock checks. Unique label identities are not serial tracking.

Labels remain tied to the original shop, profile and stock location. Existing
transfer and serialized-unit workflows are unchanged. Generated namespaces are
reserved against manufacturer/serial imports. Migration 059 stops if existing
identifiers conflict with the new SFL namespace.

Generation requires BARCODES:GENERATE, viewing VIEW, printing PRINT and reprinting
REPRINT. Print requests validate label ownership or the saved manufacturer code
and audit the selection. No runtime label update/delete grant is introduced.

## Verification

Build and run `--label-checks` for HTTP permission, validation and CSRF checks.
Browser checks `variants-offline.cjs --barcode-workflow`,
`variants-offline.cjs --labels-only`, `labels-offline.cjs` and `receiving-offline.cjs`
exercise supplied codes, explicit generation, repeated clicks, specific/all
reprints, receipt retry and A4/thermal printing with mock APIs.
`variants-offline.cjs --barcode-sale-scan` checks both code types and repeated scans
in the real New Sale UI with mock lookup responses.

`--stock-barcode-workflow-checks` uses production repositories and migrations 054
and 059 against a disposable fixture database. It requires the explicit environment
variable STOCKFLOW_BARCODE_TEST_CONNECTION and refuses any destination other than
127.0.0.1:55439/barcode_workflow_test. It checks old-code preservation, uncovered
stock, concurrent generation, scanner lookup, manufacturer receipts, print
ownership, tenant/location isolation and unchanged stock. It must never be pointed
at an application database.
