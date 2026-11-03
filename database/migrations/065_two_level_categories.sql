-- MANUAL ONLY. Back up the complete database and stop application/workers. Apply after 064 as PostgreSQL superuser.
-- First run database/tools/065_category_two_level_preview.sql. Ambiguous roots/conflicts abort this transaction.
-- Promotes existing IDs. No products, variants, balances, barcodes or historical transactions are rewritten.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 065 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='products' AND column_name='parent_product_code') THEN RAISE EXCEPTION 'Apply 064 first'; END IF;
END $$;
LOCK TABLE public.categories,public.products,public.catalog_attribute_schemas,public.brand_categories,public.supplier_product_catalog IN ACCESS EXCLUSIVE MODE;
-- Stops catalog/profile writers in the same order used by the application. Application/workers must be stopped.
DO $$ DECLARE r record; BEGIN FOR r IN SELECT tenant_id FROM public.tenants ORDER BY tenant_id LOOP PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||r.tenant_id::text,0)); END LOOP; END $$;
CREATE TEMP TABLE sf065_preview ON COMMIT DROP AS WITH roots AS (
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
DO $$ DECLARE doc jsonb; r jsonb; BEGIN
 SELECT migration_preview INTO doc FROM sf065_preview;
 IF jsonb_array_length(doc->'hierarchy_conflicts')>0 OR jsonb_array_length(doc->'orphan_conflicts')>0 THEN RAISE EXCEPTION '065 planned hierarchy is invalid: %',doc; END IF;
 IF jsonb_array_length(doc->'duplicate_conflicts')>0 THEN RAISE EXCEPTION '065 normalized parent/sibling name conflicts: %',doc->'duplicate_conflicts'; END IF;
 FOR r IN SELECT value FROM jsonb_array_elements(doc->'root_review') LOOP
  IF (r->>'direct_products')::int>0 OR (r->>'brand_mappings')::int>0 OR (r->>'supplier_mappings')::int>0 OR (r->>'inherited_fields')::int>0 OR (r->>'configuration_entries')::int>0 OR r->'inventory_defaults'<>'null'::jsonb OR (r->>'inactive_root_with_active_children')::boolean THEN RAISE EXCEPTION '065 root requires explicit dependency/status resolution: %',r; END IF;
 END LOOP;
 IF EXISTS(SELECT 1 FROM pg_constraint WHERE confrelid='public.categories'::regclass AND conrelid::regclass::text NOT IN('categories','products','brand_categories','supplier_product_catalog')) THEN RAISE EXCEPTION '065 found an additional category foreign key; inspect before migration'; END IF;
 IF EXISTS(SELECT 1 FROM public.categories c LEFT JOIN public.categories p ON p.tenant_id=c.tenant_id AND p.category_id=c.parent_category_id AND p.business_profile_code=c.business_profile_code WHERE c.parent_category_id IS NOT NULL AND p.category_id IS NULL) THEN RAISE EXCEPTION '065 orphan or cross-profile parent'; END IF;
END $$;
CREATE TABLE IF NOT EXISTS public.category_hierarchy_065_backup(section text PRIMARY KEY,document jsonb NOT NULL,saved_at timestamptz NOT NULL DEFAULT now());
REVOKE ALL ON public.category_hierarchy_065_backup FROM PUBLIC,stockflow_runtime;
INSERT INTO public.category_hierarchy_065_backup(section,document) SELECT 'categories',COALESCE(jsonb_agg(to_jsonb(c)),'[]') FROM public.categories c ON CONFLICT DO NOTHING;
INSERT INTO public.category_hierarchy_065_backup(section,document) SELECT 'preview',migration_preview FROM sf065_preview ON CONFLICT DO NOTHING;
INSERT INTO public.category_hierarchy_065_backup(section,document) SELECT 'functions',jsonb_agg(pg_get_functiondef(oid)) FROM pg_proc WHERE oid IN('public.sf_validate_catalog_registration()'::regprocedure,'public.sf_validate_product_catalog_assignment()'::regprocedure,'public.sf_organize_bike_hierarchy(bigint)'::regprocedure,'public.sf_bike_hierarchy_category(bigint,text,integer)'::regprocedure) ON CONFLICT DO NOTHING;
CREATE TEMP TABLE sf065_identity ON COMMIT DROP AS SELECT category_id,tenant_id,to_jsonb(c)-'parent_category_id'-'is_active'-'is_retired_grouping' original_document FROM public.categories c;
CREATE TEMP TABLE sf065_flags ON COMMIT DROP AS SELECT tenant_id,category_id,is_active FROM public.categories;
CREATE TEMP TABLE sf065_references ON COMMIT DROP AS SELECT product_id,tenant_id,category_id FROM public.products;
-- Snapshot every non-catalog application table. Audit logs may append migration records, never lose history.
CREATE TEMP TABLE sf065_data_hash(table_name text PRIMARY KEY,fingerprint text) ON COMMIT DROP;
DO $$ DECLARE t record; h text; BEGIN
 FOR t IN SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename NOT IN('categories','audit_logs','category_hierarchy_065_backup') ORDER BY tablename LOOP
  EXECUTE format('LOCK TABLE public.%I IN SHARE MODE',t.tablename);
  EXECUTE format('SELECT md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'''' ORDER BY md5(to_jsonb(t)::text)),'''')) FROM public.%I t',t.tablename) INTO h;
  INSERT INTO sf065_data_hash VALUES(t.tablename,h);
 END LOOP;
END $$;
ALTER TABLE public.categories ADD COLUMN IF NOT EXISTS is_retired_grouping boolean NOT NULL DEFAULT false;
-- Preserve exact trigger modes while updating archived profiles and populated category relationships.
CREATE TEMP TABLE sf065_triggers ON COMMIT DROP AS SELECT tgname,tgenabled FROM pg_trigger WHERE tgrelid='public.categories'::regclass AND NOT tgisinternal;
DO $$ DECLARE r record; BEGIN FOR r IN SELECT * FROM sf065_triggers WHERE tgenabled<>'D' LOOP EXECUTE format('ALTER TABLE public.categories DISABLE TRIGGER %I',r.tgname); END LOOP; END $$;
UPDATE public.categories c SET parent_category_id=NULL
 FROM sf065_preview p CROSS JOIN LATERAL jsonb_array_elements(p.migration_preview->'promotions') m
 WHERE c.tenant_id=(m->>'tenant_id')::bigint AND c.category_id=(m->>'category_id')::int;
UPDATE public.categories c SET is_active=false,is_retired_grouping=true
 FROM sf065_preview p CROSS JOIN LATERAL jsonb_array_elements(p.migration_preview->'root_review') m
 WHERE c.tenant_id=(m->>'tenant_id')::bigint AND c.category_id=(m->>'category_id')::int;
-- Record an audit entry without requiring an invented application actor.
INSERT INTO public.audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,old_values,new_values)
 SELECT c.tenant_id,NULL,'Categories','TWO_LEVEL_MIGRATION','categories',c.category_id,old, to_jsonb(c)
 FROM public.categories c JOIN public.category_hierarchy_065_backup b ON b.section='categories'
 CROSS JOIN LATERAL jsonb_array_elements(b.document) old
 WHERE (old->>'category_id')::int=c.category_id AND (old->>'tenant_id')::bigint=c.tenant_id
 AND (old->>'parent_category_id' IS DISTINCT FROM to_jsonb(c)->>'parent_category_id' OR (NOT COALESCE((old->>'is_retired_grouping')::boolean,false) AND c.is_retired_grouping))
 AND NOT EXISTS(SELECT 1 FROM public.audit_logs a WHERE a.tenant_id=c.tenant_id AND a.record_id=c.category_id AND a.action_name='TWO_LEVEL_MIGRATION');
