-- Read-only checks after manually applying 057. No application data is modified.
SELECT column_name,is_generated,column_default
FROM information_schema.columns
WHERE table_schema='public' AND table_name='categories' AND column_name IN('category_code','display_order');
SELECT tenant_id,category_id,category_code,category_name,parent_category_id,display_order
FROM public.categories ORDER BY tenant_id,display_order,category_id;
SELECT tenant_id,lower(category_code),count(*) FROM public.categories
GROUP BY tenant_id,lower(category_code) HAVING count(*)>1;
-- In the UI, verify root -> children filtering, third-level navigation, POST for
-- Add Subcategory, PUT for existing selections, and unique custom code rejection.
