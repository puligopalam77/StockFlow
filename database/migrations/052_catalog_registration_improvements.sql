-- MANUAL ONLY: back up, stop application/workers; run after 051 as PostgreSQL superuser.
-- Existing product/variant identities, attribute schemas, stock and history are retained.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 052 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='profile_data_visibility_enabled') THEN RAISE EXCEPTION 'Apply 051 first'; END IF;
 IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='catalog_registration_enhancements_enabled') THEN RAISE EXCEPTION '052 is already installed'; END IF;
END $$;
ALTER TABLE public.brands ADD COLUMN IF NOT EXISTS aliases text[] NOT NULL DEFAULT '{}';
CREATE TABLE public.variant_supplier_identifiers(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),variant_id bigint NOT NULL,supplier_id integer NOT NULL,
 supplier_sku varchar(100) NOT NULL CHECK(length(btrim(supplier_sku))>0),
 PRIMARY KEY(tenant_id,variant_id,supplier_id),
 FOREIGN KEY(tenant_id,variant_id) REFERENCES public.product_variants(tenant_id,variant_id),
 FOREIGN KEY(tenant_id,supplier_id) REFERENCES public.suppliers(tenant_id,supplier_id)
);
CREATE UNIQUE INDEX ux_variant_supplier_sku ON public.variant_supplier_identifiers(tenant_id,supplier_id,lower(btrim(supplier_sku)));
ALTER TABLE public.variant_supplier_identifiers ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.variant_supplier_identifiers FORCE ROW LEVEL SECURITY;
CREATE POLICY supplier_item_tenant ON public.variant_supplier_identifiers USING(tenant_id=public.sf_tenant()) WITH CHECK(tenant_id=public.sf_tenant());
CREATE POLICY supplier_item_profile ON public.variant_supplier_identifiers AS RESTRICTIVE TO stockflow_runtime
 USING(public.sf_transfer_permission('SUPPLIERS','VIEW') AND EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=variant_supplier_identifiers.tenant_id AND v.variant_id=variant_supplier_identifiers.variant_id))
 WITH CHECK(public.sf_transfer_permission('SUPPLIERS','VIEW') AND EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=variant_supplier_identifiers.tenant_id AND v.variant_id=variant_supplier_identifiers.variant_id)
  AND EXISTS(SELECT 1 FROM public.suppliers s WHERE s.tenant_id=variant_supplier_identifiers.tenant_id AND s.supplier_id=variant_supplier_identifiers.supplier_id));
GRANT SELECT,INSERT,UPDATE,DELETE ON public.variant_supplier_identifiers TO stockflow_runtime;
-- NULL preserves legacy rows without rewriting them. Reads derive their original
-- Active/Inactive state; new/edited rows get an explicit status.
ALTER TABLE public.products ADD COLUMN IF NOT EXISTS product_status varchar(20)
 CHECK(product_status IN('ACTIVE','INACTIVE','DISCONTINUED'));
CREATE FUNCTION public.sf_product_status_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_OP='UPDATE' AND NEW.product_status IS NOT DISTINCT FROM OLD.product_status AND NEW.is_active IS DISTINCT FROM OLD.is_active THEN
  NEW.product_status:=CASE WHEN NEW.is_active THEN 'ACTIVE' ELSE 'INACTIVE' END;
 ELSE NEW.product_status:=COALESCE(NEW.product_status,CASE WHEN NEW.is_active THEN 'ACTIVE' ELSE 'INACTIVE' END); END IF;
 NEW.is_active:=NEW.product_status='ACTIVE'; RETURN NEW;
END $$;
CREATE TRIGGER aa_product_status_guard BEFORE INSERT OR UPDATE ON public.products FOR EACH ROW EXECUTE FUNCTION public.sf_product_status_guard();
REVOKE ALL ON FUNCTION public.sf_product_status_guard() FROM PUBLIC;
CREATE OR REPLACE FUNCTION public.sf_brand_alias_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE identifier text; identifiers text[];
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 identifiers:=array_append(NEW.aliases,NEW.brand_name);
 IF cardinality(NEW.aliases)>20 OR EXISTS(SELECT 1 FROM unnest(identifiers) v WHERE v IS NULL OR length(btrim(v)) NOT BETWEEN 2 AND 100)
  OR (SELECT count(*)<>count(DISTINCT lower(btrim(v))) FROM unnest(identifiers) v) THEN
  RAISE EXCEPTION 'Use distinct brand names/aliases of 2 to 100 characters, with at most 20 aliases' USING ERRCODE='23514'; END IF;
 FOREACH identifier IN ARRAY identifiers LOOP
  IF EXISTS(SELECT 1 FROM public.brands b WHERE b.tenant_id=NEW.tenant_id AND b.brand_id<>NEW.brand_id
   AND b.business_profile_code=NEW.business_profile_code
   AND (lower(btrim(b.brand_name))=lower(btrim(identifier)) OR EXISTS(SELECT 1 FROM unnest(b.aliases) a WHERE lower(btrim(a))=lower(btrim(identifier))))) THEN
   RAISE EXCEPTION 'Brand name or alias already exists' USING ERRCODE='23505'; END IF;
 END LOOP;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_brand_alias_guard ON public.brands;
