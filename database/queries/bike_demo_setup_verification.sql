-- READ ONLY. Run as database administrator before/after manually applying 050
-- and before/after selecting Bike Accessories, with other writers stopped.
-- Record fingerprints before changing setup. Existing Electrical hashes, all
-- document totals and ledger hashes must remain identical after setup alone.
SELECT tenant_id,business_name,business_profile_code,business_profile_version,
 to_jsonb(t)->>'profile_catalog_switching_enabled' AS retained_catalog_switching
FROM public.tenants t ORDER BY tenant_id;
SELECT p.tenant_id,count(*) AS original_electrical_products,
 md5(string_agg(to_jsonb(p)::text,'|' ORDER BY p.product_id)) AS electrical_product_fingerprint
FROM public.products p JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
WHERE c.business_profile_code='ELECTRICAL' GROUP BY p.tenant_id ORDER BY p.tenant_id;
SELECT v.tenant_id,count(*) AS original_electrical_variants,
 md5(string_agg(to_jsonb(v)::text,'|' ORDER BY v.variant_id)) AS electrical_variant_fingerprint
FROM public.product_variants v JOIN public.products p ON p.product_id=v.product_id AND p.tenant_id=v.tenant_id
JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
WHERE c.business_profile_code='ELECTRICAL' GROUP BY v.tenant_id ORDER BY v.tenant_id;
SELECT tenant_id,count(*) AS movement_count,sum(quantity) AS unchanged_raw_movement_quantity,
 md5(string_agg(to_jsonb(it)::text,'|' ORDER BY inventory_transaction_id)) AS ledger_fingerprint
FROM public.inventory_transactions it GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,count(*) AS invoices,sum(total_amount) AS invoice_total FROM public.sales GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,count(*) AS purchases,sum(total_amount) AS purchase_total FROM public.purchases GROUP BY tenant_id ORDER BY tenant_id;
SELECT tenant_id,business_profile_code,category_name,category_id FROM public.categories
WHERE business_profile_code='BIKE_ACCESSORIES' ORDER BY tenant_id,category_name;
SELECT tenant_id,business_profile_code,version,jsonb_array_length(definition->'attributes') AS attributes,
 jsonb_array_length(definition->'categories') AS category_assignments
FROM public.catalog_attribute_schemas WHERE business_profile_code='BIKE_ACCESSORIES' ORDER BY tenant_id;
SELECT tgrelid::regclass::text AS table_name,tgname,tgenabled
FROM pg_trigger WHERE tgname='z_active_catalog_write' ORDER BY tgrelid::regclass::text;
-- Expect 3 enabled triggers after 050.
