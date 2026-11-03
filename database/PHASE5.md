# Phase 5 master data

Run `migrations/007_phase5_master_data.sql` manually in **StockFlow_DB**, after the Phase 2 tables exist. The migration adds case-insensitive category/brand uniqueness, SKU/barcode uniqueness, required-name checks, and product value checks. It is transactional and can be rerun. It deliberately fails on existing duplicates or invalid records instead of silently changing business data. No new seed data is required.

The application checks duplicates before saving; the database indexes are still required to prevent duplicates from concurrent requests. Existing inactive records retain their names, SKUs, and barcodes. Edit and reactivate the original record instead of duplicating it.

Start StockFlow and use **Master data** in the navigation. All five modules support listing, searching, creation, editing, and deactivation. Products include category/brand dropdowns, prices, tax, warranty, minimum stock, and serial tracking. Select an active category/brand when saving a product. Blank optional fields are stored as NULL.

| Module | Admin | Sales | Service |
| --- | --- | --- | --- |
| Categories, brands, products | View/create/edit/deactivate | View | View |
| Suppliers | View/create/edit/deactivate | No access | No access |
| Customers | View/create/edit/deactivate | View/create/edit | View |

Deactivation preserves history. Only Admin can change customer active status. Product creation does not create physical units or stock movements. Admin and Sales can now open customer sales history from the customer list; see [Phase 9 sales](../docs/phase9-sales.md).

API routes use `/api/categories`, `/api/brands`, `/api/suppliers`, `/api/customers`, and `/api/products`, with GET/POST on the collection and GET/PUT/DELETE on `/{id}`. PUT accepts the editable fields plus `isActive`; the route supplies the ID. Responses are DTOs rather than domain entities. Lists accept `activeOnly=true` and `term=...`. `/search?term=...` returns up to 20 active matches by default; product barcode lookup is `/api/products/barcode/{barcode}` and returns active products only. Collection listings include inactive records by default for maintenance.

API writes require the login cookie and the `X-CSRF-TOKEN` anti-forgery header, supplied automatically by the management pages and development Swagger UI. Validation returns 400; concurrent uniqueness conflicts return 409; missing records return 404; missing/insufficient authentication returns 401/403.

## Verification

From the solution root:

```powershell
dotnet run --project tests/StockFlow.MasterDataChecks
dotnet run --project tests/StockFlow.MasterDataChecks -- --database-readonly
```

The first command exercises real pages, controllers, services, authorization, and anti-forgery handling with in-memory test repositories. The second runs only SELECT queries against the configured database to verify repository SQL and mappings. Neither applies migrations or modifies database records. After applying the migration, verify real create/edit/deactivate flows through the UI. Browser interaction and PostgreSQL writes are not covered by these automated checks.
