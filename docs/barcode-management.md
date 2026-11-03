# Barcode management

The Purchases > Barcodes workflow now follows the supplied two-screen reference:
received lots, then a compact unit table with preview and reprint dialogs.

## Deployment

After 061, back up, stop the application/workers, and manually apply
[062_barcode_label_sizes.sql](../database/migrations/062_barcode_label_sizes.sql)
as PostgreSQL superuser, then restart. No application database SQL is executed by
the application or during these development checks. Existing codes and stock are
unchanged. Older receipt-wide audit records lack selected barcode details and are
not presented as per-label print history.

## Screens

The received-lot table retains search, type filters and purchase links, adding
Pending / Partial / Completed barcode availability and pagination. Opening either
screen is read-only. The selected receipt remains under Purchases, with its lot
number in the breadcrumb and receipt/purchase/date/supplier summary.

The detail screen has total/with-barcode/missing counts, Print Labels and Reprint
Labels tabs, per-unit selection, SKU, saved barcode, status, copies and row actions.
For larger receipts, use Group by variant with expandable unit groups or pagination.
Grouping never changes row identities. Select All Eligible includes up to 500 rows
matching the search and tab; copy totals must also remain at most 500.

Initial printing selects rows with no saved print request. Reprint selects rows
with history for the current saved code. Reprint Labels also offers a dialog with
search, the last request time/actor, selection and a copies-per-label control.
The history panel shows recent requests and View all.

The print settings default to 50 x 30 mm thermal labels. Small parts can use
40 x 25 mm. Very small packaging can use 30 x 20 mm only after the user confirms
a successful scan test with the shop's scanner; the server also enforces this
confirmation. The tiny layout keeps the product name, barcode image and number,
omitting SKU/GRN detail to preserve space. These dimensions describe the output,
not a guarantee of scan reliability on every scanner, stock or printer.

The barcode column includes a barcode image above each saved number, in the unit
table and reprint dialog. A barcode that cannot be rendered still shows its saved
number; opening the list never changes the code.

A4 is the backup option. Choose the measured 21-label 63 x 38 mm template (3
columns x 7 rows, left 7.5 mm, top 15.5 mm, column gap 3 mm, row gap 0 mm), or enter
the actual label dimensions, columns, rows, top/left margins and gaps for a custom
sheet. The server rejects geometry extending beyond 210 x 297 mm. These dimensions
must match the physical sticker stock; not every 21-label sheet uses this layout.
Margins and gaps apply to every page. Print history retains the template and
scan-test confirmation alongside the selected format. Legacy sheet/thermal history
retains its original 63 x 38 mm interpretation.

Preview and print use the same selected dimensions. Preview is a dialog showing
the product/variant, scannable Code 128 barcode value, SKU and lot number. Printer
selection happens in the browser's print dialog; a normal web page cannot list or
confirm the user's installed printer hardware. Print at 100% scale with browser
headers/footers off. For thermal printing, choose the matching paper size in the
printer driver and use no margins.

## Identity and requests

Preview checks the selected unit keys on the server, retains saved codes, and
generates only selected missing non-serialized codes in a committed transaction.
Manufacturer model codes are the default when no individual generated code exists;
they can legitimately repeat across received pieces. Existing individual SFI/SFR
codes are retained. Serialized unit codes and serials keep their existing workflow.
Missing or unavailable serialized units are not silently converted into quantity
labels. Barcode allocation remains server-side with uniqueness constraints.

Print submits the saved row keys, exact barcode values, copies, paper format,
action and a UUID retry identifier. The server revalidates ownership and the saved
codes before inserting immutable history. Retrying the same request retains one
history record. Reusing its ID for a different selection is rejected. Failed
generation, validation, permission or server responses prevent browser printing.
The open preview retains its request ID for retry if the response is interrupted.

Reprint never generates codes, requires a previous saved request for each selected
code, and records a new reprint request. Changing copy counts never changes stock
or barcode identity. Requests are labeled Print requested, not physically printed:
opening or canceling a browser print dialog cannot confirm printer output. A user
can retry from the same preview or explicitly reprint the saved code.

VIEW, GENERATE, PRINT and REPRINT permissions remain distinct; GENERATE is needed
only when the selected preview contains missing codes. POST endpoints use existing
CSRF protection. History has tenant, current location and receipt/profile ownership
policies, with SELECT/INSERT only for the runtime role.

## Verification

- Build, `--barcode-management-checks` and the retained `--label-checks` verify HTTP
  permissions, selection/copy validation, retry behavior and CSRF.
- `barcode-management-offline.cjs` renders real Razor/CSS/JavaScript with mock APIs.
  It verifies lot status/search, individual/grouped rows, summary, preview dialogs,
  supplied codes, selective generation, failure blocking, retry IDs, history/reload,
  reprinting, copy controls, inline barcode images, all thermal PDF dimensions,
  scan-test gating, custom A4 validation, 21-label pagination and mobile layout.
- `--stock-barcode-workflow-checks` uses production repositories against the guarded
  disposable database 127.0.0.1:55439/barcode_workflow_test. It tests migrations
  054/059/060/061/062, partial generation, stable codes, manufacturer reuse, saved actor,
  copies/format, request deduplication, scanner resolution, tenant/location isolation
  and unchanged inventory quantities. It never reads application configuration.

The older `barcode-lots-offline.cjs` and `labels-offline.cjs` entry points now run
the consolidated compact-screen check; their previous card UI was replaced.
