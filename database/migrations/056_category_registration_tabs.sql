-- MANUAL ONLY. Back up and stop application/workers. Apply after 055 as PostgreSQL superuser.
-- Existing category identities, schemas, product values, inventory and history are retained.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 056 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='inherit_parent_fields') THEN RAISE EXCEPTION 'Apply 055 first'; END IF;
 IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='registration_defaults') THEN RAISE EXCEPTION '056 is already installed'; END IF;
END $$;
CREATE FUNCTION public.sf_category_inventory_defaults_valid(doc jsonb) RETURNS boolean
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT doc IS NULL OR (jsonb_typeof(doc)='object'
 AND doc->>'stockUnit' IN('PIECE','PAIR','SET','BOX') AND doc->>'trackingMethod' IN('QUANTITY','SERIAL')
 AND jsonb_typeof(doc->'reorderLevel')='number' AND (doc->>'reorderLevel')::numeric BETWEEN 0 AND 999999999 AND (doc->>'reorderLevel')::numeric=trunc((doc->>'reorderLevel')::numeric)
 AND NOT COALESCE((doc->>'enableExpiryTracking')::boolean,false)
 AND NOT EXISTS(SELECT 1 FROM unnest(ARRAY['allowProductOverride','generateSkuAutomatically','suggestBarcodeGeneration','enableVehicleCompatibility','enableWarranty','requireManufacturerPartNumber','enableExpiryTracking']) k WHERE jsonb_typeof(doc->k) IS DISTINCT FROM 'boolean'))
$$;
ALTER TABLE public.categories ADD COLUMN registration_defaults jsonb;
ALTER TABLE public.categories ADD CONSTRAINT ck_category_inventory_defaults CHECK(public.sf_category_inventory_defaults_valid(registration_defaults) IS TRUE);
REVOKE ALL ON FUNCTION public.sf_category_inventory_defaults_valid(jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_category_inventory_defaults_valid(jsonb) TO stockflow_runtime;

CREATE FUNCTION public.sf_save_category_configuration(cat integer,configuration jsonb) RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); profile text; doc jsonb; previous jsonb; defaults jsonb:=configuration->'inventory'; a jsonb; rules jsonb; schema_version bigint; next_version bigint;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 SELECT business_profile_code INTO profile FROM public.tenants WHERE tenant_id=tid;
 IF NOT EXISTS(SELECT 1 FROM public.categories WHERE tenant_id=tid AND category_id=cat AND business_profile_code=profile) THEN RAISE EXCEPTION 'Choose a category in the current setup' USING ERRCODE='23514'; END IF;
 IF public.sf_category_inventory_defaults_valid(defaults) IS DISTINCT FROM true OR defaults IS NULL THEN RAISE EXCEPTION 'Invalid inventory defaults' USING ERRCODE='23514'; END IF;
 IF profile<>'ELECTRICAL' AND (defaults->>'trackingMethod'='SERIAL' OR (defaults->>'enableWarranty')::boolean) THEN RAISE EXCEPTION 'Serial tracking and warranty require the Electrical setup' USING ERRCODE='23514'; END IF;
 IF jsonb_typeof(configuration->'attributes') IS DISTINCT FROM 'array' OR jsonb_typeof(configuration->'fields') IS DISTINCT FROM 'array'
 OR jsonb_array_length(configuration->'attributes')>30 OR jsonb_array_length(configuration->'fields')>30 THEN RAISE EXCEPTION 'Invalid category configuration' USING ERRCODE='23514'; END IF;
 IF (SELECT count(*)<>count(DISTINCT x->>'code') FROM jsonb_array_elements(configuration->'attributes') x) THEN RAISE EXCEPTION 'Duplicate reusable field code' USING ERRCODE='23514'; END IF;
 SELECT s.definition,s.version INTO doc,schema_version FROM public.catalog_attribute_schemas s WHERE s.tenant_id=tid AND s.business_profile_code=profile FOR UPDATE;
 doc:=COALESCE(doc,'{"attributes":[],"categories":[]}'::jsonb);schema_version:=COALESCE(schema_version,0);
 IF schema_version IS DISTINCT FROM (configuration->>'schemaVersion')::bigint THEN RAISE EXCEPTION 'Category settings changed; reload before saving' USING ERRCODE='23514'; END IF;
 FOR a IN SELECT * FROM jsonb_array_elements(configuration->'attributes') LOOP
  SELECT COALESCE(jsonb_agg(x),'[]'::jsonb) INTO rules FROM jsonb_array_elements(doc->'attributes') x WHERE x->>'code' IS DISTINCT FROM a->>'code';
  doc:=jsonb_set(doc,'{attributes}',rules||jsonb_build_array(a));
 END LOOP;
 SELECT COALESCE(jsonb_agg(x),'[]'::jsonb) INTO rules FROM jsonb_array_elements(doc->'categories') x WHERE (x->>'categoryId')::integer<>cat;
 doc:=jsonb_set(doc,'{categories}',rules||jsonb_build_array(jsonb_build_object('categoryId',cat,'fields',configuration->'fields')));
 IF (defaults->>'enableVehicleCompatibility')::boolean AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(public.sf_category_attribute_config(doc,cat)) r WHERE r->'rule'->>'code'='vehicle_compatibility' AND r->'attribute'->>'type'='TEXT_LIST') THEN RAISE EXCEPTION 'Configure the vehicle compatibility field first' USING ERRCODE='23514'; END IF;
 IF (defaults->>'requireManufacturerPartNumber')::boolean AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(public.sf_category_attribute_config(doc,cat)) r WHERE r->'rule'->>'code'='manufacturer_part_number' AND (r->'rule'->>'required')::boolean) THEN RAISE EXCEPTION 'Configure a required manufacturer part number field first' USING ERRCODE='23514'; END IF;
 -- Existing routine enforces edit permission, concurrency, field policies and tenant-wide product locks.
 next_version:=public.sf_save_catalog_schema(doc,schema_version);
 SELECT registration_defaults INTO previous FROM public.categories WHERE tenant_id=tid AND category_id=cat;
 IF defaults IS DISTINCT FROM previous AND EXISTS(SELECT 1 FROM public.products WHERE tenant_id=tid AND category_id=cat) THEN
  RAISE EXCEPTION 'Inventory rules are locked after product registration; use a reviewed migration or new category' USING ERRCODE='23514'; END IF;
 UPDATE public.categories SET registration_defaults=defaults WHERE tenant_id=tid AND category_id=cat;
 RETURN next_version;
END $$;
REVOKE ALL ON FUNCTION public.sf_save_category_configuration(integer,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_save_category_configuration(integer,jsonb) TO stockflow_runtime;
COMMENT ON COLUMN public.categories.registration_defaults IS '056: validated category defaults used for new product registration, never stock balances. NULL retains legacy behavior.';
COMMIT;
