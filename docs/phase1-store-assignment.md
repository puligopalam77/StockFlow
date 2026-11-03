# Phase 1: automatic store assignment

Retail staff sign in directly to the default assigned store's active SALES_FLOOR
location. If a store has several sales floors, the lowest active location ID is
used. A single assigned store can be used without an explicit default. Multiple
assigned stores require a default. Backrooms never silently become billing locations.
Warehouse-only staff retain their assigned location for receiving and transfers.

Staff see the assigned store/location as text and cannot switch to another store,
another location, or consolidated mode through the context API. On each request,
the server checks the session against the current default assignment. An assignment
change requires signing in again; old sessions cannot keep posting to the former store.

Business owners and users whose current database role is Admin retain filters for
authorized stores and consolidated reports. Their existing module grants and
location access still apply. Consolidated operational views remain read-only.
Admin status does not grant owner-only store management privileges.

## Manual database baseline

No new migration is needed: this change reuses schema through migration 025.
Do not execute completed migrations again. Run
`database/queries/phase1_store_assignment_checks.sql` manually as a database
administrator in the application database. The script is read-only and shows:

- Required schema objects/columns (this is not migration execution history).
- Employee automatic locations and assignments requiring attention.
- Store sales-floor counts and the automatically selected location.
- Runtime role and row-security configuration.

If required objects are missing, stop before the data checks and follow the
existing database README for missing migrations. Resolve assignment problems in
Admin / Users or Store management. Staff with multiple assignments remain pinned
to their default store; admin can change that assignment when staff move stores.
Restart the updated application and have staff sign in again.

## Verification

`dotnet run --project tests/StockFlow.MasterDataChecks -- --store-assignment-checks`
checks selection, staff page rendering, switch denial, forged/consolidated scopes,
assignment changes, administrator switching, and read-only consolidated posting.
The checks use fake repositories and never access the application database.

After deployment, manually test a cashier at each shop: scan and sell an item,
then verify that only that store's stock is reduced and its invoice/report shows
the sale. The SQL baseline does not substitute for this live reconciliation.
