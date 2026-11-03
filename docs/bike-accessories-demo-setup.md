# Bike Accessories demo setup

For selected-profile purchases, invoices, payments and totals, also install **051**.
See [business-profile data visibility](business-profile-data-visibility.md) for the
current installation and verification steps. The 050-only behavior below is the
catalog setup baseline.

The owner can select Bike Accessories in Shop Business Profile and click **Apply
shop setup**. Products opens automatically with bike categories and registration
fields. Electrical remains a supported setup. Clothing and Footwear are left for
later setup work; the updated selector offers Electrical and Bike Accessories.

This changes the active catalog, registration experience and available menus for
the company and all its branches. Existing products are not relabeled or moved.
Electrical catalog records, variant IDs, serials, warranty settings, stock ledger,
invoices and receipts remain stored as entered. Switching back to Electrical
restores that catalog. No sample products, sales or opening stock are created.

## Before the demo: manual deployment

1. Finish/reconcile pending receipts and bills, stop application writers and back
   up the database. Confirm migrations through **049** are applied.
2. Run/export [bike_demo_setup_verification.sql](../database/queries/bike_demo_setup_verification.sql)
   as administrator to record original product/variant/ledger fingerprints and
   financial totals. Its queries work before 050 when 049 is present.
3. Manually execute the whole
   [050_bike_catalog_setup_switch.sql](../database/migrations/050_bike_catalog_setup_switch.sql)
   as PostgreSQL superuser. If it fails, issue ROLLBACK in that session and resolve
   the error before retrying. It adds setup support; it does not select Bike for
   any company or modify existing stock/documents.
4. Restart the updated application. Open Administration → Shop Business Profile,
   choose **Bike Accessories**, and click **Apply shop setup**. Extra reason and
   acknowledgement inputs are replaced by this explicit setup action; the server
   still checks administrator/owner rights, edit permission, CSRF and version, and
   records an audit reason automatically.
5. Products opens. Run the verification queries again before entering demo stock.
   Electrical product/variant/ledger fingerprints and all document totals must
   match. New bike categories, a default schema and setup audit entries are expected.

The developer did not execute database scripts. Without 050, the old protection
continues to block switching a populated company. Do not bypass it by manually
changing tenant profile codes or product categories.

## What is prepared automatically

| Category | Registration fields |
| --- | --- |
| Helmets / Gloves | Size and color as variant options; material optional |
| Head Lights | Compatible bike models, bike make, year range, voltage, power, connector |
| Side Guards | Compatible models, left/right/pair/universal fitting, make, finish |
| Mirrors | Compatible models and fitting / side |
| Bike Spare Parts, Brake Parts, Chains & Sprockets, Bike Covers, Bike Luggage | Make, compatible models, year range, material/finish |

Use a unique part number / SKU for each physically different item. Manufacturer
brand remains the existing Brand field; bike make and compatible models are
separate fields. Compatibility is entered text, not a certified fitment database.

Repeated setup does not duplicate categories. Existing customized bike schemas
are preserved. If bike products already exist, setup does not add new required
fields to those categories; their established configuration stays intact. Default
fields are installed when the bike catalog has not yet been adopted.

## Short demo verification

1. Open Add Product. Select Helmets: size/color should appear; tracking and Electrical
   warranty inputs should be hidden. Enter a product name, part-number/SKU prefix,
   prices, and manufacturer brand if configured.
2. Enable Has Variants, choose M/L and Black, preview two combinations and save.
   Each variant gets its own SKU and barcode. Registration alone adds no stock.
3. Select Head Lights in another registration. Check model compatibility, voltage,
   power and connector fields. Select Side Guards and check fitting options.
4. On a demo database copy, receive 5 M helmets and 3 L helmets. Sell one of each:
   expect M=4, L=2 and 6 units remaining. Confirm payment and compare the invoice
   and stock history. Product and serial stock from Electrical must not appear in
   the active bike catalog or current-stock list.
5. Check search/billing/transfers captions and the absence of Electrical warranty
   and service menus. With 050 alone, old invoices, receipts and financial history remain visible
   with their original names and amounts; the page explains that financial history
   includes previously entered records. These figures are not reset for the demo.
6. Return to Shop Business Profile, choose Electrical and apply. Confirm the original
   catalog and warranty/serial fields return. Bike records remain stored separately.

Browser draft keys now include the setup after 050 so new bike drafts do not load
new Electrical drafts. Older browser drafts are not deleted; finish/reconcile them
before deployment. Stale browser requests for an item in another setup are rejected
by catalog checks and database posting guards. After 051, saved financial/history reads also follow the selected setup.

This is a retained-catalog setup within one company, not isolation between separate
customers. Use a separate customer/company account when onboarding an independent
live bike business. Use a database copy for fictitious demo transactions.

## Automated checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BikeDemoSetupChecks.ps1
```

The runner includes existing retail/Electrical regressions plus a populated-company
switch test and a browser demo of select/apply → ready registration → Electrical
restoration. It validates the shipped SQL's field templates against application
rules, but does not execute SQL. Results are in `tests/bin/bike-demo-results` and
`tests/bin/retail-phase56-results`. PostgreSQL trigger, profile filtering, schema
seeding, concurrency and real-data preservation checks remain manual until 050 and
the before/after checks are performed.
