# Checking warranty and recording claims

Check the warranty using the item's serial number or the customer's invoice number. A claim records the reported problem and its handling; creating a claim does not issue a replacement.

## Find a warranty

1. Open **Repairs & warranty → Check warranty** at `/Warranty/Lookup`.
2. In **Search by**, select **Serial number** or **Invoice number**.
3. Scan or type the number into **Scan / enter number**, then select **Search**.
4. Open the matching **Warranty details** and check the item, warranty dates, coverage and claim history.

Use the individual serial rather than a barcode shared by every item of a model. Invoice search can find warranties for multiple items on the same bill.

New sales automatically register warranty when the sold item's warranty term is greater than zero. A product with **Warranty months** set to zero does not receive that automatic warranty.

## Record a problem

1. Open the correct **Warranty details** at `/Warranty/Details/{id}`.
2. If **Create claim** is available, enter **Problem reported** and select **Create claim**.
3. Review the claim under **Claims**. An authorized user can select **Update** beside the claim, choose **Update status to**, fill in **How was the problem handled?** and **Update / resolution notes** as required, then select **Save claim update**.

An existing in-progress claim can prevent a duplicate claim. Returned, cancelled or replaced items may not be eligible for a new claim; check the displayed status and history.

## Handle a replacement

A physical warranty replacement requires an approved claim and an available serialized unit of the same product. Select **Update** beside the approved claim and choose **Replaced** under **Update status to** when your account permits it. Select **Available unit (same product)** and explicitly choose **Confirmed replacement warranty rule**: **Continue original dates**, **New full warranty**, or **Custom end date**. Enter the custom date when that rule is selected.

Confirm the provider's coverage rule before saving the claim update. The original serial stays in history; a replacement is recorded as another unit.

## Missing warranty or coverage details

First check the entered serial or invoice and the original sale. If authorized, use **Register a missing warranty** for an older sold item by entering its **Sale item ID** and selecting **Save warranty**. StockFlow uses the original sale date and warranty term; this does not create coverage for an unsold item.

If **Type** or **Provider** is unspecified, ask the responsible person to confirm it before promising repair or replacement. Authorized users can update **Warranty type and provider**, provide **Reason**, and select **Save warranty details**.
