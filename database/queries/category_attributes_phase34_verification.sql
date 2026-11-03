-- READ ONLY. Run as database administrator after manually applying 048.
SELECT to_regclass('public.catalog_attribute_schemas') AS schema_table,
       to_regprocedure('public.sf_save_catalog_schema(jsonb,bigint)') AS save_function,
       to_regprocedure('public.sf_catalog_locked_categories()') AS locked_categories_function;

SELECT c.relname,c.relrowsecurity,p.policyname,p.cmd,p.roles,p.qual
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
LEFT JOIN pg_policies p ON p.schemaname=n.nspname AND p.tablename=c.relname
WHERE n.nspname='public' AND c.relname='catalog_attribute_schemas';

SELECT tgrelid::regclass::text AS table_name,tgname,tgdeferrable,tginitdeferred,tgenabled
FROM pg_trigger WHERE tgname='category_attributes_validate' ORDER BY tgrelid::regclass::text;
-- Expect two enabled, deferrable, initially deferred triggers.

SELECT grantee,privilege_type FROM information_schema.role_table_grants
WHERE table_schema='public' AND table_name='catalog_attribute_schemas'
ORDER BY grantee,privilege_type;
-- stockflow_runtime must have SELECT only; changes use the permission-checked function.

SELECT s.tenant_id,t.business_name,s.business_profile_code,s.version,s.updated_at,
 jsonb_array_length(s.definition->'attributes') AS reusable_attributes,
 jsonb_array_length(s.definition->'categories') AS assigned_categories
FROM public.catalog_attribute_schemas s JOIN public.tenants t USING(tenant_id)
ORDER BY s.tenant_id,s.business_profile_code;

SELECT s.tenant_id,s.business_profile_code,c.category_id,c.category_name,
 public.sf_category_attribute_config(s.definition,c.category_id) AS effective_fields,
 (SELECT count(*) FROM public.products p WHERE p.tenant_id=c.tenant_id AND p.category_id=c.category_id) AS products_including_inactive
FROM public.catalog_attribute_schemas s JOIN public.categories c
 ON c.tenant_id=s.tenant_id AND c.business_profile_code=s.business_profile_code
WHERE public.sf_category_attribute_config(s.definition,c.category_id)<>'[]'::jsonb
ORDER BY s.tenant_id,c.category_id;

-- Expect zero rows: configured variants missing required attributes.
SELECT p.tenant_id,p.product_id,v.variant_id,c.category_name,f->'attribute'->>'label' AS missing_field
FROM public.products p JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id
JOIN public.catalog_attribute_schemas s ON s.tenant_id=p.tenant_id AND s.business_profile_code=c.business_profile_code
JOIN public.product_variants v ON v.tenant_id=p.tenant_id AND v.product_id=p.product_id
CROSS JOIN LATERAL jsonb_array_elements(public.sf_category_attribute_config(s.definition,p.category_id)) f
WHERE (f->'rule'->>'required')::boolean AND btrim(COALESCE(v.attributes->>(f->'rule'->>'code'),''))='';

SELECT tenant_id,user_id,action_name,table_name,old_values,new_values
FROM public.audit_logs WHERE module_name='Category attributes'
ORDER BY audit_log_id DESC LIMIT 20;
