# Simple stock receipts and optional purchase orders

Use **Receive Stock → New stock receipt** for goods physically arriving at the shop. Choose the supplier, enter an optional supplier invoice number, scan/find items, enter quantities and costs, record individual serials where required, and select **Save stock receipt**. The date defaults to today. Tax and cost defaults come from the registered item.

The normal form keeps item selection, quantity and cost visible. Notes, payment terms, discounts, alternate received date and bulk variant quantities are under **More options**. Each line has an optional tax/discount section. Scan buttons follow scanner access; staff stock is assigned to their signed-in location.

Use **More options → Order goods for later / partial deliveries** only when recording goods that have not all arrived. **Save purchase order** saves a draft and adds no stock. Record subsequent physical deliveries from the saved order details. **Save as draft** also permits saving unfinished receiving work. Users without purchase approval can save orders but cannot post stock through this screen.

## Product registration while receiving

- A known model barcode identifies an existing product/variant and fills its details and defaults. Repeated model scans increase that line's quantity.
- An unknown code opens **Register product & add to receipt**, retaining the original code and leading zeros. Staff enter the missing name, category, tracking and prices. A barcode alone cannot supply an unknown product's description or price; there is no external manufacturer catalog lookup.
- The same registration dialog is available manually through **Register new product**. Model/brand, tax, warranty, an editable generated SKU and a specification are optional expanded details. **Has variants** reveals the name of the first configuration being registered. Further configurations remain maintainable in Product Registration.
- Select whether a scanned code is a shared model barcode or an individual unit serial. An individual serial requires serial tracking and is added to the receipt's serial list; it is not stored as a shared model barcode. For a new serial belonging to an already registered model, choose that existing model and enter the serial on its receipt line.
- Registration uses the same product/configuration APIs as Configuration. It creates catalog details, not inventory quantity. If registration succeeds but loading the item fails, **Retry adding saved item** loads it without registering again.
- Inline registration needs PRODUCTS CREATE, PRODUCT_VARIANTS VIEW/CREATE and CATEGORIES VIEW. Optional brand selection needs BRANDS VIEW. Existing permissions are preserved.

## Interrupted saves

The screen saves a draft first, obtains its ID, then completes that saved purchase to post stock. An interrupted completion can be retried against that ID. The existing completion endpoint and row lock prevent duplicate posting. Recovery is stored separately for each tenant, user and location in browser session storage.

If the initial draft response is lost before its ID is known, this screen has not invoked stock completion. It asks the user to check purchase history rather than submit a second order automatically. If the draft was saved, open that record. If no record was saved, use the recovery panel's start-new-order action after checking history. If saved draft editing fails, retain the original draft and review it before further posting.

No new migration is required; existing migrations through 027 support the flow. No SQL was executed on the running application database. Existing optional electronics checks remain read-only.

Validation covers the compact layout, model/serial distinction, preserved unknown barcodes, registration and catalog-loading failures, serial counts, permission restrictions, order saving without stock, and interrupted completion recovery. Disposable PostgreSQL/API tests confirm that repeated completion creates exactly one movement and physical unit. Browser checks cover desktop and narrow mobile layouts.
