-- MANUAL ONLY. Back up and stop application/workers. Run after 054 as PostgreSQL superuser.
-- Existing category IDs/codes, product/brand links, stock and history are retained.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 055 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.stock_item_labels') IS NULL THEN RAISE EXCEPTION 'Apply 054 first'; END IF;
 IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='inherit_parent_fields') THEN RAISE EXCEPTION '055 is already installed'; END IF;
END $$;
ALTER TABLE public.categories ADD COLUMN inherit_parent_fields boolean NOT NULL DEFAULT true;
-- Freeze the existing direct configuration of populated categories before enabling inheritance.
-- Temporarily preserve trigger states: archived profiles must not be edited as the active profile.
CREATE TEMP TABLE sf055_trigger_state ON COMMIT DROP AS
 SELECT tgname,tgenabled FROM pg_trigger WHERE tgrelid='public.categories'::regclass AND NOT tgisinternal;
DO $$ DECLARE r record; BEGIN
 FOR r IN SELECT * FROM sf055_trigger_state WHERE tgenabled<>'D' LOOP EXECUTE format('ALTER TABLE public.categories DISABLE TRIGGER %I',r.tgname); END LOOP;
END $$;
UPDATE public.categories c SET inherit_parent_fields=false
 WHERE EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=c.tenant_id AND p.category_id=c.category_id);
DO $$ DECLARE r record; BEGIN
 FOR r IN SELECT * FROM sf055_trigger_state WHERE tgenabled<>'D' LOOP
  EXECUTE format('ALTER TABLE public.categories ENABLE %s TRIGGER %I',CASE r.tgenabled WHEN 'A' THEN 'ALWAYS' WHEN 'R' THEN 'REPLICA' ELSE '' END,r.tgname);
 END LOOP;
END $$;
COMMENT ON COLUMN public.categories.inherit_parent_fields IS '055: ancestors contribute fields unless this boundary is false; existing populated categories retain their direct rules pending explicit review.';

CREATE OR REPLACE FUNCTION public.sf_category_attribute_config(doc jsonb,cat integer) RETURNS jsonb
LANGUAGE sql STABLE SET search_path=pg_catalog,public,pg_temp AS $$
 WITH RECURSIVE lineage AS (
  SELECT c.category_id,c.parent_category_id,c.inherit_parent_fields,c.business_profile_code,0 depth
  FROM public.categories c WHERE c.tenant_id=public.sf_tenant() AND c.category_id=cat
  UNION ALL
  SELECT p.category_id,p.parent_category_id,p.inherit_parent_fields,p.business_profile_code,l.depth+1
  FROM public.categories p JOIN lineage l ON p.category_id=l.parent_category_id AND p.business_profile_code=l.business_profile_code
  WHERE p.tenant_id=public.sf_tenant() AND l.inherit_parent_fields AND l.depth<2
 ), rules AS (
  SELECT DISTINCT ON (f->>'code') f,l.depth FROM lineage l
  JOIN LATERAL jsonb_array_elements(COALESCE(doc->'categories','[]')) c ON (c->>'categoryId')::integer=l.category_id
  CROSS JOIN LATERAL jsonb_array_elements(c->'fields') f ORDER BY f->>'code',l.depth
 )
 SELECT COALESCE(jsonb_agg(jsonb_build_object('rule',jsonb_build_object(
  'code',f->>'code','required',f->'required','variantAxis',f->'variantAxis',
  'visible',COALESCE(f->'visible','true'::jsonb),'searchable',COALESCE(f->'searchable','true'::jsonb),
  'defaultValue',COALESCE(f->'defaultValue','null'::jsonb)),'attribute',a) ORDER BY f->>'code'),'[]'::jsonb)
 FROM rules JOIN jsonb_array_elements(COALESCE(doc->'attributes','[]')) a ON a->>'code'=f->>'code'
$$;

CREATE OR REPLACE FUNCTION public.sf_catalog_locked_categories() RETURNS SETOF integer
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 WITH RECURSIVE locked AS (
  SELECT c.category_id,c.parent_category_id,c.inherit_parent_fields,0 depth FROM public.categories c
  WHERE c.tenant_id=public.sf_tenant() AND public.sf_actor()>0
  AND EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=c.tenant_id AND p.category_id=c.category_id)
  UNION
  SELECT c.category_id,c.parent_category_id,c.inherit_parent_fields,l.depth+1 FROM public.categories c
  JOIN locked l ON c.category_id=l.parent_category_id WHERE c.tenant_id=public.sf_tenant() AND l.inherit_parent_fields AND l.depth<2
 ) SELECT DISTINCT category_id FROM locked
$$;

