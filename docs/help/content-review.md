# Maintaining StockFlow help content

Latest claims review: 2 November 2026. Current billing/help guidance now distinguishes
registered-customer full payment, optional electronic references, no new credit/
split checkout, historical collections, four profiles, working GST registers,
quotations, missing expenses, provisional cost estimates and unverified hardware.
The renamed Add a customer section retains exactly its previous SALES CREATE and
CUSTOMERS CREATE access checks; no permission was added or removed. Changed
English keys fall back to English; existing translations are not newly certified.
Follow [the evidence hierarchy and claims policy](../planning/StockFlow-evidence-policy-2026-11-02.txt).

The previous review on 11 October 2026 updated the embedded set to eight guides.
Changes cover current Reports submenu names and Shop summary, sales filtering and
CSV export, receipt history, stock correction/counting, four-field cash closing,
company logo/billing defaults, Android connection and device features. Sources
include the Reports navigation partial, Inventory/Index, Sales/Index,
Settings/Company, StockInflow and StockInflow/Details, CashClosing/Index, Android MainActivity and
DeviceFeatures, plus camera-scanner.js and help-voice.js. This is a repository
review; physical-device and shop acceptance remain separate. New English text
falls back to English where existing language packs lack a translation.

The additional `application-features.md` guide was checked against Service/Create,
Reports/Index, Transfers/Index, CashClosing/Index, Admin/Users, Admin/Roles,
Admin/Stores, shared configuration navigation and account screens on 2026-10-08.
It extends the embedded guide set to seven. Local question matching and business
summary checks run without AI, using scoped fake records. Native wording for new
guide translations and live shop workflows still need review.

The staff guides use current English captions and describe existing workflows. Keep task instructions in the staff articles and implementation evidence here. The signed-in Help panel embeds these eight guides, filters screen links using normal permissions, and requires no database migration. Normal article search works without AI; optional [AI guide selection](ai-setup.md) requires a configured model.

The guides are embedded resources in StockFlow.Web. Rebuild after changing them;
published builds carry the same content without requiring a separate docs folder.
The renderer supports headings, paragraphs, bold captions, numbered steps, tables
and links between these eight articles. Keep content within this format. The panel
renders text as DOM nodes and does not interpret article HTML or arbitrary links.

## Code review record

Reviewed on 8 October 2026 against the repository pages, browser handlers and repositories listed below. This is code verification, not a claim that transactions were performed in a running shop database.

| Guide | Caption and workflow sources |
| --- | --- |
| [Products](products.md) | [Products page](../../src/StockFlow.Web/Pages/Products/Index.cshtml), [master controls](../../src/StockFlow.Web/Pages/Shared/_ConfigurationMaster.cshtml), [registration fields](../../src/StockFlow.Web/Pages/Shared/_ProductRegistrationForm.cshtml), [registration handler](../../src/StockFlow.Web/wwwroot/js/product-registration-form.js) |
| [Receiving stock](receiving-stock.md) | [receipt page](../../src/StockFlow.Web/Pages/Purchases/Create.cshtml), [receipt handler](../../src/StockFlow.Web/wwwroot/js/purchase-create.js), [purchase details](../../src/StockFlow.Web/Pages/Purchases/Details.cshtml), [delivery handler](../../src/StockFlow.Web/wwwroot/js/purchase-receipts.js) |
| [Billing](billing.md) | [billing page](../../src/StockFlow.Web/Pages/Sales/Create.cshtml), [billing handler](../../src/StockFlow.Web/wwwroot/js/sales-create.js), [sales list](../../src/StockFlow.Web/Pages/Sales/Index.cshtml), [invoice](../../src/StockFlow.Web/Pages/Sales/Invoice.cshtml) |
| [Returns](returns.md) | [sale details](../../src/StockFlow.Web/Pages/Sales/Details.cshtml), [operation handlers](../../src/StockFlow.Web/wwwroot/js/shop-operations.js), [return validation and accounting](../../src/StockFlow.Infrastructure/Repositories/OperationsRepository.cs) |
| [Warranty](warranty.md) | [lookup](../../src/StockFlow.Web/Pages/Warranty/Lookup.cshtml), [details](../../src/StockFlow.Web/Pages/Warranty/Details.cshtml), [lookup handler](../../src/StockFlow.Web/wwwroot/js/warranty-lookup.js), [sale registration](../../src/StockFlow.Infrastructure/Repositories/SalesRepository.cs), [warranty repository](../../src/StockFlow.Infrastructure/Repositories/WarrantyRepository.cs) |
| [Common problems](common-problems.md) | Receiving and billing handlers above, [login](../../src/StockFlow.Web/Pages/Account/Login.cshtml.cs), [password recovery](../../src/StockFlow.Web/Pages/Account/ForgotPassword.cshtml.cs), [field validation](../../src/StockFlow.Web/wwwroot/js/common/form-validation.js) |

## Review changes before publishing help

1. Compare each bold caption with its Razor page or JavaScript control. Include conditional button names when permissions change them.
2. Follow each task from its page through its handler and application validation. Check when stock or money changes and what retries do.
3. Check article links, screen routes, required fields and numeric limits.
4. On a configured test installation, have authorized staff perform product registration, receipt, billing, return and warranty tasks. Confirm the displayed result and history for each task before calling the guides shop-tested.
5. Update the affected article and this review record after behavior changes.
   Regenerate help packs with the development generator's `--help-only` option.
   Check every English key has a translation in every selectable language and
   preserve placeholders such as `{count}`. Rebuild after updating guide text.

## Help language review

Help translations are stored separately under `wwwroot/languages/help`. They
include paragraphs, numbered instructions, headings, table cells, questions and
panel captions for all 22 Indian languages. Header selection updates an open
article in place, keeps the search input and permitted screen links, and restores
English alone when English is selected. A missing or failed pack leaves English
instructions available with a visible status message.

Initial wording is machine translated from the public guides using the existing
development-only translation helper. Coverage, placeholders, rendering and
language changes are checked by automated tests. Native-speaker review is still
required before describing the wording as linguistically reviewed. In particular,
review stock conditions, refunds, credit and warranty coverage with shop staff.
English captions remain the reference for choosing controls in the application.

## Current limitations relevant to support

The header store selector was removed in favor of the language dropdown. Some consolidated-location warnings in operational pages still mention a top-bar location selector. The staff guides do not direct users to that missing control: the administrator must provide an assigned location or review the store context through existing administration/report controls.

The password recovery page does not save an anonymous password reset without account verification. Do not describe it as a completed reset service.

Help content covers guidance, not automatic execution of sales, returns, payments or security changes. Existing guides under `docs/phase*.md` remain the developer and deployment references; do not feed database credentials, operational secrets or customer records into a future help assistant.
