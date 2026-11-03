-- Read-only verification after manual migration 056.
SELECT to_regprocedure('public.sf_save_category_configuration(integer,jsonb)') AS configuration_function;
SELECT tenant_id,business_profile_code,category_id,category_code,category_name,registration_defaults
FROM public.categories ORDER BY tenant_id,business_profile_code,category_id;
-- Must return no rows.
SELECT tenant_id,category_id FROM public.categories
WHERE public.sf_category_inventory_defaults_valid(registration_defaults) IS DISTINCT FROM true;
SELECT tenant_id,category_id FROM public.categories WHERE business_profile_code<>'ELECTRICAL'
AND (registration_defaults->>'trackingMethod'='SERIAL' OR (registration_defaults->>'enableWarranty')::boolean);
-- Direct assignments stay in the existing reusable schema; inherited rules are derived.
SELECT tenant_id,business_profile_code,version,jsonb_array_length(definition->'attributes') AS reusable_definitions,
jsonb_array_length(definition->'categories') AS direct_category_assignments FROM public.catalog_attribute_schemas;