CREATE TRIGGER aa_brand_alias_guard BEFORE INSERT OR UPDATE ON public.brands FOR EACH ROW EXECUTE FUNCTION public.sf_brand_alias_guard();
REVOKE ALL ON FUNCTION public.sf_brand_alias_guard() FROM PUBLIC;

-- Brand links are recommendations, not exclusive category restrictions. Active
-- brand/profile visibility and tenant FKs continue to be enforced independently.
CREATE OR REPLACE FUNCTION public.sf_check_product_brand_category() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF NEW.is_active AND NEW.brand_id IS NOT NULL AND NOT EXISTS(
  SELECT 1 FROM public.brands b WHERE b.tenant_id=NEW.tenant_id AND b.brand_id=NEW.brand_id AND b.is_active
 ) THEN RAISE EXCEPTION 'Choose an active brand in this shop setup' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION public.sf_protect_brand_category() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||COALESCE(NEW.tenant_id,OLD.tenant_id)::text,0));
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Replace brand links using insert/delete' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.categories WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id AND is_active)
  THEN RAISE EXCEPTION 'Choose an active category in this shop setup' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;

CREATE OR REPLACE FUNCTION public.sf_used_category_parent_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF NEW.parent_category_id IS DISTINCT FROM OLD.parent_category_id AND EXISTS(
  WITH RECURSIVE tree AS (
   SELECT OLD.category_id id UNION ALL SELECT c.category_id FROM public.categories c JOIN tree t ON c.parent_category_id=t.id WHERE c.tenant_id=OLD.tenant_id
  ) SELECT 1 FROM public.products p JOIN tree t ON p.category_id=t.id WHERE p.tenant_id=OLD.tenant_id
 ) THEN RAISE EXCEPTION 'A category containing registered products cannot be moved; use a new category' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_used_category_parent_guard ON public.categories;
CREATE TRIGGER aa_used_category_parent_guard BEFORE UPDATE ON public.categories FOR EACH ROW EXECUTE FUNCTION public.sf_used_category_parent_guard();
REVOKE ALL ON FUNCTION public.sf_used_category_parent_guard() FROM PUBLIC;

-- Normalize new rule defaults when comparing old/new schemas. Merely loading and
-- saving legacy rules does not relabel them or unlock configured product identities.
CREATE OR REPLACE FUNCTION public.sf_category_attribute_config(doc jsonb,cat integer) RETURNS jsonb
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT COALESCE(jsonb_agg(jsonb_build_object('rule',jsonb_build_object(
  'code',f->>'code','required',f->'required','variantAxis',f->'variantAxis',
  'visible',COALESCE(f->'visible','true'::jsonb),'searchable',COALESCE(f->'searchable','true'::jsonb),
  'defaultValue',COALESCE(f->'defaultValue','null'::jsonb)),'attribute',a) ORDER BY f->>'code'),'[]'::jsonb)
 FROM jsonb_array_elements(COALESCE(doc->'categories','[]')) c,
 jsonb_array_elements(c->'fields') f JOIN jsonb_array_elements(COALESCE(doc->'attributes','[]')) a ON a->>'code'=f->>'code'
 WHERE (c->>'categoryId')::integer=cat
$$;

CREATE OR REPLACE FUNCTION public.sf_catalog_default_values() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE rule jsonb; doc jsonb;
BEGIN
 SELECT s.definition INTO doc FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
 JOIN public.catalog_attribute_schemas s ON s.tenant_id=c.tenant_id AND s.business_profile_code=c.business_profile_code
 WHERE p.tenant_id=NEW.tenant_id AND p.product_id=NEW.product_id;
 FOR rule IN SELECT x->'rule' FROM jsonb_array_elements(public.sf_category_attribute_config(doc,
  (SELECT category_id FROM public.products WHERE tenant_id=NEW.tenant_id AND product_id=NEW.product_id))) x LOOP
  IF NULLIF(btrim(NEW.attributes->>(rule->>'code')),'') IS NULL AND NULLIF(rule->>'defaultValue','') IS NOT NULL THEN
   NEW.attributes:=jsonb_set(NEW.attributes,ARRAY[rule->>'code'],to_jsonb(rule->>'defaultValue'),true);
  END IF;
 END LOOP;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_catalog_default_values ON public.product_variants;
