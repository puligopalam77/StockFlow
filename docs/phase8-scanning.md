# Phase 8 scanning

Open `/Scan` from the sidebar. Admin, Sales and Service can use a USB keyboard-mode scanner with an Enter suffix, or type a code and press Enter / Look up. Leading zeros are preserved. Lookups only read inventory; they never receive or sell stock.

API: `GET /api/scan/{code}` or `GET /api/scan?code=...`. The query route is used by the UI to support slashes and other reserved characters. Responses distinguish product barcode, serial number and unit barcode; include stock, status and availability. Inactive physical units remain searchable for service, but are unavailable. Ambiguous codes produce a validation error. Missing codes return 404, invalid codes 400, unauthenticated requests 401.

Execute `database/migrations/010_phase8_scanning.sql` manually in StockFlow_DB. It adds normalized unique indexes, preserving existing identifiers and stock. If duplicates are reported, roll back the failed transaction, use `database/queries/phase8_scanning_queries.sql` to review them, resolve them deliberately and rerun. No migration was executed automatically. Lookup computes stock from the transaction ledger and does not require the optional Phase 7 view.

Purchases use the shared scanner handler, scan counts and removable serial entries. Duplicate scans are rejected across all lines. Completion requires the exact serial count; drafts intentionally allow incomplete counts. Server validation and database uniqueness remain authoritative. Existing physical units cannot be added through the product scanner as new stock.

The [Phase 9 sales cart](phase9-sales.md) now supports product/serial/unit scanning, repeated quantity scans, duplicate serial rejection, and manual available-unit selection. Scan responses also include product tax percentage. Availability returned by scanning is informational: sale completion revalidates and locks stock transactionally. Basic warranty registration and display are part of completed serialized sales; full warranty/service workflows remain later work. Existing product barcode API remains available. Inventory browsing and stock adjustments are available in the [Phase 7 application module](phase7-inventory.md).

Checks: `dotnet run --project tests/StockFlow.MasterDataChecks` verifies authentication, role access, normalization, availability and invalid codes using fake repositories. Add `-- --database-readonly` to exercise the real lookup SQL without writes. A physical USB scanner still needs an on-device acceptance check.
