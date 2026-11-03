# Phase 8: Administration and pilot rollout

## Administration changes

User creation begins with an explicit role template choice and an assigned shop. No Admin role is preselected and no permissions are granted when the form opens. Selecting a template for a new user loads its saved module actions. Replacing customized unsaved selections asks before applying another template. Editing an existing user's role preserves their individual grants until Load role defaults is explicitly used.

Assigned store is the primary control. Additional stores, central warehouses and the default store are under Additional stores / warehouse access. Existing advanced assignments are preserved when saving permissions only. Changing the primary store selects that store as the retail assignment; review advanced access where needed. Staff stay pinned to the default store; additional access supports authorized administrator reviews. Owner accounts retain business-wide access. Store assignments require STORES / MANAGE_ACCESS. New User requires user CREATE, user MANAGE_PERMISSIONS and store MANAGE_ACCESS. Existing users can be inspected without these write permissions.

Both Users and Role templates use the same permission editor. Groups follow the shop workflows: Dashboard & scanning, Receive Stock, Sell / Billing, Inventory & transfers, Reports, Setup, Service & Warranty, and Administration & Security. Groups and individual action lists start collapsed. Search finds module names, descriptions or actions, and Assigned only narrows the list. Counts show assigned modules/actions. Highlighted actions change stock or security; only supported actions appear. View is added when an action is checked, and clearing View clears that module's actions. Filtering never removes saved selections from the request.

The latest flows reuse the existing action catalog:

| Workflow | Main actions |
| --- | --- |
| Guided receiving | STOCK_INFLOW: VIEW, RECEIVE; scanning additionally SCAN: VIEW, SCAN |
| Opening stock / corrections | STOCK_ADJUSTMENT: VIEW, ADJUST; stock checking additionally INVENTORY: VIEW |
| Billing | SALES: VIEW, CREATE; scanning SCAN: VIEW, SCAN |
| Customers | CUSTOMERS: VIEW for lookup; CREATE for quick creation |
| Invoice printing | INVOICES: VIEW, PRINT |
| Optional barcode labels | BARCODES: VIEW and relevant GENERATE, PRINT, REPRINT actions |
| Registration / variants | PRODUCTS and PRODUCT_VARIANTS: relevant VIEW, CREATE, EDIT actions; category/brand/supplier lookup access as needed |
| Transfers | STOCK_TRANSFER: VIEW, with CREATE, APPROVE, DISPATCH, RECEIVE, CANCEL selected separately; APPROVE also records damage/loss |
| Period reports | REPORTS: VIEW; INVENTORY: VIEW for stock and SALES: VIEW for financials |

There are no separate active legacy dispatch/receive/cancel modules. They are actions under Stock transfers. Inventory approval is not adjustment authorization. The Check workflow access panel identifies missing baseline actions without granting them. It is guidance, not a substitute for testing the complete task with that user's account. Existing role templates can be empty; use Role templates to select and save the intended actions before creating employees.

Actions the signed-in administrator does not hold are disabled, and the repository continues to reject unsupported actions, missing View and attempts to grant authority the administrator lacks. Template saves affect future template loads only; user permission saves invalidate the target's sessions. Deactivation blocks access immediately. Concurrent stale permission edits are rejected. User writes are blocked in the editor while a request is running. If an administration save returns an uncertain network result, inspect/reload the user or role before submitting again; administration edits are not automatically retried like stock postings.

## Manual deployment

No new database migration is required for Phase 8. Keep the baseline through 027, restart the rebuilt application and optionally execute [administration_rollout_checks.sql](../database/queries/administration_rollout_checks.sql) as database administrator. It is read-only, reviews the active catalog and account readiness, and never grants actions or changes users. Existing stock, billing and reporting read-only checks remain available.

The application database was not used for development/testing. Configuration files and the existing authentication/MFA policies were not changed.

## Validation completed in development

