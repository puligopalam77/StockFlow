-- MANUAL ONLY. Back up and stop writers; apply after 047 as PostgreSQL superuser.
-- No existing catalog, quantities, serials, prices or history are converted.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 048 as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_change_business_profile(text,bigint,text,boolean)') IS NULL THEN RAISE EXCEPTION 'Apply 047 first'; END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.catalog_attribute_schemas(
 tenant_id bigint NOT NULL REFERENCES public.tenants(tenant_id),
 business_profile_code varchar(40) NOT NULL CHECK(business_profile_code IN('ELECTRICAL','BIKE_ACCESSORIES','CLOTHING','FOOTWEAR')),
 version bigint NOT NULL CHECK(version>0),
 definition jsonb NOT NULL CHECK(jsonb_typeof(definition)='object'),
 updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,business_profile_code)
);
ALTER TABLE public.catalog_attribute_schemas ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS catalog_schema_tenant ON public.catalog_attribute_schemas;
CREATE POLICY catalog_schema_tenant ON public.catalog_attribute_schemas FOR SELECT TO stockflow_runtime
 USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_current_business_profile());
REVOKE ALL ON public.catalog_attribute_schemas FROM PUBLIC,stockflow_runtime;
GRANT SELECT ON public.catalog_attribute_schemas TO stockflow_runtime;

CREATE OR REPLACE FUNCTION public.sf_catalog_locked_categories() RETURNS SETOF integer
 LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT DISTINCT category_id FROM public.products WHERE tenant_id=public.sf_tenant() AND public.sf_actor()>0
$$;
REVOKE ALL ON FUNCTION public.sf_catalog_locked_categories() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_catalog_locked_categories() TO stockflow_runtime;

-- Effective configuration is compared by code, so reordering definitions is harmless.
CREATE OR REPLACE FUNCTION public.sf_category_attribute_config(doc jsonb,cat integer) RETURNS jsonb
 LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT COALESCE(jsonb_agg(jsonb_build_object('rule',f,'attribute',a) ORDER BY f->>'code'),'[]'::jsonb)
 FROM jsonb_array_elements(COALESCE(doc->'categories','[]')) c,
 jsonb_array_elements(c->'fields') f JOIN jsonb_array_elements(COALESCE(doc->'attributes','[]')) a ON a->>'code'=f->>'code'
 WHERE (c->>'categoryId')::integer=cat
$$;

CREATE OR REPLACE FUNCTION public.sf_save_catalog_schema(doc jsonb,expected_version bigint) RETURNS bigint
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); uid integer:=public.sf_actor(); profile text;
 old_doc jsonb; old_version bigint; a jsonb; c jsonb; f jsonb; cat integer;
