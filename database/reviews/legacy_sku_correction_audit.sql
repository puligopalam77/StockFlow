-- READ ONLY. Run after 069 as PostgreSQL superuser to see every location/shop.
-- This is an audit, NOT a migration or an authorization to rename identifiers.
-- Includes all shops separately; filter tenant_id/business_profile_code in the final SELECT if needed.
-- Does not allocate codes, advance counters, disable guards or modify any record.
-- Physical labels outside StockFlow cannot be detected by this query.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
WITH catalog AS (
 SELECT p.tenant_id,c.business_profile_code,p.product_id,p.product_name,p.category_id,
        c.category_name,c.sku_prefix,p.has_variants,p.parent_product_code,
        v.variant_id,v.variant_name,v.sku AS old_sku,v.is_active,
        r.last_number AS current_prefix_counter,
        COALESCE(
          p.parent_product_code ~ ('^'||c.sku_prefix||'-[0-9]{4,}$') AND
          CASE WHEN p.has_variants THEN
             left(v.sku,length(p.parent_product_code)+1)=p.parent_product_code||'-'
             AND substring(v.sku FROM length(p.parent_product_code)+2) ~ '^[0-9]{2,}$'
          ELSE v.sku=p.parent_product_code END,false) AS follows_current_format
 FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 LEFT JOIN public.product_variants v USING(tenant_id,product_id)
 LEFT JOIN public.catalog_prefix_registry r
   ON r.tenant_id=p.tenant_id AND r.profile=c.business_profile_code AND r.prefix=c.sku_prefix
), review AS (
 SELECT catalog.*,
   EXISTS(SELECT 1 FROM public.inventory_transactions i WHERE i.tenant_id=catalog.tenant_id AND i.product_id=catalog.product_id)
   OR EXISTS(SELECT 1 FROM public.purchase_items i WHERE i.tenant_id=catalog.tenant_id AND i.product_id=catalog.product_id)
   OR EXISTS(SELECT 1 FROM public.sale_items i WHERE i.tenant_id=catalog.tenant_id AND i.product_id=catalog.product_id)
   OR EXISTS(SELECT 1 FROM public.product_units i WHERE i.tenant_id=catalog.tenant_id AND i.product_id=catalog.product_id)
   OR EXISTS(SELECT 1 FROM public.stock_count_items i WHERE i.tenant_id=catalog.tenant_id AND i.product_id=catalog.product_id)
   OR EXISTS(SELECT 1 FROM public.stock_transfer_items i JOIN public.product_variants v USING(tenant_id,variant_id)
      WHERE v.tenant_id=catalog.tenant_id AND v.product_id=catalog.product_id) AS family_has_history,
   (SELECT count(*) FROM public.variant_barcodes b
      WHERE b.tenant_id=catalog.tenant_id AND b.variant_id=catalog.variant_id) AS barcode_mapping_count,
   (SELECT count(*) FROM public.stock_item_labels l
      WHERE l.tenant_id=catalog.tenant_id AND l.variant_id=catalog.variant_id) AS stock_label_count,
   (SELECT count(*) FROM public.purchase_item_labels l
      JOIN public.purchase_receipt_items ri USING(tenant_id,receipt_item_id)
      JOIN public.purchase_items pi USING(tenant_id,purchase_item_id)
      WHERE pi.tenant_id=catalog.tenant_id AND pi.variant_id=catalog.variant_id) AS receipt_label_count
 FROM catalog
)
SELECT *,CASE
 WHEN follows_current_format THEN 'ALREADY_CURRENT_FORMAT'
 WHEN variant_id IS NULL THEN 'MISSING_VARIANT_REQUIRES_MANUAL_REVIEW'
 WHEN family_has_history THEN 'KEEP_IDENTIFIERS_REVIEW_DEMO_COPY_OPTION'
 ELSE 'NO_RECORDED_HISTORY_STILL_REVIEW_BARCODES_AND_PHYSICAL_LABELS'
 END AS review_result
FROM review
ORDER BY tenant_id,business_profile_code,product_id,variant_id;
COMMIT;