CREATE OR REPLACE FUNCTION public.sf_category_name_key(text) RETURNS text LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE SET search_path=pg_catalog,public,pg_temp AS $$ SELECT lower(regexp_replace(btrim(normalize($1,NFKC)),'[[:space:]]+',' ','g')) $$;
DROP INDEX IF EXISTS public.ux_category_root_name;
DROP INDEX IF EXISTS public.ux_category_child_name;
CREATE UNIQUE INDEX ux_category_root_name ON public.categories(tenant_id,business_profile_code,public.sf_category_name_key(category_name)) WHERE parent_category_id IS NULL;
CREATE UNIQUE INDEX ux_category_child_name ON public.categories(tenant_id,business_profile_code,parent_category_id,public.sf_category_name_key(category_name)) WHERE parent_category_id IS NOT NULL;

CREATE OR REPLACE FUNCTION public.sf_validate_catalog_registration() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE parent_row public.categories%ROWTYPE;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF TG_OP='UPDATE' THEN
  IF NEW.tenant_id<>OLD.tenant_id OR (to_jsonb(NEW)->>(CASE WHEN TG_TABLE_NAME='categories' THEN 'category_id' ELSE 'brand_id' END))<>(to_jsonb(OLD)->>(CASE WHEN TG_TABLE_NAME='categories' THEN 'category_id' ELSE 'brand_id' END)) THEN RAISE EXCEPTION 'Catalog identity cannot change' USING ERRCODE='23514'; END IF;
  NEW.created_by:=OLD.created_by;NEW.created_at:=OLD.created_at;NEW.updated_by:=NULLIF(public.sf_actor(),0);NEW.updated_at:=CURRENT_TIMESTAMP;
 ELSE NEW.created_by:=NULLIF(public.sf_actor(),0);NEW.updated_by:=NULL;NEW.updated_at:=NULL;
 END IF;
 IF TG_TABLE_NAME='categories' THEN
  NEW.category_name:=regexp_replace(btrim(normalize(NEW.category_name,NFKC)),'[[:space:]]+',' ','g');NEW.description:=NULLIF(btrim(NEW.description),'');
  IF TG_OP='INSERT' AND NEW.is_retired_grouping OR TG_OP='UPDATE' AND (NEW.is_retired_grouping IS DISTINCT FROM OLD.is_retired_grouping OR OLD.is_retired_grouping) THEN RAISE EXCEPTION 'Retired groupings require a reviewed migration' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND NEW.parent_category_id IS DISTINCT FROM OLD.parent_category_id THEN RAISE EXCEPTION 'Category parents are read-only; use a reviewed migration' USING ERRCODE='23514'; END IF;
  IF NEW.parent_category_id IS NOT NULL THEN
   SELECT * INTO parent_row FROM public.categories WHERE tenant_id=NEW.tenant_id AND category_id=NEW.parent_category_id AND business_profile_code=NEW.business_profile_code;
   IF NOT FOUND OR parent_row.parent_category_id IS NOT NULL OR parent_row.is_retired_grouping OR parent_row.category_id=NEW.category_id THEN RAISE EXCEPTION 'Choose a top-level parent category from this shop; third levels are not allowed' USING ERRCODE='23514'; END IF;
   IF TG_OP='INSERT' AND NOT parent_row.is_active THEN RAISE EXCEPTION 'Choose an active parent category' USING ERRCODE='23514'; END IF;
   IF EXISTS(SELECT 1 FROM public.categories WHERE tenant_id=NEW.tenant_id AND parent_category_id=NEW.category_id) THEN RAISE EXCEPTION 'A subcategory cannot have children' USING ERRCODE='23514'; END IF;
  END IF;
  -- Deactivation preserves descendants' individual statuses and every existing product/history record.
 ELSE
  NEW.brand_name:=btrim(NEW.brand_name);NEW.description:=NULLIF(btrim(NEW.description),'');
  IF NOT NEW.is_active AND EXISTS(SELECT 1 FROM public.products WHERE tenant_id=NEW.tenant_id AND brand_id=NEW.brand_id AND is_active) THEN RAISE EXCEPTION 'Reassign or deactivate active products first' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION public.sf_validate_product_catalog_assignment() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF TG_OP='INSERT' OR NEW.category_id IS DISTINCT FROM OLD.category_id THEN
  IF NOT EXISTS(SELECT 1 FROM public.categories c LEFT JOIN public.categories p ON p.tenant_id=c.tenant_id AND p.category_id=c.parent_category_id
   WHERE c.tenant_id=NEW.tenant_id AND c.category_id=NEW.category_id AND c.is_active AND NOT c.is_retired_grouping
   AND (c.parent_category_id IS NULL OR (p.parent_category_id IS NULL AND p.is_active AND NOT p.is_retired_grouping AND p.business_profile_code=c.business_profile_code))) THEN RAISE EXCEPTION 'Choose an active parent category or its immediate active subcategory' USING ERRCODE='23514'; END IF;
 END IF;
 IF NEW.is_active AND NEW.brand_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.brands WHERE tenant_id=NEW.tenant_id AND brand_id=NEW.brand_id AND is_active) THEN RAISE EXCEPTION 'Select an active brand' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
