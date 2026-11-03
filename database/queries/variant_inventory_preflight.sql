-- Read-only. Run BEFORE migration 018. Resolve returned collisions before migrating.
SELECT lower(btrim(COALESCE(NULLIF(btrim(sku),''),'LEGACY-'||product_id))) AS normalized_sku,
       array_agg(product_id ORDER BY product_id) AS products
FROM products GROUP BY 1 HAVING count(*)>1;
SELECT lower(btrim(barcode)) AS normalized_barcode,array_agg(product_id ORDER BY product_id) AS products
FROM products WHERE NULLIF(btrim(barcode),'') IS NOT NULL GROUP BY 1 HAVING count(*)>1;
-- Keep these totals for comparison after migration. No prices or history will be recalculated.
SELECT (SELECT count(*) FROM products) AS products,
       (SELECT count(*) FROM purchase_items) AS purchase_lines,
       (SELECT count(*) FROM sale_items) AS sale_lines,
       (SELECT count(*) FROM product_units) AS physical_units,
       (SELECT count(*) FROM inventory_transactions) AS movements,
       (SELECT sum(current_stock) FROM vw_current_stock) AS stock,
       (SELECT sum(total_amount) FROM sales) AS sales_total;