- Actual Razor screens with mocked APIs: explicit role choice, full current catalog, search/collapsed groups, View dependencies, separate adjustment/transfer actions, complete payloads despite filtering, assigned shop, preserved multi-store/warehouse access, passwords cleared after saves, role/user permission saves and mobile widths.
- Disposable PostgreSQL database with the complete baseline through 027: current module/action catalog matches the application, role-template/user creation works, default store login is pinned, template updates preserve existing grants, actual cookies are rejected after permission version changes, fresh sessions cannot bill after billing revocation, deactivation blocks existing cookies, and stale edits fail.
- Existing receiving, label, billing, transfer and reporting suites validate identifier reuse, exact interrupted-request retries, atomic stock postings, last-unit/serial concurrency, stock/store isolation and financial reconciliation.

Physical USB scanners, label printers, counter networking and actual shop accounts cannot be validated by mocked browser/device tests. Their sign-off is intentionally left for the one-store pilot below. Wider rollout should wait for that sign-off.

## One-store pilot checklist

To repeat the development checks, build `tests/StockFlow.MasterDataChecks` with `--artifacts-path tests/bin/store-assignment-build`, then run its built DLL with `--admin-action-checks`, `--multistore-database-test`, `--billing-database-test` and `--receiving-database-test`. Database checks need the disposable PostgreSQL cluster at port 55441 and create/drop uniquely named test databases. The administration flag also writes actual Razor fixtures for `tests/StockFlow.UiChecks/security-offline.cjs` and `rollout-admin-offline.cjs`; run those with the project's Playwright Node binary. Billing, receiving, labels, inventory and period report browser scripts cover the corresponding pilot software flows.

Record the results in [phase8-pilot-signoff.md](phase8-pilot-signoff.md). Use one selected shop and a small, agreed set of clothes and serialized batteries. Keep the other shops on their existing process until the pilot is accepted.

1. Record the pilot store, owner/operator, application version, scanner model/interface, printer model/driver, label dimensions, browser, workstation and local network URL. Apply any earlier pending migrations manually; back up and retain the previous application build before deployment.
2. Save the intended role templates, create real receiving and cashier users, assign the pilot store, and finish required password/authenticator setup. Confirm each user sees only permitted menus and cannot switch stores or post to another store. Check owner/admin views separately.
3. Receive a known clothing variant using its existing barcode. Register an unknown barcode without losing it. Receive distinct battery serials. Check quantities, purchase cost and stock increase exactly once. Post opening stock separately where needed.
4. With the actual scanner focused in each scan field, scan existing and generated labels, including leading-zero codes. Verify Enter termination, rapid repeated scans, size/color lookup and individual serial selection. Use a non-localhost LAN URL if that is the counter's normal setup.
5. With the actual printer, print optional A4 and/or thermal labels at the required size. Confirm no clipping, legibility and reliable physical scans. Reprint and confirm the identifier is unchanged. Print an invoice and verify customer, total, tax and payment details.
6. Bill registered-customer sales using Cash, UPI, Debit Card, Credit Card and Bank Transfer, with optional references and mandatory electronic receipt confirmation. Test discounts/approvals and reject new credit, split checkout, insufficient stock and duplicate serials. Separately verify historical walk-in/split/credit records and authorized later collections. Check stock and saved invoice history.
7. Using two authorized counter sessions, attempt to sell the same last quantity unit and the same serial. Exactly one should succeed. Verify the other session receives an actionable stock message and only one stock deduction/invoice exists.
8. Interrupt a receiving, sale, adjustment and transfer response using a controlled test network interruption, then retry the saved request in the same tab/session. Confirm one posting/invoice and the original identifier. Do not re-enter the quantity as a new request or clear browser storage before resolving the result.
9. Dispatch to a second approved test endpoint only if transfers are in the pilot scope. Confirm source deduction, transit quantities, a partial receipt, final receipt and any controlled damage/loss. These must not create purchase/sale records.
10. Revoke an action or deactivate a test cashier while signed in. The old session must stop working; after re-login a revoked action stays unavailable. Confirm another store's stock, invoices and reports remain inaccessible. Review the audit log.
11. Run Today/This Week/This Month/custom reports. Compare movement opening/closing, receipt/sale quantities, invoice totals, actual collections and end-date credit outstanding against the pilot records. Review any warning rows before sign-off.
12. Have the shop operator record acceptance or defects. Resolve failures and rerun affected steps. Roll out to the next store only after acceptance, retaining per-store device/account sign-off and a rollback plan.
