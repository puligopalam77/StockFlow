# Purchase barcode lots

The compact Print/Reprint tabs, selected missing-code preparation, dialogs and
persistent request history are documented in [barcode management](barcode-management.md).
Apply migration 061 after 060 for the current print-history workflow. The original
receipt-card behavior described below is retained as background.

Apply manual migration 060 after 059 with the application/workers stopped and a
backup taken, as PostgreSQL superuser. Restart afterward. No application database
migration is run automatically.

Purchases > Barcodes lists guided stock receipts, opening-stock imports and
purchase-order delivery receipts in the current shop, profile and stock location.
Guided receipts that also have purchase delivery records appear only once.
Search by lot number, purchase order or supplier, or filter the receipt type.

Open View barcodes to inspect the lot's supplied manufacturer identifiers. Choose
Prepare unique item labels when needed, select specific items or Select all, then
preview, print or reprint. Codes remain saved across retries and reprints. Both
manufacturer and generated codes remain selectable when available. The existing
A4 and thermal layouts are retained, with a maximum of 500 labels per print batch.
The lot page and sidebar remain under Purchases; Back to barcode lots returns to
the list. Purchase detail barcode links open the lots belonging to that purchase.

Purchase-receipt piece identifiers use SFR<receipt-item-id>X<piece-number>. Existing
SFI/SFL labels and serialized unit identifiers are preserved. Runtime receipt label
rows have tenant/location/profile ownership policies and SELECT/INSERT only.
Generation requires a completed delivery, a non-serialized variant and a piece
number within the received good quantity. The migration reserves the SFR namespace
and rejects conflicting pre-existing identifiers before creating the table.

New Sale resolves SFR labels through the owning completed receipt to the correct
variant, using the existing aggregate quantity sales flow. Generating labels never
posts stock, creates serials or changes the original purchase. Reversed receipts
cannot generate labels and their SFR codes do not resolve for sale scanning.

BARCODES VIEW, GENERATE, PRINT and REPRINT permissions govern the respective actions;
POST actions retain CSRF protection. The navigation move does not grant permissions.
Migration 060 is needed for the new menu entry and purchase-delivery generation;
guided receipt labels continue using the existing receiving endpoints.

Verification: build, --label-checks, barcode-lots-offline.cjs, labels-offline.cjs,
and the disposable PostgreSQL --stock-barcode-workflow-checks. The latter checks
migrations 054/059/060, lot deduplication, stable generated receipt codes, scanner
resolution and location isolation using production repositories. It refuses any
database other than 127.0.0.1:55439/barcode_workflow_test and never reads application
configuration.
