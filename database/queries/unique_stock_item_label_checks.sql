-- Read-only checks after manual migration 054; does not allocate or print labels.
SELECT to_regclass('public.stock_item_labels') AS item_label_table;
SELECT c.relrowsecurity,c.relforcerowsecurity FROM pg_class c WHERE c.oid='public.stock_item_labels'::regclass;
-- to_jsonb keeps this diagnostic compatible with both 054 and 059 layouts.
SELECT tenant_id,location_id,intake_item_id,to_jsonb(l)->>'variant_id' AS variant_id,item_number,count(*) FROM public.stock_item_labels l
GROUP BY tenant_id,location_id,intake_item_id,to_jsonb(l)->>'variant_id',item_number HAVING count(*)>1;
SELECT l.label_id,l.barcode,i.intake_id,i.variant_id,l.item_number,i.quantity
FROM public.stock_item_labels l JOIN public.stock_intake_items i USING(tenant_id,location_id,intake_item_id)
WHERE l.item_number>i.quantity OR cardinality(i.serial_numbers)>0;
SELECT tenant_id,location_id,intake_item_id,count(*) AS saved_labels
FROM public.stock_item_labels GROUP BY tenant_id,location_id,intake_item_id ORDER BY intake_item_id;
