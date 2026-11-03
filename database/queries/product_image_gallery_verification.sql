-- Read-only verification after manual migration 053. Contains no image bytes.
SELECT column_name,data_type FROM information_schema.columns
WHERE table_schema='public' AND (table_name='products' AND column_name='product_images'
 OR table_name='tenants' AND column_name='product_image_gallery_enabled');
SELECT tenant_id,product_image_gallery_enabled FROM public.tenants ORDER BY tenant_id;
SELECT tenant_id,product_id,product_name,jsonb_array_length(product_images) AS image_count,
 product_images->0->>'fileName' AS main_image,
 product_image_name AS compatibility_main_image,
 public.sf_product_gallery_valid(product_images) AS gallery_valid
FROM public.products ORDER BY tenant_id,product_id;