BEGIN
 IF tid<=0 OR uid<=0 OR NOT EXISTS(
  SELECT 1 FROM public.users u JOIN public.roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id
  JOIN public.tenants t ON t.tenant_id=u.tenant_id
  JOIN public.user_action_permissions p ON p.tenant_id=u.tenant_id AND p.user_id=u.user_id
  JOIN public.module_actions ma ON ma.module_action_id=p.module_action_id
  JOIN public.modules m ON m.module_id=ma.module_id
  WHERE u.tenant_id=tid AND u.user_id=uid AND u.is_active AND r.is_active AND t.is_active
   AND p.is_allowed AND ma.is_active AND m.is_active AND m.module_code='CATEGORIES' AND ma.action_code='EDIT'
 ) THEN RAISE EXCEPTION 'Category edit permission required' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 SELECT business_profile_code INTO profile FROM public.tenants WHERE tenant_id=tid;
 SELECT definition,version INTO old_doc,old_version FROM public.catalog_attribute_schemas WHERE tenant_id=tid AND business_profile_code=profile FOR UPDATE;
 IF COALESCE(old_version,0) IS DISTINCT FROM expected_version THEN RAISE EXCEPTION 'Attribute settings changed; reload before saving' USING ERRCODE='23514'; END IF;
 IF jsonb_typeof(doc->'attributes') IS DISTINCT FROM 'array' OR jsonb_typeof(doc->'categories') IS DISTINCT FROM 'array'
  OR jsonb_array_length(doc->'attributes')>30 OR jsonb_array_length(doc->'categories')>200 THEN
  RAISE EXCEPTION 'Invalid attribute configuration' USING ERRCODE='23514'; END IF;
 IF (SELECT count(*)<>count(DISTINCT x->>'code') FROM jsonb_array_elements(doc->'attributes') x)
 OR (SELECT count(*)<>count(DISTINCT x->>'categoryId') FROM jsonb_array_elements(doc->'categories') x) THEN
  RAISE EXCEPTION 'Duplicate attribute or category' USING ERRCODE='23514'; END IF;
 FOR a IN SELECT * FROM jsonb_array_elements(doc->'attributes') LOOP
  IF COALESCE(a->>'code','')!~'^[a-z][a-z0-9_]{0,39}$' OR length(btrim(COALESCE(a->>'label',''))) NOT BETWEEN 1 AND 80
   OR COALESCE(a->>'type','') NOT IN('TEXT','NUMBER','DATE','BOOLEAN','SELECT') OR jsonb_typeof(a->'options') IS DISTINCT FROM 'array'
   OR jsonb_array_length(a->'options')>50 THEN RAISE EXCEPTION 'Invalid attribute definition' USING ERRCODE='23514'; END IF;
  IF (a->>'type'='SELECT' AND jsonb_array_length(a->'options')=0) OR (a->>'type'<>'SELECT' AND jsonb_array_length(a->'options')<>0)
   OR EXISTS(SELECT 1 FROM jsonb_array_elements_text(a->'options') x WHERE length(btrim(x)) NOT BETWEEN 1 AND 100 OR x<>btrim(x))
   OR (SELECT count(*)<>count(DISTINCT lower(x)) FROM jsonb_array_elements_text(a->'options') x)
   OR (a->>'minimum')::numeric > (a->>'maximum')::numeric THEN
   RAISE EXCEPTION 'Invalid attribute options or limits' USING ERRCODE='23514'; END IF;
 END LOOP;
 FOR c IN SELECT * FROM jsonb_array_elements(doc->'categories') LOOP
  cat:=(c->>'categoryId')::integer;
  IF NOT EXISTS(SELECT 1 FROM public.categories WHERE tenant_id=tid AND category_id=cat AND business_profile_code=profile)
   OR jsonb_typeof(c->'fields') IS DISTINCT FROM 'array' OR jsonb_array_length(c->'fields')>30 THEN
   RAISE EXCEPTION 'Invalid current-profile category' USING ERRCODE='23514'; END IF;
  IF (SELECT count(*)<>count(DISTINCT x->>'code') FROM jsonb_array_elements(c->'fields') x)
   OR (SELECT count(*) FROM jsonb_array_elements(c->'fields') x WHERE (x->>'variantAxis')::boolean)>3 THEN
   RAISE EXCEPTION 'Duplicate fields or too many variant axes' USING ERRCODE='23514'; END IF;
  FOR f IN SELECT * FROM jsonb_array_elements(c->'fields') LOOP
   SELECT x INTO a FROM jsonb_array_elements(doc->'attributes') x WHERE x->>'code'=f->>'code';
   IF a IS NULL OR jsonb_typeof(f->'required') IS DISTINCT FROM 'boolean' OR jsonb_typeof(f->'variantAxis') IS DISTINCT FROM 'boolean'
    OR ((f->>'variantAxis')::boolean AND (a->>'type'<>'SELECT' OR NOT (f->>'required')::boolean)) THEN
    RAISE EXCEPTION 'Variant axes must be required selection fields' USING ERRCODE='23514'; END IF;
  END LOOP;
 END LOOP;
 -- Tenant-wide definer access includes inactive products and every branch.
 FOR cat IN SELECT DISTINCT category_id FROM public.products WHERE tenant_id=tid LOOP
  IF public.sf_category_attribute_config(old_doc,cat) IS DISTINCT FROM public.sf_category_attribute_config(doc,cat) THEN
   RAISE EXCEPTION 'Category attributes are locked after product registration; use a new category' USING ERRCODE='23514'; END IF;
 END LOOP;
 INSERT INTO public.catalog_attribute_schemas(tenant_id,business_profile_code,version,definition)
 VALUES(tid,profile,COALESCE(old_version,0)+1,doc)
 ON CONFLICT(tenant_id,business_profile_code) DO UPDATE SET version=EXCLUDED.version,definition=EXCLUDED.definition,updated_at=now();
 INSERT INTO public.audit_logs(tenant_id,user_id,location_id,module_name,action_name,table_name,record_id,old_values,new_values)
 VALUES(tid,uid,NULL,'Category attributes','UPDATE','catalog_attribute_schemas',tid,old_doc,doc);
 RETURN COALESCE(old_version,0)+1;
END $$;

-- Deferred validation allows the existing automatic DEFAULT row to be filled by
-- registration within the same transaction. Only the final committed state counts.
CREATE OR REPLACE FUNCTION public.sf_validate_category_attributes() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE doc jsonb; cfg jsonb; r jsonb; a jsonb; row_value record; value text; axes text[]; combo jsonb; combos jsonb[]:=ARRAY[]::jsonb[];
 pid integer; tid bigint; cat integer; number_value numeric; date_value date;
