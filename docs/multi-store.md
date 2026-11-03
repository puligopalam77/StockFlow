# Multi-store rollout

The application now supports isolated business accounts, shared catalogs within each business, retail stores, stock locations, central warehouses, assigned user access, and stock transfers. Database installation is manual. Application startup does not apply migrations.

## Apply to the application database

1. Back up the database and stop StockFlow. Ensure migrations through `019_purchase_receipts.sql` have already been applied. Do not rerun completed migrations.
2. Review the three seed records near the beginning of `020_tenants_stores_locations.sql`. Existing data belongs to business code `DEFAULT`, store `MAIN`, location `Main stock`. Change the names/code if appropriate. Existing stock is assigned to this one physical location; migration cannot infer historical branch ownership. Existing Admin users become business owners. Review that list before rollout.
3. Run **`database/migrations/020_tenants_stores_locations.sql`**, then **`database/migrations/021_stock_transfers.sql`**, as a PostgreSQL superuser (PostgreSQL 15 or newer). Both files use transactions. If a file fails, roll back its transaction and investigate before retrying. The security helper functions must remain owned by the trusted migration administrator because they validate membership while reading through forced row security.
4. If the application's database login differs from the migration login, manually grant it membership: `GRANT stockflow_runtime TO your_application_login;`. Replace the identifier with the actual login. The application sets its effective role to the non-owner, non-superuser `stockflow_runtime` on every scoped database connection. Never grant BYPASSRLS or ownership to this runtime role.
5. Rebuild/restart StockFlow and sign in again, using business code **`DEFAULT`** unless changed in step 2. Old authentication cookies do not contain tenant/location claims and are rejected. The `IProductRepository` registration is present in `Program.cs`, fixing the previously reported service-construction exception.
6. Open **Store management** (`/Admin/Stores`) to add stores and stock locations. Each new store gets a main stock location. A central warehouse has no retail store. Create employees in **User Management**, then assign stores/central warehouses by searching for their name or username on the Store management screen. New users cannot sign in until they have an assignment. Owners can access every location in their business.
7. Physically reconcile the legacy stock at Main stock, then use transfers to distribute it. Do not insert duplicate opening stock for the new branches.

For another independent business, edit and manually run `database/inserts/004_provision_tenant.sql`. It refuses unchanged placeholders. Generate its password hash with `dotnet run --project src/StockFlow.Web -- --hash-password`; paste the output into the template. Tenant creation is a database-administrator operation, not a privilege of another business's owner. The initial owner must change the password at first login.

## Daily operation

The top-bar selector switches between one stock location, every authorized location in a store, or all authorized stores/warehouses. The shared catalog remains business-wide. Existing inventory, purchase, sales, invoice, scan, service, warranty and dashboard queries automatically use the selected scope. Select an individual location to post transactions; consolidated views are read-only for operational transactions. Billing requires a location attached to a store; central warehouses can receive stock and transfer it to shops.

The server checks business status, account status, assigned locations and session security version on every request. Revoking an assignment takes effect on the next request. Requests from an old browser tab are rejected if its location differs from the active authentication cookie. Saved sale/receipt retries also retain their original location. Cross-business foreign keys are rejected in PostgreSQL, including forged product, supplier, customer, user and location IDs.

**Transfers** (`/Transfers`): create a draft with variants and quantities, request it, approve it, dispatch every requested line, then receive actual quantities. Dispatch deducts from the source and credits a dedicated in-transit location. Receipt deducts from transit and credits the destination. Partial receipts remain open. Serial-tracked lines require exactly the selected physical units. Select the source stock location to load dispatch serials; destination receipts show the transfer's outstanding serials.

Damage and loss use **Record damage / loss**, with a reason and selected quantities/serials. Damaged stock is recorded at the destination but excluded from saleable stock. Lost stock is written off in transit. `RECEIVED` means every dispatched unit is accounted for by receipt, damage or loss; inspect the line counts for the breakdown. Cancellation is allowed only before dispatch. Returning already dispatched stock requires receiving/reconciling it and making a reverse transfer. Request IDs make retries idempotent, and the browser preserves uncertain transfer requests for an explicit retry.

Grant ordinary transfer view/create/approve through **Stock transfers** permissions. Dispatch, receive and cancel each have a separate module; grant its **View + Approve** permissions for that responsibility. Source assignment is required for approval/dispatch/cancellation, destination assignment for receipt/discrepancies. Transfer creators also need Products view; serialized dispatchers need Inventory view to pick units. Module permissions never replace location access.

**Reports → Stock by store** (`/Reports/Stores`) shows location and variant balances, external inflow/outflow, internal transfer movements and sales totals (only with Sales permission). Owners can see in-transit balances in the all-stores view. Existing dashboards count all visible stock movements, including transfer legs; the comparison report separates internal transfers to avoid treating them as external purchasing/selling activity. Optional store-specific pricing from the proposal is deferred: shared variant prices still apply and actual sale prices remain snapshotted on sale items.

## Validation

The isolated database suite creates a unique database on the test PostgreSQL cluster at `127.0.0.1:55441`, applies all migrations and drops only that database when finished:

```powershell
dotnet build tests/StockFlow.MasterDataChecks --artifacts-path tests/bin/multistore-build
dotnet tests/bin/multistore-build/bin/StockFlow.MasterDataChecks/debug/StockFlow.MasterDataChecks.dll --multistore-database-test
```

Checks cover legacy stock backfill, restricted runtime connections, cross-tenant parent rejection, branch isolation, transfer retry, partial/over receipt, damage/loss, exact serial dispatch/receipt, store-tagged sales, revoked assignments and tenant-aware login. Existing fake-repository application checks exercise startup/DI, authorization and anti-forgery without writing to the application database. The original variant integration suite remains usable on its pre-multi-store schema.

For a manual reconciliation after rollout, an administrator can query:

```sql
-- Company/location balances, including stock in transit.
SELECT tenant_id, location_id, SUM(current_stock) AS stock
FROM vw_location_current_stock GROUP BY tenant_id, location_id ORDER BY 1,2;
-- Outstanding transfer quantities; completed transfers should have none.
SELECT h.tenant_id, h.transfer_number, h.status,
       SUM(i.dispatched_quantity-i.received_quantity-i.damaged_quantity-i.lost_quantity) AS outstanding
FROM stock_transfers h JOIN stock_transfer_items i USING(tenant_id,transfer_id)
GROUP BY h.tenant_id,h.transfer_number,h.status ORDER BY 1,2;
```

Inventory is derived from the ledger; there is no second balance table to keep synchronized. Internal transfer legs conserve total stock until damage/loss is explicitly posted. Database login credentials and the application server are trusted: row security protects normal application queries; it is not a sandbox for people given direct administrator/database credentials.
