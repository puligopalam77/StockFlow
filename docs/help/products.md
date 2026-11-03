# Adding a battery or geyser

This guide describes Electrical products. Bike Accessories, Clothing and Footwear
use their configured category/variant fields. Serials and warranty currently
belong to Electrical. Fractional grocery quantities and stock lot/expiry allocation
are not implemented. Product registration adds no stock; use [Receive new stock](receiving-stock.md).

## Add a product

1. Open **Products & contacts > Products**, then choose **Add product**.
2. Under **What are you adding?**, choose **Battery** or **Geyser / water heater**. Use **Spare part / accessory** for related parts.
3. Enter the product name, brand and model. The product group is selected when a matching group exists.
4. For a battery, copy **Capacity (Ah)**, **Voltage (V)** and **Battery type** from the label. For a geyser, copy **Tank capacity (L)**, **Power (W)** and **Heater type**. Leave optional details blank when unknown.
5. Enter buying/selling defaults and applicable warranty months. GST invoicing uses the reviewed effective classification in GST Settings; an editable product percentage or supplier bill alone does not establish statutory classification. Twelve months is one year.
6. Check how the stock is counted. Batteries and geysers start with **By serial number**. Spare parts start with **By quantity**. Change this only to match how the item is actually tracked.
7. Select **Save product**. The product is now available when receiving stock or making a bill.

## Extra details are optional

**More options: barcode, photo and packing** contains the shared model barcode, photo, HSN code, packing and supplier code. A shop item code is created for you. You may change it to your own code.

A shop item code identifies a model in your shop. A model barcode can be shared by many items. A serial number identifies one physical battery or geyser. Enter individual serial numbers when receiving or selling those items.

**More battery details and warranty card terms** can hold the free-replacement period and part-payment warranty period. **More geyser details and warranty card terms** can hold mounting, water-pressure rating, tank warranty and heating-element warranty. These values are product notes. They do not automatically extend the overall warranty or approve a free replacement. Copy the actual brand terms and check the warranty record when processing a claim.

## Add a product while receiving stock

Choose **Add new product** on the receipt. Fill in the same details, then choose **Save product & add to receipt**. Check the quantity and enter each serial number before confirming **Receive Stock**.

If the model already exists, select it rather than registering it again.

## If the Battery or Geyser group is missing

Ask the owner to add the group in **Products & contacts > Product groups**, then reopen the product form. Choose the groups allowed for each brand in **Brands**. A brand assigned only to Battery will not appear for Geyser until its allowed groups are updated.

The manual restore folder includes an optional script to add missing Battery and Geyser root groups. It does not change existing product or stock records.
