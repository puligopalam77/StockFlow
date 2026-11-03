# Customer returns and refunds

Record a customer return against the original completed sale. Choose the physical condition of the goods separately from how the customer is settled.

## Post a return

1. Open `/Sales`, find the original invoice using **Search**, select its invoice number, then select **Sale details** on the invoice.
2. Open **Return, refund or exchange**. This section requires return permission.
3. Choose **Invoice item** and enter **Quantity**. Return only the quantity still eligible on that invoice. A serialized invoice line is returned as one unit.
4. Choose **Stock condition** using the table below.
5. Choose **Settlement**. For money refunded immediately, select **Cash**, **Card**, **UPI** or **Bank payment**, then enter **Refund paid now**. Leave it zero for a settlement that does not pay money immediately.
6. Enter **Reason** and select **Post return**.
7. Review **Account activity** to check the recorded return and remaining balance.

| Stock condition | Use when |
| --- | --- |
| Resellable | The returned goods can be sold again |
| Damaged | The goods are damaged and cannot be sold as normal stock |
| Return to supplier | The goods are intended for supplier return |
| Hold for service | The goods need service handling |

Only **Resellable** goods should go back into stock available for sale. **Settlement** records how the customer is settled; it does not replace the stock-condition decision.

## Record an exchange

Choose **Exchange**, post the return, then create a separate replacement sale through [New sale](billing.md). Posting the return alone does not create the replacement bill. Check the actual settlement and both records with the shop owner; the original invoice and serial history remain traceable.

## Resolve a refund limit message

The refund cannot exceed the returned value or the amount available from payments already received after accounting for earlier returns and refunds. A sale on credit may need **Credit adjustment** instead of a cash refund. Ask an authorized user to review the invoice balance if the expected amount differs.

This guide covers customer returns. Supplier returns are handled separately under **Supplier account and returns** in **Purchase details**.
