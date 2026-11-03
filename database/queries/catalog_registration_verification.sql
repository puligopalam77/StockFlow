-- MANUAL READ-ONLY: run after 052 as PostgreSQL superuser.
-- Compare original-data fingerprints before/after with profile_data_preservation_snapshot.sql.
SELECT tenant_id,business_profile_code,catalog_registration_enhancements_enabled FROM public.tenants ORDER BY tenant_id;

-- Should return zero inconsistent statuses. NULL is the preserved legacy representation.
SELECT product_id,tenant_id,product_status,is_active FROM public.products
 WHERE product_status IS NOT NULL AND (product_status='ACTIVE') IS DISTINCT FROM is_active;

SELECT tenant_id,brand_id,brand_name,aliases FROM public.brands ORDER BY tenant_id,brand_id;
SELECT i.tenant_id,i.variant_id,v.sku,s.supplier_code,s.supplier_name,i.supplier_sku
 FROM public.variant_supplier_identifiers i JOIN public.product_variants v USING(tenant_id,variant_id)
 JOIN public.suppliers s USING(tenant_id,supplier_id) ORDER BY i.tenant_id,v.sku,s.supplier_code;

-- Verify category rules and defaults without changing any settings.
SELECT s.tenant_id,s.business_profile_code,s.version,c.category_name,
 a->>'label' attribute,a->>'type' type,f->>'required' required,f->>'variantAxis' creates_variants,
 COALESCE(f->>'visible','true') visible,COALESCE(f->>'searchable','true') searchable,f->>'defaultValue' default_value
 FROM public.catalog_attribute_schemas s CROSS JOIN LATERAL jsonb_array_elements(s.definition->'categories') cfg
 JOIN public.categories c ON c.tenant_id=s.tenant_id AND c.category_id=(cfg->>'categoryId')::integer
 CROSS JOIN LATERAL jsonb_array_elements(cfg->'fields') f
 JOIN LATERAL jsonb_array_elements(s.definition->'attributes') a ON a->>'code'=f->>'code'
 ORDER BY s.tenant_id,s.business_profile_code,c.category_name,a->>'code';

SELECT n.nspname,c.relname,t.tgname,t.tgenabled FROM pg_trigger t
 JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname='public' AND t.tgname IN('aa_brand_alias_guard','aa_used_category_parent_guard',
  'aa_product_status_guard','aa_duplicate_product_guard','aa_catalog_default_values','ab_manufacturer_part_guard') ORDER BY c.relname,t.tgname;

SELECT tablename,policyname,permissive,roles,qual,with_check FROM pg_policies
 WHERE schemaname='public' AND tablename='variant_supplier_identifiers';

-- Review pre-existing duplicates manually. 052 never merges/deletes historical catalog rows.
SELECT p.tenant_id,p.category_id,p.brand_id,lower(regexp_replace(btrim(p.product_name),'\s+',' ','g')) normalized_name,
 lower(btrim(COALESCE(p.model,''))) product_model,count(*) FROM public.products p
 GROUP BY p.tenant_id,p.category_id,p.brand_id,normalized_name,product_model HAVING count(*)>1;
