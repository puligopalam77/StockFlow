-- Read-only preflight. Does not create products, SKUs, barcodes, stock or vehicle links.
BEGIN READ ONLY;
SELECT jsonb_build_object(
 'migration_065_installed',EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='is_retired_grouping'),
 'product_count',(SELECT count(*) FROM public.products),
 'variant_count',(SELECT count(*) FROM public.product_variants),
 'products_without_sku',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',p.tenant_id,'product_id',p.product_id)) FROM public.products p WHERE NOT EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=p.tenant_id AND v.product_id=p.product_id)),'[]'::jsonb),
 'products_without_family_code',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',p.tenant_id,'product_id',p.product_id)) FROM public.products p WHERE NULLIF(btrim(to_jsonb(p)->>'parent_product_code'),'') IS NULL),'[]'::jsonb),
 'legacy_family_code_equals_sku',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',p.tenant_id,'product_id',p.product_id,'variant_id',v.variant_id)) FROM public.products p JOIN public.product_variants v USING(tenant_id,product_id) WHERE lower(btrim(to_jsonb(p)->>'parent_product_code'))=lower(btrim(v.sku))),'[]'::jsonb),
 'sku_conflicts',COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT tenant_id,lower(btrim(sku)) sku,array_agg(variant_id) variants FROM public.product_variants GROUP BY 1,2 HAVING count(*)>1) x),'[]'::jsonb),
 'barcode_conflicts',COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT tenant_id,lower(btrim(barcode)) barcode,array_agg(DISTINCT variant_id) variants FROM public.variant_barcodes WHERE is_active GROUP BY 1,2 HAVING count(DISTINCT variant_id)>1) x),'[]'::jsonb),
 'historical_rows',jsonb_build_object('inventory_transactions',(SELECT count(*) FROM public.inventory_transactions),'purchase_items',(SELECT count(*) FROM public.purchase_items),'product_units',(SELECT count(*) FROM public.product_units)),
 'identity_fingerprint',(SELECT md5(COALESCE(string_agg(concat_ws(':',tenant_id,product_id,variant_id,sku,is_default),';' ORDER BY tenant_id,variant_id),'')) FROM public.product_variants),
 'variant_values_fingerprint',(SELECT md5(COALESCE(string_agg(md5((to_jsonb(v)-'use_default_purchase_price'-'use_default_selling_price')::text),'' ORDER BY tenant_id,variant_id),'')) FROM public.product_variants v),
 'product_values_fingerprint',(SELECT md5(COALESCE(string_agg(md5((to_jsonb(p)-'registration_defaults'-'registration_request_id'-'registration_request_hash')::text),'' ORDER BY tenant_id,product_id),'')) FROM public.products p),
 'stock_ledger_fingerprint',(SELECT md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'' ORDER BY md5(to_jsonb(t)::text)),'')) FROM public.inventory_transactions t)
) AS registration_preview;
ROLLBACK;