BEGIN
 pid:=NEW.product_id; tid:=NEW.tenant_id;
 SELECT p.category_id,s.definition INTO cat,doc FROM public.products p JOIN public.tenants t ON t.tenant_id=p.tenant_id
 LEFT JOIN public.catalog_attribute_schemas s ON s.tenant_id=t.tenant_id AND s.business_profile_code=t.business_profile_code
 WHERE p.tenant_id=tid AND p.product_id=pid;
 IF NOT FOUND THEN RETURN NULL; END IF;
 cfg:=public.sf_category_attribute_config(doc,cat);
 IF TG_TABLE_NAME='products' AND TG_OP='UPDATE' THEN
  IF OLD.category_id<>NEW.category_id AND (cfg<>'[]'::jsonb OR public.sf_category_attribute_config(doc,OLD.category_id)<>'[]'::jsonb) THEN
   RAISE EXCEPTION 'Configured product categories cannot change; register a new product' USING ERRCODE='23514'; END IF;
 END IF;
 IF cfg='[]'::jsonb THEN RETURN NULL; END IF;
 SELECT array_agg(x->'rule'->>'code' ORDER BY x->'rule'->>'code') INTO axes FROM jsonb_array_elements(cfg) x WHERE (x->'rule'->>'variantAxis')::boolean;
 IF NOT EXISTS(SELECT 1 FROM public.product_variants WHERE tenant_id=tid AND product_id=pid) THEN
  RAISE EXCEPTION 'Category attributes require item configurations' USING ERRCODE='23514'; END IF;
 IF (SELECT count(*) FROM public.product_variants WHERE tenant_id=tid AND product_id=pid)>30 THEN
  RAISE EXCEPTION 'Use at most 30 configured variants per product' USING ERRCODE='23514'; END IF;
 FOR row_value IN SELECT variant_id,attributes FROM public.product_variants WHERE tenant_id=tid AND product_id=pid ORDER BY variant_id LOOP
  FOR r IN SELECT * FROM jsonb_array_elements(cfg) LOOP
   a:=r->'attribute'; value:=btrim(COALESCE(row_value.attributes->>(a->>'code'),''));
   IF value='' THEN
    IF (r->'rule'->>'required')::boolean THEN RAISE EXCEPTION '% is required',a->>'label' USING ERRCODE='23514'; END IF;
    CONTINUE;
   END IF;
   IF length(value)>100 THEN RAISE EXCEPTION 'Attribute value is too long' USING ERRCODE='23514'; END IF;
   IF a->>'type'='SELECT' AND NOT (a->'options' ? value) THEN RAISE EXCEPTION 'Invalid selection for %',a->>'label' USING ERRCODE='23514'; END IF;
   IF a->>'type'='BOOLEAN' AND value NOT IN('true','false') THEN RAISE EXCEPTION 'Invalid boolean value' USING ERRCODE='23514'; END IF;
   IF a->>'type'='NUMBER' THEN
    IF value!~'^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)$' THEN RAISE EXCEPTION 'Invalid numeric attribute' USING ERRCODE='23514'; END IF;
    number_value:=value::numeric;
    IF number_value<(a->>'minimum')::numeric OR number_value>(a->>'maximum')::numeric THEN RAISE EXCEPTION 'Numeric attribute outside limits' USING ERRCODE='23514'; END IF;
   END IF;
   IF a->>'type'='DATE' THEN
    IF value!~'^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN RAISE EXCEPTION 'Invalid date attribute' USING ERRCODE='23514'; END IF;
    BEGIN date_value:=value::date; EXCEPTION WHEN OTHERS THEN RAISE EXCEPTION 'Invalid date attribute' USING ERRCODE='23514'; END;
   END IF;
   IF NOT (r->'rule'->>'variantAxis')::boolean AND EXISTS(
    SELECT 1 FROM public.product_variants v WHERE v.tenant_id=tid AND v.product_id=pid
    AND btrim(COALESCE(v.attributes->>(a->>'code'),''))<>value
   ) THEN RAISE EXCEPTION 'Common product attributes must match across variants' USING ERRCODE='23514'; END IF;
  END LOOP;
  IF axes IS NOT NULL THEN
   SELECT jsonb_object_agg(k,row_value.attributes->>k) INTO combo FROM unnest(axes) k;
   IF combo=ANY(combos) THEN RAISE EXCEPTION 'Duplicate variant option combination' USING ERRCODE='23514'; END IF;
   combos:=array_append(combos,combo);
  END IF;
 END LOOP;
 -- Saved option identities cannot be relabeled into different stock items.
 IF TG_TABLE_NAME='product_variants' AND TG_OP='UPDATE' AND axes IS NOT NULL THEN
  IF EXISTS(SELECT 1 FROM unnest(axes) k WHERE OLD.attributes ? k AND OLD.attributes->>k IS DISTINCT FROM NEW.attributes->>k) THEN
   RAISE EXCEPTION 'Saved variant options cannot change; add a new variant' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NULL;
END $$;
DROP TRIGGER IF EXISTS category_attributes_validate ON public.product_variants;
CREATE CONSTRAINT TRIGGER category_attributes_validate AFTER INSERT OR UPDATE ON public.product_variants
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.sf_validate_category_attributes();
DROP TRIGGER IF EXISTS category_attributes_validate ON public.products;
CREATE CONSTRAINT TRIGGER category_attributes_validate AFTER INSERT OR UPDATE ON public.products
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.sf_validate_category_attributes();

REVOKE ALL ON FUNCTION public.sf_save_catalog_schema(jsonb,bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_save_catalog_schema(jsonb,bigint) TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_validate_category_attributes() FROM PUBLIC;
COMMIT;