CREATE TRIGGER aa_catalog_default_values BEFORE INSERT OR UPDATE ON public.product_variants FOR EACH ROW EXECUTE FUNCTION public.sf_catalog_default_values();
REVOKE ALL ON FUNCTION public.sf_catalog_default_values() FROM PUBLIC;

CREATE OR REPLACE FUNCTION public.sf_searchable_attributes(cat integer,attrs jsonb) RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT COALESCE(string_agg(v.value,' '),'') FROM jsonb_each_text(attrs) v
 WHERE NOT EXISTS(SELECT 1 FROM public.catalog_attribute_schemas s,
  jsonb_array_elements(s.definition->'categories') c,jsonb_array_elements(c->'fields') f
  WHERE s.tenant_id=public.sf_tenant() AND s.business_profile_code=public.sf_current_business_profile()
  AND (c->>'categoryId')::integer=cat AND f->>'code'=v.key AND COALESCE((f->>'searchable')::boolean,true)=false)
$$;
REVOKE ALL ON FUNCTION public.sf_searchable_attributes(integer,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_searchable_attributes(integer,jsonb) TO stockflow_runtime;

-- Additional schema/value validation functions follow; retained from 048 with
-- new TEXT_LIST values and field policy checks, preserving all ownership checks.

CREATE OR REPLACE FUNCTION public.sf_catalog_attribute_value_valid(a jsonb,value text) RETURNS boolean
LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE parts text[]; n numeric; d date;
BEGIN
 IF value='' THEN RETURN true; END IF;
 IF length(value) > (CASE WHEN a->>'type'='TEXT_LIST' THEN 1000 ELSE 100 END) THEN RETURN false; END IF;
 IF a->>'type'='TEXT_LIST' THEN
  parts:=string_to_array(value,';');
  RETURN cardinality(parts)<=30 AND NOT EXISTS(SELECT 1 FROM unnest(parts) p WHERE length(btrim(p)) NOT BETWEEN 1 AND 100 OR p ~ '[[:cntrl:]]')
   AND (SELECT count(*)=count(DISTINCT lower(btrim(p))) FROM unnest(parts) p);
 ELSIF a->>'type'='SELECT' THEN RETURN a->'options' ? value;
 ELSIF a->>'type'='BOOLEAN' THEN RETURN value IN('true','false');
 ELSIF a->>'type'='DATE' THEN
  IF value!~'^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN RETURN false; END IF;
  d:=value::date; RETURN true;
 ELSIF a->>'type'='NUMBER' THEN
  IF value!~'^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)$' THEN RETURN false; END IF;
  n:=value::numeric; RETURN (a->>'minimum' IS NULL OR n>=(a->>'minimum')::numeric) AND (a->>'maximum' IS NULL OR n<=(a->>'maximum')::numeric);
 END IF;
 RETURN value!~'[[:cntrl:]]';
EXCEPTION WHEN invalid_text_representation OR datetime_field_overflow THEN RETURN false;
END $$;
REVOKE ALL ON FUNCTION public.sf_catalog_attribute_value_valid(jsonb,text) FROM PUBLIC;

CREATE OR REPLACE FUNCTION public.sf_duplicate_product_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=NEW.tenant_id AND p.product_id<>NEW.product_id
  AND p.category_id=NEW.category_id AND p.brand_id IS NOT DISTINCT FROM NEW.brand_id
  AND lower(regexp_replace(btrim(p.product_name),'\s+',' ','g'))=lower(regexp_replace(btrim(NEW.product_name),'\s+',' ','g'))
  AND lower(btrim(COALESCE(p.model,'')))=lower(btrim(COALESCE(NEW.model,'')))) THEN
   RAISE EXCEPTION 'Product already exists; edit it or add a variant instead' USING ERRCODE='23505'; END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_duplicate_product_guard ON public.products;
CREATE TRIGGER aa_duplicate_product_guard BEFORE INSERT ON public.products FOR EACH ROW EXECUTE FUNCTION public.sf_duplicate_product_guard();
REVOKE ALL ON FUNCTION public.sf_duplicate_product_guard() FROM PUBLIC;