CREATE OR REPLACE FUNCTION public.sf_used_category_parent_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF (NEW.parent_category_id IS DISTINCT FROM OLD.parent_category_id OR NEW.inherit_parent_fields IS DISTINCT FROM OLD.inherit_parent_fields) AND EXISTS(
  WITH RECURSIVE tree AS (
   SELECT OLD.category_id id UNION ALL SELECT c.category_id FROM public.categories c JOIN tree t ON c.parent_category_id=t.id WHERE c.tenant_id=OLD.tenant_id
  ) SELECT 1 FROM public.products p JOIN tree t ON p.category_id=t.id WHERE p.tenant_id=OLD.tenant_id
 ) THEN RAISE EXCEPTION 'Registered products protect this hierarchy and inherited rules; use a reviewed migration or a new category' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;

CREATE FUNCTION public.sf_inherited_category_limits() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE doc jsonb; cat integer; cfg jsonb; tid bigint:=NEW.tenant_id; saved_tid text:=current_setting('app.tenant_id',true);
BEGIN
 -- This guard also runs during superuser seeding; resolve functions against the row's tenant.
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 PERFORM set_config('app.tenant_id',tid::text,true);
 IF TG_TABLE_NAME='catalog_attribute_schemas' THEN doc:=NEW.definition;
 ELSE SELECT definition INTO doc FROM public.catalog_attribute_schemas WHERE tenant_id=tid AND business_profile_code=NEW.business_profile_code; END IF;
 FOR cat IN SELECT category_id FROM public.categories WHERE tenant_id=tid AND business_profile_code=NEW.business_profile_code LOOP
  cfg:=public.sf_category_attribute_config(doc,cat);
  IF jsonb_array_length(cfg)>30 OR (SELECT count(*) FROM jsonb_array_elements(cfg) r WHERE (r->'rule'->>'variantAxis')::boolean)>3 THEN
   RAISE EXCEPTION 'Inherited category fields exceed 30 fields or three variant axes' USING ERRCODE='23514'; END IF;
 END LOOP;
 PERFORM set_config('app.tenant_id',COALESCE(saved_tid,''),true);
 RETURN NEW;
END $$;
CREATE TRIGGER inherited_category_limits AFTER INSERT OR UPDATE ON public.categories FOR EACH ROW EXECUTE FUNCTION public.sf_inherited_category_limits();
CREATE TRIGGER inherited_schema_limits BEFORE INSERT OR UPDATE ON public.catalog_attribute_schemas FOR EACH ROW EXECUTE FUNCTION public.sf_inherited_category_limits();
REVOKE ALL ON FUNCTION public.sf_inherited_category_limits() FROM PUBLIC;

CREATE OR REPLACE FUNCTION public.sf_searchable_attributes(cat integer,attrs jsonb) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT COALESCE(string_agg(v.value,' '),'') FROM jsonb_each_text(attrs) v
 WHERE NOT EXISTS(SELECT 1 FROM public.catalog_attribute_schemas s,
  jsonb_array_elements(public.sf_category_attribute_config(s.definition,cat)) r
  WHERE s.tenant_id=public.sf_tenant() AND s.business_profile_code=public.sf_current_business_profile()
  AND r->'rule'->>'code'=v.key AND COALESCE((r->'rule'->>'searchable')::boolean,true)=false)
$$;

-- Reuse existing Bike identities. Only unused subtrees may be moved automatically.
CREATE FUNCTION public.sf_bike_hierarchy_category(tid bigint,label text,parent integer) RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE id integer; previous_parent integer; active boolean; matches integer; parent_depth integer; subtree_height integer;
BEGIN
 WITH RECURSIVE ancestors AS (
  SELECT category_id,parent_category_id,1 depth FROM public.categories WHERE tenant_id=tid AND category_id=parent
  UNION ALL SELECT c.category_id,c.parent_category_id,a.depth+1 FROM public.categories c JOIN ancestors a ON c.category_id=a.parent_category_id WHERE c.tenant_id=tid AND a.depth<3
 ) SELECT COALESCE(max(depth),0) INTO parent_depth FROM ancestors;
 IF parent_depth>=3 THEN RAISE NOTICE '055: category % would exceed three levels; leave for review',label; RETURN NULL; END IF;
 SELECT count(*) INTO matches FROM public.categories WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES' AND lower(btrim(category_name))=lower(label);
 IF matches>1 THEN RAISE NOTICE '055: ambiguous category %, leave for review',label; RETURN NULL; END IF;
 SELECT category_id,parent_category_id,is_active INTO id,previous_parent,active FROM public.categories
 WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES' AND lower(btrim(category_name))=lower(label);
 IF id IS NULL THEN
  INSERT INTO public.categories(tenant_id,business_profile_code,category_name,parent_category_id,is_active)
  VALUES(tid,'BIKE_ACCESSORIES',label,parent,true) RETURNING category_id INTO id;
 ELSIF NOT active THEN RETURN NULL;
 ELSIF previous_parent IS DISTINCT FROM parent THEN
  IF EXISTS(WITH RECURSIVE tree AS (SELECT id AS cid UNION ALL SELECT c.category_id FROM public.categories c JOIN tree t ON c.parent_category_id=t.cid WHERE c.tenant_id=tid)
   SELECT 1 FROM public.products p JOIN tree t ON p.category_id=t.cid WHERE p.tenant_id=tid) THEN
   RAISE NOTICE '055: populated category % retained at its existing parent; review separately',label;
  ELSE
   WITH RECURSIVE descendants AS (
    SELECT id cid,1 depth UNION ALL SELECT c.category_id,d.depth+1 FROM public.categories c JOIN descendants d ON c.parent_category_id=d.cid WHERE c.tenant_id=tid AND d.depth<3
   ) SELECT max(depth) INTO subtree_height FROM descendants;
   IF parent_depth+subtree_height>3 THEN RAISE NOTICE '055: subtree % retained to preserve the three-level limit',label;
   ELSE UPDATE public.categories SET parent_category_id=parent WHERE tenant_id=tid AND category_id=id; END IF;
  END IF;
 END IF;
 RETURN id;
