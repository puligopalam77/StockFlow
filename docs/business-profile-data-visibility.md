# Business-profile data visibility (051)

Selecting Bike Accessories now selects its business records as well as its catalog.
Electrical records remain stored and return when Electrical is selected. The selection
applies to the company and all branches; existing branch permissions still apply.

Purchases/orders, receipt history, sales/invoices, payments, returns, stock movements,
transfers, stock counts, held bills/approvals, cash-closing history, dashboard figures,
reports/exports, search and notification feeds follow the selected setup. Direct API,
invoice-download and attachment links also respect the selection. A fresh Bike setup
has empty history and zero transaction totals until Bike transactions are posted.

Company details, stores, users, permissions, billing policy and security/audit records
remain company administration. Contacts and brands used by both profiles remain
shared; their transaction balances are calculated from the selected profile's records.
Unrelated Electrical contacts and brands are hidden in Bike.

## Manual installation

1. Confirm migrations through **050** are installed. Stop the application and all
   notification workers, take a database backup and keep writers stopped through
   the before/after comparison.
2. Run/export [profile_data_preservation_snapshot.sql](../database/queries/profile_data_preservation_snapshot.sql)
   as PostgreSQL superuser. It works before and after 051 and records counts plus
   fingerprints of the original columns, excluding only newly added ownership metadata.
3. Manually execute the complete [051_profile_data_visibility.sql](../database/migrations/051_profile_data_visibility.sql)
   as PostgreSQL superuser. Application startup does not execute it. If an error
   occurs, issue `ROLLBACK`; resolve the cause before retrying. Do not rerun after
   successful installation.
4. Run the preservation snapshot again before restarting. Every count/hash must match.
   No quantities, amounts, serials, document snapshots or existing IDs should change.
5. Restart the updated application. The banner should say **Products, transactions
   and totals follow this setup**. If it says to apply 051, the connected database
   has not enabled the new filtering yet.

Existing document ownership is inferred from the actual item categories. A document
containing several profiles stops the migration for manual review; no partial invoice
or partial financial total is displayed. Existing empty/unlinked records and legacy
cash closings are assigned Electrical because they have no reliable item ownership.
Review these before rollout if Bike data was already entered under 050. Known Bike
documents, held-bill carts and discount requests are inferred from their products.
New records receive the active setup automatically. Historical ownership cannot be
changed through normal application writes. Old cached bills cannot be saved into a
different setup. Document numbers, scan codes and request UUIDs remain company-wide.
Cash-closing version numbers are separate for each profile/location/date.

## Verification in the application

Use a restored demo copy for new test transactions. Retain the original Electrical
purchase and invoice links and note stock quantities and balances before switching.

1. Select **Bike Accessories → Apply shop setup**. Confirm bike categories/fields
   and Bike guidance appear. Electrical warranty/service menus stay hidden.
2. Open Purchase Orders (the screen in the request), Received Stock History,
   Sales, Inventory, Transfers, Cash Closing and Reports. Electrical rows must be
   absent. Choose a date range covering the old Electrical transactions: those
   amounts must be excluded from totals and dashboard figures.
3. Open the saved Electrical purchase/invoice links, including API and attachment
   links. They must return no record/access rather than Electrical document contents.
   Search an old Electrical invoice, product/serial and unrelated customer/supplier;
   they must not appear in Bike results.
4. Add a Bike supplier/customer and a helmet with M/L variants. Receive M=5 and L=3;
   sell one of each and collect payment. Verify M=4, L=2, received=8, sold=2, remaining=6.
   Check the Bike invoice, receipt, customer/supplier balances, cash totals and matching
   daily/weekly/monthly/yearly reports. Test a return, payment and transfer as used by
   the shop. Reports must agree with the saved Bike documents.
5. Hold a Bike bill and leave another transaction tab open. Switch to Electrical.
   Confirm Bike held bills/approvals/history disappear; stale Bike postings must be
   rejected. All original Electrical purchases, invoices, stock and serial/warranty
   settings must return with their original amounts. Electrical totals exclude Bike.
6. Switch back to Bike. Confirm its new records, quantities and held bill return.
   Test both shops and a restricted staff login; profile filtering must not expand
   store, module or action access. A genuinely shared contact may appear in both,
   but its displayed financial records/balance must follow the selected profile.
7. Run [profile_data_visibility_verification.sql](../database/queries/profile_data_visibility_verification.sql)
   as superuser after editing its tenant/owner/location IDs. It compares expected
   IDs against actual runtime-role visibility for 22 core tables and asserts stale
   profile connections expose no products, purchases, sales or stock ledger rows.
   Repeat after selecting Electrical through the UI. Every `passed` value must be true.

## Development checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BikeDemoSetupChecks.ps1`.
This builds the application and runs server checks with fake repositories plus offline
browser regressions for Electrical and retail workflows, setup switching and Bike
registration. Added checks cover migration capability messaging, selected-profile
guidance and retention/restoration. These checks do **not** execute migration 051 or
prove PostgreSQL isolation; the manual SQL and transaction checks above are required.

If rollback is needed, restore the pre-051 database backup and matching application
version while writers remain stopped. Retain the failed environment for diagnosis;
do not remove policies or rewrite business profiles to make totals appear.
