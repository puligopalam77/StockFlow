-- READ ONLY. Run as database administrator after manually applying 049.
SELECT table_name,column_name,data_type FROM information_schema.columns
WHERE table_schema='public' AND table_name IN('sale_items','purchase_items')
 AND column_name IN('variant_attributes_snapshot','sku_snapshot','unit_of_measure_snapshot','product_name_snapshot')
ORDER BY table_name,column_name;
SELECT tgrelid::regclass::text AS table_name,tgname,tgenabled,pg_get_triggerdef(oid) AS definition
FROM pg_trigger WHERE tgname='zz_retail_variant_snapshot' ORDER BY tgrelid::regclass::text;

-- Export these counts/totals BEFORE 049 and compare after, while writers are stopped.
-- These base-table queries also work before 049. They never calculate or post stock.
SELECT tenant_id,count(*) AS products FROM public.products GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,count(*) AS variants FROM public.product_variants GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,count(*) AS sales,sum(total_amount) AS billed FROM public.sales GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,count(*) AS purchases,sum(total_amount) AS purchased FROM public.purchases GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,location_id,variant_id,transaction_type,count(*) AS movements,sum(quantity) AS quantity
FROM public.inventory_transactions GROUP BY tenant_id,location_id,variant_id,transaction_type
ORDER BY tenant_id,location_id,variant_id,transaction_type;

-- New documents have non-null SKU snapshots. Old rows remain null; no backfill.
SELECT s.tenant_id,s.invoice_number,i.sale_item_id,i.variant_id,i.sku_snapshot,
 i.unit_of_measure_snapshot,i.variant_attributes_snapshot
FROM public.sale_items i JOIN public.sales s ON s.sale_id=i.sale_id AND s.tenant_id=i.tenant_id
WHERE i.sku_snapshot IS NOT NULL ORDER BY i.sale_item_id DESC LIMIT 30;
SELECT p.tenant_id,p.purchase_number,i.purchase_item_id,i.variant_id,i.sku_snapshot,
 i.product_name_snapshot,i.unit_of_measure,i.variant_attributes_snapshot
FROM public.purchase_items i JOIN public.purchases p ON p.purchase_id=i.purchase_id AND p.tenant_id=i.tenant_id
WHERE i.sku_snapshot IS NOT NULL ORDER BY i.purchase_item_id DESC LIMIT 30;

-- Expect zero malformed snapshots and zero cross-product/tenant document links.
SELECT 'sale' AS source,i.sale_item_id AS item_id FROM public.sale_items i
LEFT JOIN public.product_variants v ON v.variant_id=i.variant_id AND v.product_id=i.product_id AND v.tenant_id=i.tenant_id
WHERE v.variant_id IS NULL OR jsonb_typeof(i.variant_attributes_snapshot)<>'object'
UNION ALL
SELECT 'purchase',i.purchase_item_id FROM public.purchase_items i
LEFT JOIN public.product_variants v ON v.variant_id=i.variant_id AND v.product_id=i.product_id AND v.tenant_id=i.tenant_id
WHERE v.variant_id IS NULL OR jsonb_typeof(i.variant_attributes_snapshot)<>'object';
