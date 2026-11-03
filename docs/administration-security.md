# Administration and security

Run `database/migrations/017_administration_security.sql` manually after migrations 001–016, then restart the application. No database migration is applied automatically. Use the first two queries in `database/queries/administration_security_queries.sql` to detect duplicate usernames/emails before running the migration; resolve duplicates deliberately, then rerun the migration. It is transactional and safe to rerun without overwriting existing saved permissions.

Existing accounts retain their passwords and are not forced to change them. Existing sessions must sign in again because cookies now include a security version. The migration seeds role templates and per-user permissions for existing Admin, Sales and Service users. Unknown roles receive no permissions until an administrator assigns them.

## Screens

- `/Admin/Users`: search, create, edit, deactivate/reactivate, password reset and individual permission matrix. View, Create, Edit, Deactivate, Approve and Export are separate flags. Selecting another action enables View; clearing View clears the other actions. Use row All, Select all, Clear all or Load role defaults.
- `/Admin/Roles`: edit role permission templates. Changes affect future template selections, never existing users automatically.
- `/Admin/AuditLogs`: filter by actor ID, action and date, paginate and inspect before/after values.
- `/Admin/Security`: security policy overview.
- `/Account/ChangePassword`: change your own password; mandatory before using the application when required by an administrator.

Create/edit with a full permission matrix requires both the matching User Management action and Security Edit. User Management Edit alone permits password reset; Deactivate controls account status changes. Read-only managers can inspect accounts. Only an Admin can manage another Admin account. Non-admin permission managers cannot grant actions they do not themselves possess.

Passwords must have 8–128 characters, uppercase, lowercase, a digit and a special character. PasswordHasher stores only salted password hashes. Password confirmations are never persisted. Five failed attempts lock the account for 15 minutes; successful login or password reset clears failed attempts. Login failures use a generic message.

Account, role, permission and password changes invalidate affected sessions on the next request. Forced password changes block both pages and APIs. Accounts cannot deactivate themselves, change their own role or remove their own user/security management access. Administration transactions serialize the final-administrator check and use optimistic versions to reject stale user edits. No user or historical transaction deletion endpoint is provided.

The sidebar, Razor pages and APIs use saved per-user permissions. Role labels do not override module grants. For example a Sales user with Products View/Create may create products, while Edit and Deactivate can remain disabled. Page dependencies still need their own View permissions (for example Products/Customers/Inventory for a sale). Search results and dashboard summaries are filtered by module permissions. Scan access includes the product/serial lookup data described by that module.

Approve controls purchase completion, stock adjustments, warranty approval/replacement and service delivery. Existing invoice Print/Save PDF controls require Invoice Export; users who can view an invoice can still use their browser's native print/copy features. Export flags do not add new export endpoints to modules that have none. Deactivate has no destructive effect on sales, purchases, invoices or stock history.

## API

`GET/POST /api/admin/users`, `GET/PUT /api/admin/users/{id}`, `PUT .../{id}/status`, `POST .../{id}/reset-password`, `GET/PUT .../{id}/permissions`; `GET /api/admin/modules`, `GET /api/admin/roles`, `GET/PUT /api/admin/roles/{id}/permissions`; `GET /api/admin/auditlogs`.

Mutation requests require the usual `X-CSRF-TOKEN`. User updates, status, permission and reset requests include `expectedVersion` from the latest `securityVersion`. Submit every active module exactly once for matrix saves. Passwords belong only to Create or Reset; edit requests must omit password fields. Administration audit records include safe profile/permission snapshots and never passwords, hashes or cookies.

## Validation

Build and run `tests/StockFlow.MasterDataChecks` for API/page permission regressions, custom role grants, forced-password redirects, session revocation, password rules and existing module checks. These checks substitute in-memory repositories and do not write to PostgreSQL. The browser check `tests/StockFlow.UiChecks/security-offline.cjs` uses rendered Razor fixtures and mocked API responses. Live PostgreSQL migration/transaction checks remain manual, including duplicate handling, login lockout, last-administrator protection, concurrent stale edits and audit persistence.

## Shop details and bill settings

Run `database/migrations/038_company_profile_and_billing_settings.sql` manually as the database administrator after migration 037, then restart the application. If a prior script left the query session in an aborted transaction, run `ROLLBACK;` before starting migration 038. No migration is run automatically.

Setup → Shop details & bill settings now saves all business information, a PNG/JPG logo up to 2 MB, invoice and stock receipt prefixes, INR currency, default tax mode, credit payment terms, default salesperson, bill-footer text and terms and conditions. Company name, address, city, state, PIN code, phone and email are required when saving. Existing profiles are not filled with guessed contact information; enter these fields before saving the expanded settings.

Use `{YYYY}` in a prefix to insert the document year, for example `INV-{YYYY}-`. Literal prefixes such as `INV-2026-` remain exactly as entered. New numbers use the existing sequences; historical numbers are unchanged. INR is the supported currency; this setting does not convert currencies or rates.

Tax inclusive treats catalog selling prices and line discounts as including GST; tax exclusive adds GST to those values. New billing screens load this setting. Held bills retain their original tax mode and must be recreated if the shop tax mode changes before completion. Inclusive invoices show net rates with any cent rounding included in the net line adjustment so their lines reconcile with the recorded total. Purchase prices and receiving tax calculations retain their existing meanings.

Payment-term settings and legacy due-date data do not enable new credit or split checkout. Current New Sale requires a registered customer and full payment by one method. Authorized historical collections remain available, without inventing due dates for older invoices. The default salesperson is separate from the signed-in actor; only active tenant users may be selected.

Logo upload, replacement and removal are saved with the profile. Completed invoices snapshot company information, terms and salesperson name, so later edits do not change those details. Document branding uses the current saved logo, including on earlier invoices. Reset to default resets billing and printing defaults in the form, retains business details and logo, and requires Save to apply.

Verified with `--company-profile-checks`, field validation and billing checks, and `tests/StockFlow.UiChecks/company-profile-offline.cjs`. These use in-memory repositories or mocked APIs. Migration execution, database persistence, prefix generation and tenant constraints should be verified against a disposable database before deployment; the migration has not been applied to the application database by the coding agent.
### Business logos on documents

The logo saved in **Shop details & bill settings** supplies the branding for all invoices, including older sales, and for stock receipt, purchase, sale and service details. Printed reports and cash closing documents also use it. Invoice A4, 80 mm receipt and Save as PDF formats include the same image. The image is embedded in the document, so printing does not depend on a separate image URL.

Replacing or removing the saved logo updates document branding when the document is reopened. Earlier invoice company contact details, customer details, prices and totals remain unchanged. Migration 038 is required to save the company profile; this document branding change needs no additional migration.