END $$;

CREATE FUNCTION public.sf_organize_bike_hierarchy(tid bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE root integer; group_id integer; child text; grouping text;
BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.tenants WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES') THEN RETURN; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 root:=public.sf_bike_hierarchy_category(tid,'Bike Parts & Accessories',NULL);
 IF root IS NULL THEN RETURN; END IF;
 FOREACH grouping IN ARRAY ARRAY['Helmets','Bike Spare Parts','Lights & Electrical','Guards & Protection','Riding Accessories'] LOOP
  group_id:=public.sf_bike_hierarchy_category(tid,grouping,root);
  IF group_id IS NULL THEN CONTINUE; END IF;
  FOREACH child IN ARRAY (CASE grouping
   WHEN 'Helmets' THEN ARRAY['Full Face Helmets','Half Face Helmets','Modular Helmets']
   WHEN 'Bike Spare Parts' THEN ARRAY['Brake Parts','Chains & Sprockets','Filters','Clutch Parts']
   WHEN 'Lights & Electrical' THEN ARRAY['Head Lights','Indicators','Fog Lights','Horns']
   WHEN 'Guards & Protection' THEN ARRAY['Side Guards','Crash Guards','Engine Guards']
   ELSE ARRAY['Gloves','Bike Covers','Bike Luggage','Riding Jackets'] END) LOOP
   PERFORM public.sf_bike_hierarchy_category(tid,child,group_id);
  END LOOP;
 END LOOP;
 PERFORM public.sf_bike_hierarchy_category(tid,'Mirrors',root);
END $$;
REVOKE ALL ON FUNCTION public.sf_bike_hierarchy_category(bigint,text,integer),public.sf_organize_bike_hierarchy(bigint) FROM PUBLIC;

DO $$ DECLARE tid bigint; saved_tid text:=current_setting('app.tenant_id',true); BEGIN
 FOR tid IN SELECT tenant_id FROM public.tenants WHERE business_profile_code='BIKE_ACCESSORIES' LOOP
  PERFORM set_config('app.tenant_id',tid::text,true);
  PERFORM public.sf_organize_bike_hierarchy(tid);
 END LOOP;
 PERFORM set_config('app.tenant_id',COALESCE(saved_tid,''),true);
END $$;
ALTER FUNCTION public.sf_seed_bike_catalog(bigint) RENAME TO sf_seed_bike_catalog_052;
CREATE FUNCTION public.sf_seed_bike_catalog(tid bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 -- The earlier flat seed must not recreate root duplicates of categories now nested.
 IF NOT EXISTS(SELECT 1 FROM public.categories WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES')
 OR NOT EXISTS(SELECT 1 FROM public.catalog_attribute_schemas WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES') THEN
  PERFORM public.sf_seed_bike_catalog_052(tid);
 END IF;
 PERFORM public.sf_organize_bike_hierarchy(tid);
END $$;
REVOKE ALL ON FUNCTION public.sf_seed_bike_catalog(bigint),public.sf_seed_bike_catalog_052(bigint) FROM PUBLIC;
-- Validate archived and active profiles before committing the new resolution rules.
DO $$ DECLARE s record; cat integer; cfg jsonb; saved_tid text:=current_setting('app.tenant_id',true); BEGIN
 FOR s IN SELECT tenant_id,business_profile_code,definition FROM public.catalog_attribute_schemas LOOP
  PERFORM set_config('app.tenant_id',s.tenant_id::text,true);
  FOR cat IN SELECT category_id FROM public.categories WHERE tenant_id=s.tenant_id AND business_profile_code=s.business_profile_code LOOP
   cfg:=public.sf_category_attribute_config(s.definition,cat);
   IF jsonb_array_length(cfg)>30 OR (SELECT count(*) FROM jsonb_array_elements(cfg) r WHERE (r->'rule'->>'variantAxis')::boolean)>3 THEN
    RAISE EXCEPTION 'Review inherited fields for category % before enabling 055',cat;
   END IF;
  END LOOP;
 END LOOP;
 PERFORM set_config('app.tenant_id',COALESCE(saved_tid,''),true);
END $$;
COMMIT;
