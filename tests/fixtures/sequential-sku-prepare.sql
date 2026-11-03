\set ON_ERROR_STOP on
SELECT set_config('app.tenant_id','1',false),set_config('app.user_id','1',false),set_config('app.business_profile_code','BIKE_ACCESSORIES',false);
INSERT INTO categories(tenant_id,business_profile_code,category_name) SELECT 1,'BIKE_ACCESSORIES','Synthetic initialization fixture' WHERE NOT EXISTS(SELECT 1 FROM categories WHERE category_name='Synthetic initialization fixture');
INSERT INTO products(product_name,category_id,has_variants,parent_product_code,sku) SELECT 'Legacy sequential fixture',category_id,true,'SYN-0006','SYN-0006-02' FROM categories WHERE category_name='Synthetic initialization fixture' AND NOT EXISTS(SELECT 1 FROM products WHERE product_name='Legacy sequential fixture');
INSERT INTO products(product_name,category_id,parent_product_code,sku) SELECT 'Legacy STD preservation fixture',category_id,'LEGACY-FAMILY','LEGACY-FAMILY-STD' FROM categories WHERE category_name='Synthetic initialization fixture';
CREATE TABLE sku067_baseline AS SELECT 'products' tab,product_id::bigint id,to_jsonb(p) value FROM products p
 UNION ALL SELECT 'product_variants',variant_id,to_jsonb(v) FROM product_variants v
 UNION ALL SELECT 'categories',category_id,to_jsonb(c) FROM categories c
 UNION ALL SELECT 'inventory_transactions',inventory_transaction_id,to_jsonb(i) FROM inventory_transactions i
 UNION ALL SELECT 'variant_barcodes',variant_barcode_id,to_jsonb(b) FROM variant_barcodes b
 UNION ALL SELECT 'purchases',0,to_jsonb(t) FROM purchases t
 UNION ALL SELECT 'purchase_items',0,to_jsonb(t) FROM purchase_items t
 UNION ALL SELECT 'sales',0,to_jsonb(t) FROM sales t
 UNION ALL SELECT 'sale_items',0,to_jsonb(t) FROM sale_items t
 UNION ALL SELECT 'product_units',0,to_jsonb(t) FROM product_units t
 UNION ALL SELECT 'variant_supplier_identifiers',0,to_jsonb(t) FROM variant_supplier_identifiers t;

