-- Read-only. Run as an authorized database administrator before 065.
BEGIN READ ONLY;
WITH roots AS (
 SELECT r.* FROM public.categories r WHERE r.parent_category_id IS NULL AND NOT COALESCE((to_jsonb(r)->>'is_retired_grouping')::boolean,false)
 AND EXISTS(SELECT 1 FROM public.categories c JOIN public.categories g ON g.tenant_id=c.tenant_id AND g.parent_category_id=c.category_id WHERE c.tenant_id=r.tenant_id AND c.parent_category_id=r.category_id)
), promoted AS (SELECT c.*,r.category_id old_root FROM public.categories c JOIN roots r ON r.tenant_id=c.tenant_id AND c.parent_category_id=r.category_id), future AS (
 SELECT c.*,CASE WHEN p.category_id IS NOT NULL THEN NULL ELSE c.parent_category_id END new_parent,
 lower(regexp_replace(btrim(normalize(c.category_name,NFKC)),'[[:space:]]+',' ','g')) name_key
 FROM public.categories c LEFT JOIN promoted p USING(tenant_id,category_id)
), duplicates AS (SELECT tenant_id,business_profile_code,new_parent,name_key,array_agg(category_id) ids FROM future GROUP BY 1,2,3,4 HAVING count(*)>1)
SELECT jsonb_build_object('promotions',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',tenant_id,'profile',business_profile_code,'category_id',category_id,'code',category_code,'name',category_name,'old_parent_id',old_root,'new_parent_id',NULL)) FROM promoted),'[]'::jsonb),
 'root_review',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',r.tenant_id,'category_id',r.category_id,'name',r.category_name,
 'direct_products',(SELECT count(*) FROM public.products p WHERE p.tenant_id=r.tenant_id AND p.category_id=r.category_id),
 'brand_mappings',(SELECT count(*) FROM public.brand_categories b WHERE b.tenant_id=r.tenant_id AND b.category_id=r.category_id),
 'supplier_mappings',(SELECT count(*) FROM public.supplier_product_catalog s WHERE s.tenant_id=r.tenant_id AND s.category_id=r.category_id),
 'configuration_entries',(SELECT count(*) FROM public.catalog_attribute_schemas s CROSS JOIN LATERAL jsonb_array_elements(s.definition->'categories') e WHERE s.tenant_id=r.tenant_id AND s.business_profile_code=r.business_profile_code AND (e->>'categoryId')::integer=r.category_id),
 'inherited_fields',(SELECT COALESCE(sum(jsonb_array_length(e->'fields')),0) FROM public.catalog_attribute_schemas s CROSS JOIN LATERAL jsonb_array_elements(s.definition->'categories') e WHERE s.tenant_id=r.tenant_id AND s.business_profile_code=r.business_profile_code AND (e->>'categoryId')::integer=r.category_id),
 'inventory_defaults',to_jsonb(r)->'registration_defaults',
 'inactive_root_with_active_children',NOT r.is_active AND EXISTS(SELECT 1 FROM promoted p WHERE p.old_root=r.category_id AND p.tenant_id=r.tenant_id AND p.is_active))) FROM roots r),'[]'::jsonb),
 'duplicate_conflicts',COALESCE((SELECT jsonb_agg(to_jsonb(d)) FROM duplicates d),'[]'::jsonb),
 'before_after',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',f.tenant_id,'profile',f.business_profile_code,'category_id',f.category_id,'code',f.category_code,'before_parent',f.parent_category_id,'after_parent',f.new_parent,
 'before_path',concat_ws(' / ',gp.category_name,p.category_name,f.category_name),'after_path',concat_ws(' / ',ngp.category_name,np.category_name,f.category_name),'retire_grouping',EXISTS(SELECT 1 FROM roots r WHERE r.tenant_id=f.tenant_id AND r.category_id=f.category_id)) ORDER BY f.tenant_id,f.category_id)
 FROM future f LEFT JOIN public.categories p ON p.tenant_id=f.tenant_id AND p.category_id=f.parent_category_id LEFT JOIN public.categories gp ON gp.tenant_id=p.tenant_id AND gp.category_id=p.parent_category_id LEFT JOIN future np ON np.tenant_id=f.tenant_id AND np.category_id=f.new_parent LEFT JOIN future ngp ON ngp.tenant_id=np.tenant_id AND ngp.category_id=np.new_parent),'[]'::jsonb),
 'foreign_keys',(SELECT jsonb_agg(jsonb_build_object('table',conrelid::regclass::text,'definition',pg_get_constraintdef(oid))) FROM pg_constraint WHERE confrelid='public.categories'::regclass),
 'hierarchy_conflicts',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',c.tenant_id,'category_id',c.category_id,'parent_id',c.new_parent)) FROM future c JOIN future p ON p.tenant_id=c.tenant_id AND p.category_id=c.new_parent WHERE p.new_parent IS NOT NULL),'[]'::jsonb),
 'orphan_conflicts',COALESCE((SELECT jsonb_agg(jsonb_build_object('tenant_id',c.tenant_id,'category_id',c.category_id)) FROM future c LEFT JOIN future p ON p.tenant_id=c.tenant_id AND p.category_id=c.new_parent AND p.business_profile_code=c.business_profile_code WHERE c.new_parent IS NOT NULL AND p.category_id IS NULL),'[]'::jsonb),
 'category_count',(SELECT count(*) FROM public.categories),'product_count',(SELECT count(*) FROM public.products)) AS migration_preview;
ROLLBACK;
