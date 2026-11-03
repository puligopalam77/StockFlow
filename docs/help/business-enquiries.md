# Business enquiries from StockBot Assist

Additional direct questions now use the same scoped dashboard services as the
application: **How much stock is available?**, **How much sales today?**,
**How much sales this month?**, **How many low stock products?**,
**How many pending repairs?**, and **How many warranties are expiring?**.
Each answer requires its source module's View permission. Count/amount questions
display only the value; instructions and links appear when asked how to check.
**How much sale completed today?** returns today's completed bill count;
**How much sales today?** returns the sales amount. Sales quantity, completed bill
count and amount also support yesterday, this/last week, this/last month and explicit
ISO dates, for example **How much quantity sold yesterday?** or **How much sales
from 2026-10-01 to 2026-10-07?**. These return the value directly, with no list-screen
directions. **How much amount collected day before yesterday?** reads non-credit
payments received two days ago, using payment date rather than invoice date. It
includes payments for older completed invoices and does not subtract refunds.
This is collected money, not sales revenue or net cash. Common misspellings such
as "amout" are accepted. **Two days ago** also works.

After a successful sales/payment answer, a date-only follow-up such as **yesterday**
reuses that metric for ten minutes within the same account and business/location
scope. Without a previous topic, the assistant asks which figure you want rather
than referring you to an administrator. Questions and figures are not persisted;
only the last metric is kept temporarily in server memory.

Missing dates, product-specific sales totals, profit or customer-dues questions
receive clarification instead of invented values.
These queries do not require AI. Live values and PostgreSQL policies must still be
verified against source screens on the configured database; automated checks use
fake scoped records.

Open **Need help? → How much stock is available?**. Enter at least two characters
of the product name, item code or barcode, then select **Check stock**. You can also
select **Show low stock only** without entering a product. This enquiry works even
when AI is disabled.

Results show each product or variant separately, its available quantity, the
recorded stock count, the active location scope and the time it was checked.
Select **Stock on hand** to open the source screen. **Check stock** fetches fresh
records each time; availability can change before a sale is completed.

For serial-tracked goods, available quantity counts units currently in stock.
For goods counted by quantity, it uses the stock balance. If serial availability
differs from the recorded balance, Help displays both values and asks the
administrator to review them. No matches means no matching accessible record;
it does not mean the product has zero stock. A matching zero-stock record shows zero.
More than 50 matches are marked as partial; refine the search to find the exact item.

## Access and data handling

Inventory View permission is required for both the form and `/api/help/stock`.
The endpoint uses the same InventoryRepository as `/api/inventory`, including
the existing authenticated BusinessScope, runtime database role and PostgreSQL
tenant and location policies. Submitted tenant or location query parameters
cannot override the authenticated scope. Staff remain assigned to their store;
authorized owners/admins can read their permitted consolidated scope.

The enquiry reads existing records, has no write operation, excludes inactive
products and does not send stock records to the AI model. Use the explicit stock
enquiry for live stock quantities. The current question endpoint also supports
the business enquiries listed below, subject to account permissions.

## How much quantity sold today?

Open **Ask StockBot Assist**, enter **How much quantity sold today?** and submit.
The answer shows only the units sold on completed bills. Ask **How do I check
completed sales today?** to get the Sales history checking instructions.
The input clears after submission; your question stays visible above its answer.
This direct enquiry works without a configured AI model and requires Sales View
permission. Phrases such as **How many units were sold today?** also work.

Only completed sales dated today are counted. Draft or cancelled bills, receiving,
repairs and other stock movements are excluded. Returns are not deducted from this
gross sold quantity. Date boundaries use the configured shop timezone. No matching
completed sales yields zero. The repository uses the normal business/location-scoped
connection; users cannot submit their own scope or SQL, and records are never sent
to the AI provider. Weekly periods start Monday; this week/month runs through today.
Explicit ranges include both endpoints and allow up to 367 days. Quantity is gross
units sold, count is completed bills, and amount sums completed invoice totals.
Supported product/brand and payment-mode enquiries use their explicit tools.
Unsupported filters must not silently return an unfiltered total.

When the optional AI provider is enabled, it can interpret additional wording and
Indian-language questions into the same supported metrics and dates. It can also
route current product/customer/supplier counts, stock and low-stock counts, pending
and ready repairs, expiring warranties, and a named product's available stock to
the existing dashboard/inventory services. Product-stock results use serial-unit
availability for serialized goods, distinguish no matches from zero, and request
clarification for broad matches or discrepant stock. The server
validates the filters and queries records within the signed-in user's access.
The model receives the question, not business records, and cannot choose SQL,
tenant, store or location. Without a configured model, common English questions
and ISO date ranges still work directly.

`--help-sales-checks` verifies the API with scoped fake records, including disabled
AI, yesterday, date ranges, invalid filters, denied permissions, business scope,
and AI-interpreted questions using a controlled provider.
Confirm live totals against completed sale lines on a configured test database
before rollout; these tests do not verify live PostgreSQL data or policies.

## Verification and deployment

`--help-stock-checks` uses scoped fake stock records to verify business separation,
location validation, permission checks, exact quantities, discrepancies, empty
results, partial results and field limits. Browser checks verify the displayed
values, fresh requests, source link and clearing previous results when access fails.
The fake tests do not prove PostgreSQL policy behavior; the existing database
scope and policies are reused. Compare the enquiry with Stock on hand using
accounts in two businesses and two assigned locations on a configured test database
before shop rollout.

No new tables, database migrations or AI-provider setup are needed. Rebuild and
press F5 in VS Code to load the enquiry.

## Automatic business tools

AI interpretation selects a fixed tool; it does not execute a model-generated
API URL. The tools call the same repository/service used by the corresponding
application APIs, avoiding a second HTTP call inside the server.

| Information | Permission | Source |
| --- | --- | --- |
| Sold units, completed bill count, sales value | Sales View | Sales and sale lines, filtered by invoice date |
| Money collected | Sales View | Non-credit payments on completed bills, filtered by payment date |
| Current stock, low stock count | Inventory View | Dashboard inventory/business summaries |
| Named product's available units | Inventory View | InventoryRepository, also used by `/api/help/stock` |
| Active product count | Products View | Dashboard inventory summary |
| Active supplier/customer counts | Suppliers/Customers View | Dashboard inventory summary |
| Pending/ready repairs | Pending services View | Dashboard business summary |
| Warranties expiring within 30 days | Expiring warranties View | Dashboard business summary |

All sources use the authenticated business/location scope. Missing information
is clarified within the conversation; unavailable tools are described honestly
instead of inventing figures. Historical stock snapshots, per-employee totals,
and complete accounting-profit questions are not implemented as business tools.
Customer dues, supplier balances, overdue customers, product sales, returns,
payment-mode collections and operational questions use dedicated scoped tools.

`--help-sales-database-readonly` executes the period/collection queries against
PostgreSQL under the runtime role with an empty business scope. It validates SQL,
date bounds and result mapping, and checks that this scope exposes no totals.
It does not write records or validate an authorized shop's recorded figures.
