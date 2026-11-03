# Multi-business Phase 1: preserve the electrical baseline

Phase 1 establishes `ELECTRICAL` as the identity of existing electrical/electronics
businesses. Product Registration, battery/inverter/geyser specifications, serials,
warranty, repairs, receiving, sales, stock, reports and existing permissions keep
their current behavior. No products need re-registration.

This phase does not add a profile selector, enable bike/clothing/footwear profiles,
hide categories or menus, convert stock, or modify product/variant/serial/document
IDs. Those changes belong to later phases. The database temporarily permits only
`ELECTRICAL`.

The authenticated tenant/user lookup supplies the profile. A header or query
parameter cannot set it. Before migration 046, the optional database JSON key is
absent, so the same electrical identity is used without a missing-column failure.
Other database failures are not swallowed. Unsupported profile values are rejected
instead of being interpreted as electrical. No profile update API is added.

The layout retains `data-shop-type="battery-geyser"` for existing frontend behavior
and adds `data-business-profile="ELECTRICAL"`. No registration field or specification
template was changed for this phase.

## Manual database steps

No database scripts are executed by the app, this development task or the Phase 1
test runner. The owner executes the following manually.

1. Confirm the intended database and that current businesses use the electrical/
   electronics experience. Migration 046 assigns this profile to existing tenants
   and defaults future tenants to it until more profiles are released.
2. Pause application writes and take a backup. Existing migrations through 045
   should already be installed; do not rerun old migrations for this phase.
3. As database administrator, run
   [electrical_phase1_baseline.sql](../database/queries/electrical_phase1_baseline.sql).
   Export all result sets as the **before** baseline. It is read-only.
4. Run
   [046_electrical_business_profile_baseline.sql](../database/migrations/046_electrical_business_profile_baseline.sql).
   This adds tenant profile metadata, not product or stock data. It is transactional
   and rerunnable. Unexpected nonblank profiles cause a constraint error instead of
   being overwritten. If it fails, roll back the failed transaction and investigate.
5. Run the same baseline query again and export the **after** results. Record
   counts, document totals and ledger quantities must match while writes are paused.
6. Run
   [electrical_phase1_verification.sql](../database/queries/electrical_phase1_verification.sql).
   Expect the NOT NULL column/default, every tenant's `ELECTRICAL` profile, and a
   validated `ck_tenants_phase1_business_profile` constraint.
7. Deploy/rebuild and restart StockFlow. In VS Code stop the debugger before
   rebuilding, then press F5. The app also works before 046 is applied; there is
   no automatic migration at startup.

For an application rollback, leave the additive profile column in place. The
previous application ignores it; no stock reversal or data deletion is required.

## Verification in the running application

Use test data in a test database for transaction checks. Do not create artificial
stock movements in the live shop merely to verify deployment.

1. Sign in to the existing electrical business and choose its usual location.
   Open `/api/branches/context` in the same browser. Expect:

   ```json
   {
     "businessProfileCode": "ELECTRICAL",
     "businessProfileName": "Electrical / Electronics",
     "businessProfilePersisted": true
   }
   ```

   Before manually applying 046, `businessProfilePersisted` is `false` and the
   electrical experience remains available. After applying it, this is `true`.
2. Open Products. Check existing product/SKU/barcode identities, prices and stock.
   Open an existing product without saving: its tracking, warranty, variants and
   saved specifications must remain intact.
3. Open Add Product. Select battery, inverter and geyser categories in turn. Check
   capacity/voltage, inverter rating/power, and tank capacity/power respectively.
   Check brand filtering, serial/quantity selection, warranty/provider, pricing
   and variants. Cancel without saving when inspecting production data.
4. In the test database, save a new electrical model. Registration alone must not
   increase stock. Reopen it and confirm the values were retained.
5. Receive test goods with accepted and damaged serials. Confirm only accepted
   units enter available stock, damaged units remain separate, GRN/history links
   work, and an interrupted-save retry does not duplicate stock.
6. Sell a received test serial and inspect its invoice. Check stock decreased and
   warranty lookup/history still works. A sold serial cannot be sold again.
7. Open repairs/service, stock history and reports. Current menus, report access
   and location selection must remain available.
8. Sign in as limited staff. Confirm existing allowed pages and denied actions;
   check that another branch's stock remains separate.
9. Check desktop/mobile layouts. Physical scanner and printer checks remain manual;
   mocked browser checks cannot certify shop hardware.

## Repeat automated checks

From `E:\StockFlow`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-ElectricalPhase1Checks.ps1
```

The runner builds into `tests/bin/electrical-phase1-build` to avoid replacing DLLs
loaded by an active debugger. Server tests use fake repositories; browser tests
use mocked APIs. No database scripts or live database connections are used.
Microsoft Edge and the Playwright runtime from `StockFlow.UiChecks` are required
for browser tests. `-SkipBrowser` records that browser tests were skipped.

Logs and `results.json` are written to `tests/bin/electrical-phase1-results`.
Coverage includes legacy/persisted profile paths, header/query tampering, existing
fields/navigation and permissions, registration, receiving, sales, serial lookup,
warranty, repairs, inventory and billing. Real migration execution and the
before/after database comparison remain pending until the owner performs them.
