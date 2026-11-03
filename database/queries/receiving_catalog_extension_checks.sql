-- Run after migration 030 while connected to the intended StockFlow database.
SELECT column_name,data_type FROM information_schema.columns
WHERE table_schema='public' AND table_name IN('products','product_variants','purchases','purchase_items','purchase_receipt_items','supplier_product_catalog')
 AND column_name IN('hsn_code','unit_of_measure','product_image_url','product_image_data','mrp','pack_quantity','supplier_product_code','supplier_invoice_date','freight_amount','other_landed_cost','damaged_quantity','batch_number','manufacturing_date','expiry_date')
ORDER BY table_name,column_name;

-- Must return zero rows: resolved quantities may never exceed the expected order quantity.
SELECT purchase_item_id,quantity,received_quantity,damaged_quantity
FROM purchase_items WHERE received_quantity+damaged_quantity>quantity;

-- Supplier outstanding is derived from purchases, payments and returns.
SELECT p.purchase_number,p.total_amount,
 coalesce((SELECT sum(sp.amount) FROM supplier_payments sp WHERE sp.purchase_id=p.purchase_id),0) paid,
 coalesce((SELECT sum(pr.return_total) FROM purchase_returns pr WHERE pr.purchase_id=p.purchase_id),0) returned,
 greatest(0,p.total_amount-coalesce((SELECT sum(sp.amount) FROM supplier_payments sp WHERE sp.purchase_id=p.purchase_id),0)
  -coalesce((SELECT sum(pr.return_total) FROM purchase_returns pr WHERE pr.purchase_id=p.purchase_id),0)) outstanding
FROM purchases p ORDER BY p.purchase_id DESC LIMIT 20;

-- Catalogue entries used to prefill unknown scans. Empty is valid until supplier/manufacturer data is imported.
SELECT catalog_product_id,source_name,supplier_id,barcode,product_name,model,is_active
FROM supplier_product_catalog ORDER BY catalog_product_id DESC LIMIT 20;
