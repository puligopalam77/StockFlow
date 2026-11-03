-- OPTIONAL, READ-ONLY. Run manually as migration administrator.
-- Before 022: rows that need review when next edited (nothing is changed here).
SELECT tenant_id,variant_id,sku,warranty_months,minimum_stock_level
FROM product_variants
WHERE length(btrim(sku)) NOT BETWEEN 3 AND 100
   OR warranty_months NOT BETWEEN 0 AND 240
   OR minimum_stock_level NOT BETWEEN 0 AND 999999999;

-- After 022: products and their persisted registration defaults.
SELECT tenant_id,product_id,product_name,description,has_variants,
       CASE WHEN is_serial_tracked THEN 'SERIAL' ELSE 'QUANTITY' END AS tracking_type,
       tax_percentage AS default_tax_percentage,warranty_months AS default_warranty_months,warranty_provider
FROM products ORDER BY tenant_id,product_id;

SELECT tenant_id,variant_id,location_id,minimum_stock_level
FROM variant_location_settings ORDER BY tenant_id,variant_id,location_id;

-- Every product should retain exactly one default variant.
SELECT p.tenant_id,p.product_id,count(v.variant_id) AS default_count
FROM products p LEFT JOIN product_variants v
 ON v.tenant_id=p.tenant_id AND v.product_id=p.product_id AND v.is_default
GROUP BY p.tenant_id,p.product_id HAVING count(v.variant_id)<>1;
