-- Read-only inspection after manual migration 055. No catalog or schema writes.
SELECT tenant_id,business_profile_code,category_id,category_code,category_name,parent_category_id,inherit_parent_fields,is_active
FROM public.categories ORDER BY tenant_id,business_profile_code,parent_category_id NULLS FIRST,category_name;
-- Existing populated categories should be listed with inheritance disabled;
-- new populated categories created after 055 can have inheritance enabled.
SELECT c.tenant_id,c.category_id,c.category_name,c.inherit_parent_fields,count(p.product_id) AS products
FROM public.categories c JOIN public.products p USING(tenant_id,category_id)
GROUP BY c.tenant_id,c.category_id,c.category_name,c.inherit_parent_fields ORDER BY c.tenant_id,c.category_id;
-- These must return no rows.
SELECT c.tenant_id,c.category_id,c.parent_category_id FROM public.categories c
JOIN public.categories p ON p.tenant_id=c.tenant_id AND p.category_id=c.parent_category_id
WHERE p.business_profile_code<>c.business_profile_code OR c.category_id=c.parent_category_id;
SELECT tenant_id,business_profile_code,parent_category_id,lower(btrim(category_name)),count(*)
FROM public.categories GROUP BY tenant_id,business_profile_code,parent_category_id,lower(btrim(category_name)) HAVING count(*)>1;
