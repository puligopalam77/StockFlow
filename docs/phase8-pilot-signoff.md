# One-store pilot sign-off

Status: **Pending physical-store validation**

Use the [per-workflow acceptance record](planning/StockFlow-pilot-acceptance.csv)
for expected/actual results, evidence, reviewer and sign-off date. The executable
release gates run through `tests/Run-ShopPilotChecks.ps1 -ReleaseAcceptance`.
Missing mandatory evidence produces BLOCKED, including when the build succeeds.
See [release acceptance instructions](planning/StockFlow-release-acceptance-2026-11-02.txt).

The owner/admin can complete the interactive checklist at `/Admin/Pilot`. Every
check starts pending; all checks need a passing result and evidence before sign-off.
The checklist stays in the current browser. Export its JSON for the owner, and
record the actual applied migration baseline for that deployment. Editing any result requires
a new sign-off. Automated results are produced by `tests/Run-ShopPilotChecks.ps1`;
they do not complete the physical checks below.

| Detail | Record |
| --- | --- |
| Pilot store / location | |
| Owner / responsible administrator | |
| Receiving user / cashier user | |
| Application build / deployment date | |
| Applied migration baseline | |
| Workstation / browser / network URL | |
| Scanner model / interface / Enter suffix | |
| Printer / driver / label size / print scaling | |
| Test clothing variants / battery serials | |

| Acceptance check | Pass / Fail / Not in scope | Evidence or issue |
| --- | --- | --- |
| Role template, account setup and assigned store | Pending | |
| Permitted menus/actions and other-store rejection | Pending | |
| Receiving known/unknown barcodes and serialized units | Pending | |
| Physical scans: existing/generated labels, leading zeroes and rapid scans | Pending | |
| Physical label printing, scanning and stable reprints | Pending | |
| Registered-customer billing, supported full-payment methods and invoice printing | Pending | |
| Historical credit/split records and later collection without issuing new credit | Pending | |
| Duplicate serial / insufficient stock rejection | Pending | |
| Two-counter last-unit / serial concurrency | Pending | |
| Interrupted response and exact saved-request retries | Pending | |
| Transfers and in-transit reconciliation | Pending | |
| Access revocation / deactivation with an existing session | Pending | |
| Stock, invoice, collection and credit report reconciliation | Pending | |
| Drawer count, cash refunds/payments, explained difference and closing history | Pending | |
| Scheduled backup, archive checksum and separate database restore test | Pending | |
| Staff can receive stock, bill and correct errors in Telugu/Hindi/English | Pending | |

Operator acceptance:

Accepted by / date:

Open issues and owners:

Next-store rollout approved by / date:

Previous build and database backup available at:

Use [the same-task benchmark protocol](planning/StockFlow-workflow-benchmark.txt)
and [results template](planning/StockFlow-workflow-benchmark-results.csv) for measured
workflow acceptance. No competitor comparison or live customer pilot has been run
merely because these templates exist. Current New Sale intentionally requires one
full-payment method; record this policy when comparing products.

| Baseline measure | Observation count | Baseline value/unit | Pilot value/unit | Evidence |
| --- | --- | --- | --- | --- |
| Billing time | Pending | | | |
| Correction frequency per bill | Pending | | | |
| Failed saves per attempt | Pending | | | |
| Recovery time | Pending | | | |
| Stock discrepancies by SKU/unit | Pending | | | |
| Warranty enquiry time | Pending | | | |
| Support requests | Pending | | | |
| Staff training time | Pending | | | |

Set targets only after recording the baseline and its sample size. Keep production
two-shop topology and shop-master isolation pending until verified. Existing cost
estimates do not establish a historical costing policy or accounting valuation.
