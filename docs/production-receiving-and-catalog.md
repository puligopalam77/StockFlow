# Production receiving and catalogue extension

Apply `database/migrations/030_receiving_catalog_extensions.sql` manually after migration 029. Stop StockFlow, back up the database, run the migration as a PostgreSQL superuser, and restart the application. Then run `database/queries/receiving_catalog_extension_checks.sql` against the same database.

## Product registration

Product registration now stores HSN, GST, unit of measure, an uploaded JPG/PNG/WebP image or external image URL, and active/discontinued status. Each item configuration stores purchase price, selling price, MRP, pack quantity, reorder level, supplier product code, one primary barcode, up to 20 aliases and multiple specification rows. Existing `is_active` history is retained; the screen presents an inactive product as **Discontinued**.

## Scan-first registration

Receiving uses this order:

1. Search StockFlow unit identifiers and model barcodes.
2. Select a known product automatically.
3. For an unknown code, query `supplier_product_catalog`, preferring a record for the selected supplier and then a manufacturer-level record with no supplier.
4. Open a draft prefilled from that catalogue.
5. Require staff to verify category, GST, prices, warranty and tracking.
6. Save the product once and add its variant to the receipt.

A barcode is only an identifier; it does not contain product name, GST, warranty or pricing. Supplier/manufacturer data must therefore be imported into `supplier_product_catalog` by a trusted feed or integration. The application does not guess those values or silently create products.

Example catalogue load shape (replace the values with verified supplier data):

```sql
INSERT INTO supplier_product_catalog
 (supplier_id,source_name,barcode,product_name,category_id,brand_id,model,hsn_code,
  unit_of_measure,tracking_type,purchase_price,selling_price,mrp,tax_percentage,
  warranty_months,warranty_provider,supplier_product_code,attributes)
VALUES
 (:supplier_id,:source_name,:barcode,:product_name,:category_id,:brand_id,:model,:hsn_code,
  'PIECE','SERIAL',:purchase_price,:selling_price,:mrp,:gst_rate,
  :warranty_months,'MANUFACTURER',:supplier_product_code,:attributes_json::jsonb);
```

The table is tenant isolated. A supplier-specific barcode entry overrides a manufacturer-level entry for the same scan.

## Stock receiving

The order line keeps the expected quantity. Every receipt separately records good and damaged quantities, serials, batch/lot, manufacturing and expiry dates, pack/UOM, MRP and supplier code. Good quantities increase saleable stock. Damaged quantities create an incoming audit movement followed by a damage movement, so net saleable stock is zero while the physical damaged item remains traceable.

Freight and other landed costs are included in the supplier payable total. Supplier outstanding is calculated from the saved purchase total, supplier payments and supplier returns. It is never entered as an independent balance.

Invoice attachments accept one PDF, JPG or PNG up to 5 MB. The supplier invoice date, goods-received date and order/purchase date remain separate.
