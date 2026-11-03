# Phase 2: navigation and consistent operational screens

The shared sidebar now presents Dashboard, Receive Stock, Sell / Billing,
Inventory, Customers and Reports. Expandable task groups contain the existing
routes. Setup contains Configuration and permitted administration/company links;
Service & Warranty contains permitted service and warranty routes.

Navigation checks the existing module/action grants for each destination. Empty
groups disappear. View-only sales/purchase users do not see create links or create
buttons. The group containing the current page opens automatically, with one
current-page link. Groups use native keyboard-accessible disclosure controls;
the mobile drawer includes visible disclosure controls in its focus handling.

Operational screens share spacing, form-control sizes, buttons, table styling,
filter panels and message styling. Receiving, billing, stock history, inventory,
customers and scanning have consistent headings. Existing URLs, APIs, transaction
rules and scanner handlers remain in use. The receipt shortcut still opens the
existing purchase form: guided receiving is Phase 3.

## Configuration boundary

No Configuration, Product, Variant, Category, Brand or Supplier module files were
changed. Their existing styles, editors, scripts and behavior remain intact.
The shared sidebar places their existing Configuration entry under Setup.
Operational styles are not loaded on those module pages.

## Database and rollout

No SQL scripts or migrations are required for Phase 2. Restart the rebuilt app
and refresh the browser. Database permissions and history are unchanged.

## Validation

- Build `tests/StockFlow.MasterDataChecks`.
- Run it with `--navigation-checks` for task grouping, active links, permission
  filtering and Configuration regressions. This creates real Razor fixtures.
- Run the default MasterDataChecks suite for application/security regressions.
- Run `tests/StockFlow.UiChecks/navigation-offline.cjs` using the installed
  Playwright Node executable for desktop/mobile layout and keyboard navigation.

All these checks use fake repositories or mocked APIs; the application database
is not accessed. Browser screenshots are written to
`tests/bin/navigation-ui-fixtures/navigation-*.png`.
