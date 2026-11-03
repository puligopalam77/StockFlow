# Purchase order details dashboard

Purchases > Purchase Orders > View displays a compact dashboard with purchase details, an amount summary, products, stock receipts and supplier payments/returns. No database migration is required.

- Purchase Details shows supplier, dates, code, GSTIN and terms. View more reveals the saved supplier snapshot, notes, audit information and invoice attachment.
- Amount Summary retains the purchase amounts and displays paid, outstanding and good received quantity. Outstanding comes from the existing API, including supplier returns.
- Products show name, SKU, ordered/good received quantity, unit cost and line total. View expands specifications, batch, MRP, GST, serials and pending receipt data. Lot barcodes remains within Purchases > Barcodes; expanding a product reads its saved variant barcodes without generating codes.
- Items displays receipts and settlement side by side on desktop. Section buttons focus Supplier & Charges, Stock Receipts or Payments & Returns. Narrow screens stack the cards and scroll tables horizontally.
- Pay Supplier, Return Stock and Receive Stock open dialogs using the existing forms and endpoints. Permission checks still control their availability. Errors appear inside the active dialog.
- Receive Stock is offered only for draft/partially received orders with remaining quantity. Good plus damaged cannot exceed that quantity. An uncertain saved receipt remains available as Retry pending receipt, preserving its request ID and payload even if the order has since completed. The repository's transaction and outstanding-quantity checks remain unchanged.
- Print includes the full product table and expandable details, including when a section or search is active. Printer selection remains in the browser dialog.

The dashboard uses Razor partials under `Pages/Purchases/Partials`, `purchase-details.css`, `purchase-details.js`, and the existing receipt/settlement scripts. It preserves purchase calculations, historical records, receiving, payment and return API integrations.

Validation: build the MasterDataChecks project, run `--purchase-receiving-checks` to verify permissions and render current Razor fixtures, then run `tests/StockFlow.UiChecks/purchase-details-offline.cjs` with the Playwright Node runtime. Browser checks cover expanded details, barcode scope, tabs, dialogs, duplicate submissions, remaining-quantity rejection, completed-order protection, interrupted receipt retries, printing and 1440/390/320 pixel layouts. Browser API calls are mocked; no application database is modified.
