-- OPTIONAL, READ-ONLY: electronic product registration readiness and catalog review.
-- No migration is required for this UI change. Existing migrations through 027 are expected.
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema='public' AND (
 (table_name='products' AND column_name IN ('has_variants','is_serial_tracked','warranty_months','warranty_provider')) OR
 (table_name='product_variants' AND column_name IN ('attributes','sku','is_serial_tracked','purchase_price','selling_price','warranty_months')))
ORDER BY table_name,column_name;

SELECT p.tenant_id,p.product_id,p.product_name,p.model,p.has_variants,
 p.is_serial_tracked AS product_serial_tracking,p.warranty_provider,
 v.variant_id,v.variant_name,v.sku,v.is_serial_tracked,v.attributes,
 v.purchase_price,v.selling_price,v.warranty_months,v.is_active
FROM products p JOIN product_variants v ON v.tenant_id=p.tenant_id AND v.product_id=p.product_id
ORDER BY p.tenant_id,p.product_name,v.variant_id;
