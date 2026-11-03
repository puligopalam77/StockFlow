-- READ ONLY. Run manually as database administrator after migration 047.
-- This script does not change profiles or seed categories.
BEGIN TRANSACTION READ ONLY;
SELECT current_database() AS database_name;
SELECT tenant_id,tenant_code,business_name,business_profile_code,business_profile_version
FROM public.tenants ORDER BY tenant_id;

SELECT conname,convalidated,pg_get_constraintdef(oid) AS definition
FROM pg_constraint WHERE conrelid IN('public.tenants'::regclass,'public.categories'::regclass)
AND conname IN('ck_tenants_business_profile','ck_tenants_business_profile_version','ck_category_business_profile') ORDER BY conname;

SELECT p.proname,p.prosecdef AS security_definer,p.proconfig AS fixed_configuration,
    has_function_privilege('stockflow_runtime',p.oid,'EXECUTE') AS runtime_can_execute
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
WHERE n.nspname='public' AND p.proname IN('sf_business_profile_impact','sf_change_business_profile','sf_profile_catalog_guard')
ORDER BY p.proname;

SELECT tgrelid::regclass AS guarded_table,tgname,tgenabled FROM pg_trigger
WHERE NOT tgisinternal AND tgname='aa_business_profile_guard' ORDER BY tgrelid::regclass::text;

SELECT tenant_id,business_profile_code,count(*) AS category_count
FROM public.categories GROUP BY tenant_id,business_profile_code ORDER BY tenant_id,business_profile_code;

-- Expected: no mismatches for existing or newly registered products.
SELECT p.tenant_id,p.product_id,p.product_name,c.category_id,c.business_profile_code AS category_profile,
    t.business_profile_code AS shop_profile
FROM public.products p JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id
JOIN public.tenants t ON t.tenant_id=p.tenant_id
WHERE c.business_profile_code<>t.business_profile_code ORDER BY p.tenant_id,p.product_id;

-- Verify profile changes made through the UI were audited with a reason.
SELECT tenant_id,user_id,record_id,reason,old_values,new_values,created_at
FROM public.audit_logs WHERE module_name='Business profile' ORDER BY audit_log_id DESC LIMIT 30;
COMMIT;