-- Future profile initialization must never recreate the removed wrapper/third level.
CREATE OR REPLACE FUNCTION public.sf_bike_hierarchy_category(tid bigint,label text,parent integer) RETURNS integer LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE id integer;
BEGIN
 IF parent IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.categories WHERE tenant_id=tid AND category_id=parent AND parent_category_id IS NULL AND is_active AND NOT is_retired_grouping) THEN RETURN NULL; END IF;
 SELECT category_id INTO id FROM public.categories WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES' AND public.sf_category_name_key(category_name)=public.sf_category_name_key(label) AND parent_category_id IS NOT DISTINCT FROM parent AND NOT is_retired_grouping;
 IF id IS NULL THEN INSERT INTO public.categories(tenant_id,business_profile_code,category_name,parent_category_id,is_active) VALUES(tid,'BIKE_ACCESSORIES',label,parent,true) RETURNING category_id INTO id; END IF;
 RETURN id;
END $$;
CREATE OR REPLACE FUNCTION public.sf_organize_bike_hierarchy(tid bigint) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE group_id integer;grouping text;child text;
BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.tenants WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES') THEN RETURN; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 FOREACH grouping IN ARRAY ARRAY['Helmets','Bike Spare Parts','Lights & Electrical','Guards & Protection','Riding Accessories'] LOOP
  group_id:=public.sf_bike_hierarchy_category(tid,grouping,NULL);IF group_id IS NULL THEN CONTINUE; END IF;
  FOREACH child IN ARRAY (CASE grouping WHEN 'Helmets' THEN ARRAY['Full Face Helmets','Half Face Helmets','Modular Helmets'] WHEN 'Bike Spare Parts' THEN ARRAY['Brake Parts','Chains & Sprockets','Filters','Clutch Parts'] WHEN 'Lights & Electrical' THEN ARRAY['Head Lights','Indicators','Fog Lights','Horns'] WHEN 'Guards & Protection' THEN ARRAY['Side Guards','Crash Guards','Engine Guards'] ELSE ARRAY['Gloves','Bike Covers','Bike Luggage','Riding Jackets'] END) LOOP PERFORM public.sf_bike_hierarchy_category(tid,child,group_id); END LOOP;
 END LOOP;
 PERFORM public.sf_bike_hierarchy_category(tid,'Mirrors',NULL);
