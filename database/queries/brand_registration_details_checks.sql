-- Read-only acceptance checks after manually applying 058.
SELECT to_regprocedure('public.sf_brand_registration_details()') AS details_capability;
SELECT tenant_id,brand_id,brand_code,brand_name,official_website,
 brand_logo IS NOT NULL AS has_logo,is_active,created_at,updated_at FROM public.brands ORDER BY tenant_id,brand_id;
SELECT tenant_id,lower(brand_code),count(*) FROM public.brands GROUP BY tenant_id,lower(brand_code) HAVING count(*)>1;
-- Existing cross-profile duplicates are reported, never merged by migration 058.
SELECT tenant_id,lower(regexp_replace(btrim(brand_name),'\s+',' ','g')),count(*)
FROM public.brands GROUP BY tenant_id,lower(regexp_replace(btrim(brand_name),'\s+',' ','g')) HAVING count(*)>1;
SELECT tenant_id,brand_id,category_id FROM public.brand_categories ORDER BY tenant_id,brand_id,category_id;
-- As runtime users in two different shops, verify that list/detail/logo endpoints
-- and product registration expose only their authorized brands and category links.