CREATE FUNCTION public.sf_manufacturer_part_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE number text:=NULLIF(lower(btrim(NEW.attributes->>'manufacturer_part_number')),'');
BEGIN
 IF number IS NULL THEN RETURN NEW; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF EXISTS(SELECT 1 FROM public.products own JOIN public.categories oc USING(tenant_id,category_id)
  JOIN public.products p ON p.tenant_id=own.tenant_id AND p.brand_id=own.brand_id AND p.product_id<>own.product_id
  JOIN public.categories pc ON pc.tenant_id=p.tenant_id AND pc.category_id=p.category_id AND pc.business_profile_code=oc.business_profile_code
  JOIN public.product_variants v ON v.tenant_id=p.tenant_id AND v.product_id=p.product_id
  WHERE own.tenant_id=NEW.tenant_id AND own.product_id=NEW.product_id
   AND lower(btrim(v.attributes->>'manufacturer_part_number'))=number) THEN
  RAISE EXCEPTION 'Manufacturer part number is already registered for this brand' USING ERRCODE='23505'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER ab_manufacturer_part_guard BEFORE INSERT OR UPDATE ON public.product_variants FOR EACH ROW EXECUTE FUNCTION public.sf_manufacturer_part_guard();
REVOKE ALL ON FUNCTION public.sf_manufacturer_part_guard() FROM PUBLIC;

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
   OR COALESCE(a->>'type','') NOT IN('TEXT','TEXT_LIST','NUMBER','DATE','BOOLEAN','SELECT') OR jsonb_typeof(a->'options') IS DISTINCT FROM 'array'
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
   IF (f ? 'visible' AND jsonb_typeof(f->'visible') IS DISTINCT FROM 'boolean')
    OR (f ? 'searchable' AND jsonb_typeof(f->'searchable') IS DISTINCT FROM 'boolean')
    OR (f ? 'defaultValue' AND jsonb_typeof(f->'defaultValue') NOT IN('string','null')) THEN
    RAISE EXCEPTION 'Invalid visibility, search or default policy' USING ERRCODE='23514'; END IF;
   IF NOT COALESCE((f->>'visible')::boolean,true) AND
    ((f->>'variantAxis')::boolean OR (f->>'required')::boolean AND NULLIF(btrim(f->>'defaultValue'),'') IS NULL) THEN
    RAISE EXCEPTION 'Hidden fields cannot create variants; hidden required fields need a default' USING ERRCODE='23514'; END IF;
   IF f->>'defaultValue' IS NOT NULL AND NOT public.sf_catalog_attribute_value_valid(a,f->>'defaultValue') THEN
    RAISE EXCEPTION 'Invalid default attribute value' USING ERRCODE='23514'; END IF;
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
   IF length(value) > (CASE WHEN a->>'type'='TEXT_LIST' THEN 1000 ELSE 100 END) THEN RAISE EXCEPTION 'Attribute value is too long' USING ERRCODE='23514'; END IF;
   IF a->>'type'='TEXT_LIST' AND NOT public.sf_catalog_attribute_value_valid(a,value) THEN RAISE EXCEPTION 'Invalid multiple-value attribute' USING ERRCODE='23514'; END IF;
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

