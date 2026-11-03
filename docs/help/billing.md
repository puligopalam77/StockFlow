# Creating bills and taking payment

New Sale requires a registered customer and full payment through one method: **Cash**, **UPI**, **Debit Card**, **Credit Card** or **Bank Transfer**. Final checkout posts the invoice, payment and stock together. Drafts and quotations do not post stock or money.

## Create a bill

1. Open **Sell / Billing → New sale** at `/Sales/Create`.
2. Use **Scan or Search Product**. Scan a barcode, type a code and press Enter, or search by product name, SKU or model.
3. Select the exact variant from the results. Authorized **Quick Add Item** bills a non-stock manual line without registering a product or creating inventory.
4. For serial-tracked goods, scan the serial of the physical item being sold. If the unit picker appears, select the correct **Item in stock** and use **Add item to bill** when available.
5. Check **Items in This Bill**: quantity, MRP, selling price, discount and applicable tax. Pricing can include or exclude tax; the server calculates totals. Row tax is read-only. An authorized ordinary-bill correction uses its separate action and reason; it cannot replace reviewed GST mappings.
6. Search for and select a registered customer. Older walk-in invoices remain readable, but new checkout requires a customer.
7. Check **Total Payable** and choose one payment method. **Calculate Change (Optional)** requires cash received of at least the total when enabled; a short amount disables checkout. Otherwise completing a cash sale confirms exact receipt.
8. Select **Complete Sale & Generate Bill**, then check the saved result before starting another sale. Invoice access depends on permissions.

Scanning the same quantity-tracked product again adds another item. A serial identifies one physical unit; check the unit before completing the bill.

For electronic methods, check actual receipt in the bank/payment device and explicitly confirm it when prompted. A transaction reference is optional. This records the cashier's observation; StockFlow does not independently verify provider settlement. Cancel returns to the bill without finalizing it.

## Add a customer

Use **Add Customer** when permitted. Enter the required name and ten-digit mobile, review duplicate warnings and save/select the customer. General customer management has its separate optional-mobile rules.

New credit/pay-later and split checkout are unavailable. Credit Card is a collected card payment, not customer credit. Historical split allocations and authorized later collections on older credit invoices remain supported through the original invoice. A later collection is not another sale.

Price reductions may require **Reason for discount / price change** and **Request discount approval**, depending on the business policy.

## Pause a bill

Select **Hold Bill**, then use **Saved Bills** to resume accessible held bills or recover available unfinished work in the same scope. Editor leases and revisions prevent concurrent overwrites; do not assume a universal seven-day availability promise. Holding does not reserve stock. Resuming rechecks current stock, prices, tax and payment.

## Open and print an invoice

After completion, use **Open invoice** if available. To find an earlier sale, open `/Sales`, search by invoice, customer name or mobile, and select the invoice number to open the invoice. From there, select **Sale details** to review the sale. In Sale details, **View / print invoice** opens the invoice when your account permits it.

On the invoice, choose **Print format**: **A4 invoice** or **80 mm receipt**. Select **Print invoice**. For an 80 mm receipt, use matching paper and 100% scale in the printer settings. Printing opens the browser's print controls; it does not set up the physical printer.

For an unconfirmed result, follow saved-request verification/recovery with the same request identity. Do not create another bill until the original outcome is known. Draft recovery requires the server for finalization and is not offline completed billing.

## Check today's completed sales

1. Open **Sales & invoices** under **Sell / Billing** or **Reports**, at `/Sales`.
2. Set **From** and **To** to today's date, leave other filters empty for the whole accessible scope, and select **Search**.
3. Choose **Completed** in **Status** and select **Search**. Review the summary cards and use the paging controls below the list; a single page is not the whole day's count.
4. Select an invoice to review its details. Ask StockBot **How many sales completed today?** for the completed bill count, or **How much sales today?** for the completed sales amount.

## Search and export sales invoices

1. Open **Sales & invoices** at `/Sales`. Search by invoice number, customer name or mobile; choose **From**, **To** and **Status**, then **Search**. **Reset** clears the filters.
2. The cards show **Total Sales**, **Total Invoices**, **Average Invoice Value** and **Returned Amount** for matching records. Sales totals and average use completed invoices; returned amount includes returns against matching invoices.
3. Click a sortable column heading to change order. Use **View**, **Print** or the record's available actions according to your permissions.
4. Select invoice checkboxes to export selected records, or use **Export** without a selection for matching records across pages. Review the downloaded CSV. On Android, choose a destination in **Save file**.
5. Exporting and printing do not complete a new sale or take payment.
