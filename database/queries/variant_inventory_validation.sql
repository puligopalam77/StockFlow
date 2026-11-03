-- Read-only. Run AFTER migrations 018 and 019.
SELECT (SELECT count(*) FROM products) AS products,
       (SELECT count(*) FROM purchase_items) AS purchase_lines,
       (SELECT count(*) FROM sale_items) AS sale_lines,
       (SELECT count(*) FROM product_units) AS physical_units,
       (SELECT count(*) FROM inventory_transactions) AS movements,
       (SELECT sum(current_stock) FROM vw_current_stock) AS stock,
       (SELECT sum(total_amount) FROM sales) AS sales_total;
-- All following result sets should be empty (legacy discrepancies need review).
SELECT p.product_id FROM products p WHERE NOT EXISTS(
    SELECT 1 FROM product_variants v WHERE v.product_id=p.product_id AND v.is_default);
SELECT v.variant_id,v.product_id,s.current_stock,
       (SELECT count(*) FROM product_units u WHERE u.variant_id=v.variant_id AND u.unit_status='IN_STOCK') AS available_units
FROM product_variants v JOIN vw_variant_current_stock s USING(variant_id)
WHERE s.current_stock<0 OR (v.is_serial_tracked AND s.current_stock<>
       (SELECT count(*) FROM product_units u WHERE u.variant_id=v.variant_id AND u.unit_status='IN_STOCK'));
SELECT product_id,current_stock FROM vw_current_stock p WHERE current_stock<>
    (SELECT sum(current_stock) FROM vw_variant_current_stock v WHERE v.product_id=p.product_id);
WITH identifiers AS (
    SELECT lower(btrim(barcode)) AS code,'VARIANT:'||variant_id AS target FROM variant_barcodes WHERE is_active
    UNION ALL SELECT lower(btrim(serial_number)),'UNIT:'||product_unit_id FROM product_units
    UNION ALL SELECT lower(btrim(barcode)),'UNIT:'||product_unit_id FROM product_units
    UNION ALL SELECT lower(btrim(manufacturer_barcode)),'UNIT:'||product_unit_id FROM product_units
    UNION ALL SELECT lower(btrim(internal_barcode)),'UNIT:'||product_unit_id FROM product_units
)
SELECT code,array_agg(DISTINCT target) AS conflicting_targets FROM identifiers
WHERE code IS NOT NULL AND code<>'' GROUP BY code HAVING count(DISTINCT target)>1;
SELECT i.purchase_item_id,i.quantity,i.received_quantity FROM purchase_items i JOIN purchases p USING(purchase_id)
WHERE (p.status='COMPLETED' AND i.received_quantity<>i.quantity) OR i.received_quantity>i.quantity;
