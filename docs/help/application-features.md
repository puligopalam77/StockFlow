# More StockFlow features

## Inventory and low stock

1. Open **Stock on hand** at `/Inventory` to check products and quantities at your accessible location.
2. Open **Low Stock** at `/Inventory/LowStock` to see goods at or below the minimum stock level.
3. Check **Stock receipt history** and **Stock leaving the shop** for stock movements. Sales are only one type of stock movement.

## Correct stock quantity and count stock

1. Open **Stock on hand** at `/Inventory`. Search by product, size, color, SKU, barcode or serial. Use **Low stock only** or **Include inactive products** when needed.
2. Choose **Show all stock changes** to inspect history before making a correction.
3. If your account has stock adjustment permission, use **Correct stock quantity**. Choose **Product**, **What changed?**, **Quantity** and **Reason**, then select **Save stock correction**.
4. For a shelf count, use **Count stock in the shop**. Choose **Product / size / model**, review **Quantity shown in StockFlow**, enter **Quantity you counted** and **Count notes**, then select **Save count & correct stock**.
5. A saved count adjusts recorded stock to your count and records the difference. For serialized goods, check the individual serials first. Use **Receive new stock** for supplier deliveries instead of a correction.
6. If a save is unconfirmed, check history and the screen's recovery action before submitting another correction.

## Reports

1. Expand **Reports** in the side menu and select **Shop summary**, **Sales & invoices**, **Stock on hand**, **Low stock**, **Product sales & profit** or **Daily cash closing**. Opening `/Reports` leads to **Shop summary**.
2. In **Shop summary**, choose the available location and date range, then select **Apply**. Review sales, collections, outstanding credit, invoice count, items sold, sales trend, top products, stock summary, recent activity and low stock.
3. In **Product sales & profit**, choose **Period** or **From** and **To**, then **Show report**. Review net sales, estimated cost, estimated profit, margin and the product comparison chart.
4. Expand **More reports** for **Stock receipt history**, **Stock leaving the shop**, **Expiring warranties**, **Warranty claims**, **Pending repairs** and **Completed repairs**, where permitted.
5. Stock on hand and low stock describe current balances. Sales use invoice dates; collections use payment dates; returns/refunds and cutoff balances have their own meanings. Product profit is a tax-exclusive receipt-price management estimate using evidence available before the original sale. Missing cost stays **Unknown** and partial totals are incomplete; this is not authoritative COGS, valuation or net profit.
6. Reports and locations depend on your account permissions. Quick Assist can explain these screens and answer supported live questions; it cannot calculate every report figure from arbitrary chat wording.

GST Settings at `/Admin/Gst` supports legal businesses, effective registrations,
shop mappings and reviewed tax rules. `/Reports/Gst` provides working registers,
notes and accountant CSV exports. These are not prepared/filed GSTR returns or
approved eligible ITC. E-Way Bills prepares source-linked worksheets and records
unverified external references; official generation and real provider credentials
are unavailable. Preparing/exporting never establishes a government acknowledgement.

Quotations at `/Quotations` have saved revisions, lifecycle, printing/manual PDF
sharing and conversion into New Sale. Only successful final checkout posts stock,
payment and the invoice. Operating Expenses provides shop-scoped drafts, posting,
partial/full payments, refunds/reversals, receipt evidence, correction history and
incurred-versus-paid reports. Cash Closing includes its signed cash payments.
Supplier payments and purchase charges remain separate. Expense entries create no
stock or eligible GST ITC. The new workflows require migrations 085/086 and the
corresponding permissions; production rollout and pilot verification are separate.

## Repairs

1. Open **New repair request** at `/Service/Create`.
2. Enter **Serial / barcode** and choose **Find item**, or use **Find a product without serial numbers**.
3. Select **Customer**, enter **What problem does the customer report?**, and optionally choose **Assign technician** and **Expected return date**.
4. Select **Save repair request**. Review requests in `/Service` or **Pending Repairs**.
5. Open the request details to review its status, parts, payments and delivery actions allowed for your account.

## Stock transfers

1. Open **Stock transfers** at `/Transfers` and expand **Create transfer**.
2. Choose **Send or request goods?**, **Sending location** and **Receiving location**.
3. Use **Find product / size / color / SKU**, choose the item and **Quantity**, and select **Add item**.
4. Review the transfer and use the actions allowed on its record. Sending removes stock from the sender; receiving adds stock to the receiver. Creating a transfer alone is not proof of receipt.
5. If an action has an unconfirmed result, use **Retry saved action** before creating another transfer.

## Cash closing

