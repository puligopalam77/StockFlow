# Multi-business Phase 2: shop business profiles

Phase 2 adds Electrical / Electronics, Bike Accessories, Clothing and Footwear /
Chappal Shop profiles. The selected profile belongs to the company/tenant and is
shared by all its branches. It is read from the database on every request, not
accepted from a browser header or a query parameter.

Administration now includes **Shop Business Profile** at `/Settings/BusinessProfile`.
An owner or administrator with existing Business & Billing VIEW permission can
review it; EDIT permission is also required to apply a change. No permissions are
automatically granted. Each profile restricts the permissions a user already has.

| Profile | Default specialist capabilities | Optional starter categories |
| --- | --- | --- |
| Electrical / Electronics | Existing serial, warranty and repair behavior | No changes to the existing catalog |
| Bike Accessories | Common retail, variants and product barcodes | Helmets, Gloves, Bike Covers, Mirrors, Bike Lights, Bike Luggage |
| Clothing | Common retail, variants and product barcodes | Shirts, T-Shirts, Trousers, Dresses, Sarees |
| Footwear / Chappal Shop | Common retail, variants and product barcodes | Slippers, Sandals, Shoes |

Non-electrical profiles hide electrical warranty/repair menus and deny their page
and API routes even to administrators. Product registration defaults to quantity
tracking, hides electrical warranty/tracking inputs, and does not offer electrical
specification templates. Direct product/variant API writes cannot re-enable those
features. Common menus remain subject to existing role permissions.

New category records belong to the current profile. Category list/search/detail
and parent selection are scoped by profile; product registration cannot assign a
category from a different profile. Existing electrical categories are preserved.
The optional starter list is created transactionally only when a profile actually
changes, without duplicating existing names or reactivating inactive categories.

Phase 2 does not yet add clothing size grids, footwear size systems, vehicle
compatibility fields or a custom attribute editor. Those are Phase 3/4 work.
Bike warranty/serial customization is also not enabled in this release.

## Protecting existing businesses

A profile change is blocked when the tenant has any products, including inactive
ones, or purchases, sales, stock movements or repair requests. Existing electrical
businesses should stay Electrical. Do not delete their catalog to unlock a change.
Converting a populated business requires a separate catalog migration plan.

The preview counts cover the whole tenant, not just the selected stock location.
A database function rechecks counts, permission, tenant and expected version under
the catalog lock. Product/category inserts take the same lock to prevent a profile
switch racing catalog creation. Changes and optional starter categories are saved
with an audit entry and a reason in one transaction. Stale versions are rejected.

Before migration 047, existing electrical workflows continue, but profile changes
are disabled and the screen shows the setup requirement. There is no automatic SQL
migration at startup or in the test runner.

## Manual deployment and SQL

1. Confirm migration 046 was applied to the intended database. Back up the database
   and pause writes for deployment. Do not switch a populated electrical tenant.
2. Run [the Phase 1 baseline query](../database/queries/electrical_phase1_baseline.sql)
   and export all result sets.
3. Manually run [047_shop_business_profiles.sql](../database/migrations/047_shop_business_profiles.sql)
   as a PostgreSQL superuser. The script adds metadata, constraints, guards and
   narrow functions; it does not change any current tenant's profile, seed starter
   categories, change products or post stock. It is transactional and rerunnable.
4. Run the same baseline query again while writes remain paused. Product/transaction
   counts, document totals and ledger quantities should match.
5. Run [business_profile_phase2_verification.sql](../database/queries/business_profile_phase2_verification.sql).
   Expect the four-profile constraint, positive version, three enabled catalog
   guard triggers, scoped functions and no product/category profile mismatches.
   Every current tenant should still be Electrical unless explicitly changed later.
6. Rebuild and restart StockFlow. Stop the IDE debugger before replacing its loaded
   binaries. After this migration, use the Phase 2 verification query: the earlier
   Phase 1 constraint has intentionally been replaced.

The SQL was prepared but is not automatically executed during development tests.
Actual migration execution, function permissions and concurrent database behavior
must be verified in your test database after you apply the script manually.

## Verification steps

Use separate **empty test businesses** for the three new profiles. Do not create
test sales or receipts, delete real products, or reclassify the live electrical shop.

1. Sign in to the existing electrical business. Check Products, Receive Goods,
   Sales, Stock, Warranty and Repairs. Existing products, IDs, prices, quantities,
   specifications, history and allowed menus must remain available.
2. Open `/api/branches/context`. Expect `businessProfileCode: "ELECTRICAL"`,
   `businessProfileConfigurationAvailable: true` and a positive version after 047.
3. Open Administration → Shop Business Profile. Select Clothing in a populated
   business: the preview must explain that existing products/transactions block
   the change and Apply must remain disabled. No data should change.
4. In an empty test business, preview Clothing, enter a reason, acknowledge the
   impact and apply. Leave starter-category creation selected. After reload, expect
   Clothing and its starter categories. Repeat using empty Footwear and Bike test
   businesses. All branches of a business should show the same profile.
5. Check each new profile's Products screen: only its categories should appear;
   electrical specification templates, serial tracking and warranty inputs should
   not be offered. Register a quantity-tracked test product. Starter categories
   do not by themselves add stock.
6. Verify Warranty/Repairs are absent from navigation and reports. Try direct URLs
   `/Service`, `/Warranty/Lookup`, `/api/service` and `/api/warranty/1`: access must
   be denied for a non-electrical shop, including its administrator.
7. Verify a staff member without Business & Billing rights cannot configure the
   profile. An administrator without EDIT can review but cannot save. Permissions
   for common products, receiving and billing must not be expanded by the profile.
8. In an empty test business, open the profile screen in two tabs. Apply a change
   in tab A, then attempt a different change from the older tab B. It must reject
   the stale configuration and require reload.
9. After registering a product, profile changes must be blocked even if that
   product is deactivated. Check the business-profile audit entry and reason with
   the read-only verification query.
10. Test registration, receiving and a sale with the new quantity-tracked product
    in the test database. Check desktop/mobile layout. Detailed industry attributes
    are expected in the next phases, not in this release.

For a rollback, do not run migration 046 again: it rejects new profiles. If any
tenant has moved to a new profile, restoring old application code requires a
planned application/database rollback. Do not relabel products or reverse stock
to force compatibility.

## Automated checks without SQL

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-BusinessProfilePhase2Checks.ps1
```

The runner builds to an isolated output folder, uses fake server repositories and
mocked browser APIs, and never executes SQL or connects to the application database.
Logs and results are in `tests/bin/business-profile-phase2-results`. Browser tests
require Microsoft Edge and the Playwright runtime restored by StockFlow.UiChecks.