-- Extend only unused Bike categories. Never change field definitions or required
-- rules already adopted by a product. Values are entered from verified labels;
-- no fitment, certification, year or quantity is inferred from a product name.
CREATE FUNCTION public.sf_extend_bike_registration_fields(tid bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE doc jsonb; previous jsonb; definition jsonb; category record; mapping jsonb; mappings jsonb; fields jsonb; codes text[]; code text; requires_fitment boolean;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 SELECT s.definition INTO doc FROM public.catalog_attribute_schemas s WHERE s.tenant_id=tid AND s.business_profile_code='BIKE_ACCESSORIES' FOR UPDATE;
 IF doc IS NULL THEN RETURN; END IF;
 previous:=doc;
 FOR definition IN SELECT * FROM jsonb_array_elements('[
  {"code":"vehicle_compatibility","label":"Compatible vehicles (make / model / generation or years / position)","type":"TEXT_LIST","options":[],"minimum":null,"maximum":null},
  {"code":"helmet_type","label":"Helmet type","type":"SELECT","options":["Full Face","Half Face","Modular","Off Road"],"minimum":null,"maximum":null},
  {"code":"safety_certification","label":"Safety certification","type":"TEXT","options":[],"minimum":null,"maximum":null},
  {"code":"part_position","label":"Part position","type":"SELECT","options":["Front","Rear","Left","Right","Universal"],"minimum":null,"maximum":null}
 ]'::jsonb) LOOP
  IF jsonb_array_length(doc->'attributes')<30 AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(doc->'attributes') a WHERE a->>'code'=definition->>'code') THEN
   doc:=jsonb_set(doc,'{attributes}',doc->'attributes'||jsonb_build_array(definition));
  END IF;
 END LOOP;
 FOR category IN SELECT c.category_id,c.category_name FROM public.categories c WHERE c.tenant_id=tid AND c.business_profile_code='BIKE_ACCESSORIES'
  AND NOT EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=c.tenant_id AND p.category_id=c.category_id) LOOP
  codes:=CASE WHEN category.category_name='Helmets' THEN ARRAY['helmet_type','safety_certification']
   WHEN category.category_name IN('Bike Spare Parts','Head Lights','Brake Parts','Chains & Sprockets','Side Guards','Mirrors') THEN ARRAY['vehicle_compatibility','part_position'] ELSE ARRAY[]::text[] END;
  SELECT c INTO mapping FROM jsonb_array_elements(doc->'categories') c WHERE (c->>'categoryId')::integer=category.category_id;
  mapping:=COALESCE(mapping,jsonb_build_object('categoryId',category.category_id,'fields','[]'::jsonb));
  requires_fitment:=false;
  IF 'vehicle_compatibility'=ANY(codes) AND EXISTS(SELECT 1 FROM jsonb_array_elements(doc->'attributes') a WHERE a->>'code'='vehicle_compatibility' AND a->>'type'='TEXT_LIST') THEN
   SELECT COALESCE(bool_or(COALESCE((f->>'required')::boolean,false)),false) INTO requires_fitment
    FROM jsonb_array_elements(mapping->'fields') f WHERE f->>'code'='compatible_models';
   SELECT COALESCE(jsonb_agg(f),'[]'::jsonb) INTO fields FROM jsonb_array_elements(mapping->'fields') f
    WHERE f->>'code' NOT IN('vehicle_make','compatible_models','model_years');
   mapping:=jsonb_set(mapping,'{fields}',fields);
  END IF;
  FOREACH code IN ARRAY codes LOOP
   IF jsonb_array_length(mapping->'fields')<30 AND EXISTS(SELECT 1 FROM jsonb_array_elements(doc->'attributes') a WHERE a->>'code'=code)
    AND NOT EXISTS(SELECT 1 FROM jsonb_array_elements(mapping->'fields') f WHERE f->>'code'=code) THEN
    mapping:=jsonb_set(mapping,'{fields}',mapping->'fields'||jsonb_build_array(jsonb_build_object('code',code,'required',code='vehicle_compatibility' AND requires_fitment,'variantAxis',false,'visible',true,'searchable',true,'defaultValue',NULL)));
   END IF;
  END LOOP;
  IF jsonb_array_length(mapping->'fields')>0 THEN
   SELECT COALESCE(jsonb_agg(c),'[]'::jsonb) INTO mappings FROM jsonb_array_elements(doc->'categories') c WHERE (c->>'categoryId')::integer<>category.category_id;
   doc:=jsonb_set(doc,'{categories}',mappings||jsonb_build_array(mapping));
  END IF;
 END LOOP;
 IF doc IS DISTINCT FROM previous THEN
  UPDATE public.catalog_attribute_schemas SET definition=doc,version=version+1,updated_at=now() WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES';
  INSERT INTO public.audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,reason,old_values,new_values)
   VALUES(tid,NULL,'Category attributes','BASELINE','catalog_attribute_schemas',tid,'052: registration templates for unused Bike categories',previous,doc);
 END IF;
END $$;
REVOKE ALL ON FUNCTION public.sf_extend_bike_registration_fields(bigint) FROM PUBLIC;
DO $$ DECLARE tid bigint; BEGIN
 FOR tid IN SELECT tenant_id FROM public.catalog_attribute_schemas WHERE business_profile_code='BIKE_ACCESSORIES' LOOP
  PERFORM public.sf_extend_bike_registration_fields(tid);
 END LOOP;
END $$;
ALTER FUNCTION public.sf_seed_bike_catalog(bigint) RENAME TO sf_seed_bike_catalog_050;
CREATE FUNCTION public.sf_seed_bike_catalog(tid bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 PERFORM public.sf_seed_bike_catalog_050(tid);
 PERFORM public.sf_extend_bike_registration_fields(tid);
END $$;
REVOKE ALL ON FUNCTION public.sf_seed_bike_catalog(bigint),public.sf_seed_bike_catalog_050(bigint) FROM PUBLIC;

ALTER TABLE public.tenants ADD COLUMN IF NOT EXISTS catalog_registration_enhancements_enabled boolean NOT NULL DEFAULT true;
COMMIT;