1. Open **Daily cash closing** at `/CashClosing` and select the date and permitted location.
2. Review **Recorded cash movements**. UPI, card, bank payments and unpaid credit are excluded.
3. Enter **Opening cash**, **Extra cash added**, **Extra cash removed** and **Counted cash**.
4. Check **Expected cash** and **Difference**. Enter **Reason / explanation** for a difference, extra movement or updated closing.
5. Select **Save cash closing**. Corrections are kept as versions; closing does not prevent later transactions.
6. **Difference** is counted cash minus expected cash. Do not include supplier payments or customer refunds again in extra cash movements when they are already recorded above.
7. Review **Saved closing history**. Use the permitted **View** or **Print** action for a saved version. **Print closing** opens print controls; refreshing totals does not save a closing.

## Suppliers and customers

1. Open **Suppliers** or **Customers** from **Products & contacts** to find the relevant contact.
2. Use the page's available add or edit actions; only authorized staff can save changes.
3. Enter ten digits for mobile numbers. Customer names, contact details and saved business data remain as entered when the interface language changes.
4. In billing, use **Add a new customer** and **Save and select customer** to select a new customer for the bill.

## Staff accounts

1. Open **Staff accounts & access** at `/Admin/Users`.
2. Use **Search** to find a staff account. Authorized administrators can use **New User**.
3. Review the account's role, individual permissions and assigned stores/locations before saving changes.
4. Deactivated accounts cannot sign in. Do not share passwords or grant another business's access.

## Roles and permissions

1. Open **Role templates** at `/Admin/Roles` and select **Role template**.
2. Authorized administrators can use **Add role**, review the screen/action permissions, and select **Save role template**.
3. Role template changes are defaults for new staff accounts; they do not automatically change access for existing staff.
4. Change an existing person's access through their staff account. **Reset unsaved changes** discards the unsaved permission edits.

## Stores and locations

1. Open **Stores and locations** at `/Admin/Stores`.
2. In **Create store**, enter **Store code** and **Store name**, with optional address and contact details.
3. Review location and staff access settings on the same screen before assigning a store to staff.
4. With migration 085, catalog/contact masters are private to their owning shop by default. Only an authorized tenant owner can explicitly share read access or resolve quarantined ownership, with an audit reason. Sharing does not permit editing the owning shop's prices, tax settings or contact configuration. Ambiguous multi-store legacy records require an explicit decision; invoices and historical IDs are retained. Stock and transactions remain location scoped. Active profile selection still uses the existing tenant-wide engine; independent simultaneous per-shop profile selection is not provided. Never change profiles to merge shop histories. General reports consolidate only authorized scope; complete registration GST reports require their dedicated permission.

## Shop details & bill settings

1. Open **Setup → Shop details & bill settings** at `/Settings/Company`.
2. Under **Business information**, enter company/legal name, address, city, state, PIN code, phone, alternate phone, email, website, GST and PAN details as applicable. Complete required fields.
3. Use **Change logo** to select a PNG or JPG up to 2 MB; 300 × 300 pixels is recommended. Review the preview. **Remove** clears the logo when the form is saved.
4. Under **Billing configuration**, review invoice and receipt prefixes, currency, default tax mode, default payment terms and default salesperson. Use **{YYYY}** in prefixes for the current year. Prefix changes apply to new numbers; existing invoice and receipt numbers remain unchanged.
5. Under **Invoice printing**, enter **Message printed at the bottom of bills** (up to 250 characters) and **Terms & conditions (optional)** (up to 500).
6. Select **Save company details** if authorized. **Reset to default** changes billing/printing defaults on the form; save to apply them.
7. New completed invoices retain their saved company details. Invoice and document templates that display business branding use the saved logo, including printed copies. Check the invoice preview and print output after changing the logo; this does not change the StockFlow application logo in the navigation.

Supported profiles are Electrical, Bike Accessories, Clothing and Footwear.
Profile capability never grants permissions. Serial/warranty/repair capabilities
are restricted to Electrical; unsupported controls remain unavailable. Receipt
batch/date fields and barcode label groups are not lot balances, FEFO or expiry
blocking. Current stock quantities are whole numbers.

## Language selection

1. Choose the required language in the header language dropdown.
2. English captions remain visible alongside available translations in parentheses.
3. Product names and customer data are not translated. Text without a translation stays in English.
4. Changing the business-wide language requires the configured settings permission.

## Password and account security

1. Use **Change password** in the profile menu for your signed-in account.
2. Enter the requested password fields and follow the displayed password policy.
3. **Forgot Password** requires account verification; a username alone does not authorize a reset.
4. Contact Super Admin for recovery or access problems. Never send passwords through StockBot Assist.
