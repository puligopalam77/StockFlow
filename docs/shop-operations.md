# Shop operations added after phase 8

Apply `database/migrations/028_shop_operations.sql` manually after migration 027. Stop StockFlow, take a database backup, run the migration as a PostgreSQL superuser, and restart the updated application. The application does not apply this migration automatically.

## Where staff use the new workflows

- **Sales & invoices > invoice details:** later customer collections, returns/refunds/exchanges, delivery or installation updates, and old battery/appliance trade-ins.
- **Purchase orders & deliveries > purchase details:** supplier payments and returns of received stock.
- **Inventory > Current stock:** physical quantity count and automatic audited variance adjustment. A mismatch in serialized stock is deliberately blocked until the individual missing or extra serial is identified through Stock adjustment.
- **Service & Warranty > service details:** service-charge and parts collections with a printer-friendly browser receipt.
- **Warranty claims > warranty details:** manufacturer RMA reference, dispatch/return status and charges.
- **Reports > Sales by Product:** net sold/returned quantities, net sales, weighted-average estimated cost and estimated margin.

Returns preserve the original invoice. For an exchange, post the original return and create a new replacement sale so both serial histories remain traceable. A resellable serialized return becomes available stock again; damaged, supplier-return and service-hold dispositions remain unavailable.

## Permissions

Migration 028 adds assignable actions to the existing modules:

- Sales: `COLLECT`, `RETURN`, `FULFIL`
- Purchases: `PAY`, `RETURN`
- Service: `COLLECT`

The tenant owner receives these actions. Existing Purchase Edit/Approve and Service Deliver grants are mapped to the matching new actions. Review staff assignments in **Setup > Roles & permissions** after migration.

## Verification

Run `database/queries/shop_operations_checks.sql` after migration. The installed-table query should show every table name, the action query should show all six new actions, and the remaining validation queries should return no rows.

The migration does not create returns, payments, stock counts, trade-ins or RMA records for historical documents. Those records require a real operator action and reason.