END $$;
REVOKE ALL ON FUNCTION public.sf_validate_catalog_registration(),public.sf_validate_product_catalog_assignment(),public.sf_bike_hierarchy_category(bigint,text,integer),public.sf_organize_bike_hierarchy(bigint) FROM PUBLIC;
DO $$ DECLARE r record; BEGIN FOR r IN SELECT * FROM sf065_triggers WHERE tgenabled<>'D' LOOP EXECUTE format('ALTER TABLE public.categories ENABLE %s TRIGGER %I',CASE r.tgenabled WHEN 'A' THEN 'ALWAYS' WHEN 'R' THEN 'REPLICA' ELSE '' END,r.tgname); END LOOP; END $$;
DO $$ DECLARE r record;h text; BEGIN
 IF EXISTS(SELECT 1 FROM public.categories c JOIN public.categories p ON p.tenant_id=c.tenant_id AND p.category_id=c.parent_category_id WHERE p.parent_category_id IS NOT NULL) THEN RAISE EXCEPTION '065 cannot leave third-level categories or cycles'; END IF;
 IF (SELECT count(*) FROM public.categories)<>(SELECT count(*) FROM sf065_identity) OR EXISTS((SELECT * FROM sf065_identity EXCEPT SELECT category_id,tenant_id,to_jsonb(c)-'parent_category_id'-'is_active'-'is_retired_grouping' FROM public.categories c)) THEN RAISE EXCEPTION '065 changed category identities or metadata'; END IF;
 IF EXISTS(SELECT 1 FROM public.categories c JOIN sf065_flags f USING(tenant_id,category_id) WHERE NOT c.is_retired_grouping AND c.is_active IS DISTINCT FROM f.is_active) THEN RAISE EXCEPTION '065 changed a retained category status'; END IF;
 IF EXISTS(SELECT 1 FROM sf065_preview p CROSS JOIN LATERAL jsonb_array_elements(p.migration_preview->'before_after') m JOIN public.categories c ON c.tenant_id=(m->>'tenant_id')::bigint AND c.category_id=(m->>'category_id')::int WHERE c.parent_category_id IS DISTINCT FROM (m->>'after_parent')::int) THEN RAISE EXCEPTION '065 hierarchy differs from preview'; END IF;
 IF EXISTS((SELECT * FROM sf065_references EXCEPT SELECT product_id,tenant_id,category_id FROM public.products)) THEN RAISE EXCEPTION '065 changed product/category relationships'; END IF;
 FOR r IN SELECT * FROM sf065_data_hash LOOP
  EXECUTE format('SELECT md5(COALESCE(string_agg(md5(to_jsonb(t)::text),'''' ORDER BY md5(to_jsonb(t)::text)),'''')) FROM public.%I t',r.table_name) INTO h;
  IF h IS DISTINCT FROM r.fingerprint THEN RAISE EXCEPTION '065 changed protected table %',r.table_name; END IF;
 END LOOP;
END $$;
COMMENT ON COLUMN public.categories.is_retired_grouping IS '065: reference-free former grouping kept with its original ID for audit/recovery; excluded from new classifications.';
COMMIT;
