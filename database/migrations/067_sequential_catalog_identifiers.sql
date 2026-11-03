-- MANUAL ONLY: back up the complete database, stop writers, apply after 066 as superuser.
-- Run 067_sku_prefix_dry_run.sql first. Resolve duplicate suggestions with a temporary
-- sf_sku_prefix_plan(tenant_id bigint,category_id integer,prefix text) before BEGIN.
-- Existing identifiers and transaction data are never rewritten.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 067 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.vehicle_models') IS NULL THEN RAISE EXCEPTION 'Apply 066 first'; END IF;
 IF to_regclass('public.catalog_identifier_ledger') IS NOT NULL THEN RAISE EXCEPTION '067 already installed'; END IF;
END $$;
LOCK TABLE public.categories,public.products,public.product_variants IN ACCESS EXCLUSIVE MODE;
CREATE TEMP TABLE IF NOT EXISTS sf_sku_prefix_plan(tenant_id bigint,category_id integer,prefix text) ON COMMIT DROP;
CREATE TEMP TABLE sf067_plan ON COMMIT DROP AS
 SELECT c.tenant_id,c.category_id,c.business_profile_code,
 COALESCE(p.prefix,upper(rpad(left(regexp_replace(c.category_name,'[^a-zA-Z0-9]','','g'),3),3,'X'))) prefix
 FROM public.categories c LEFT JOIN sf_sku_prefix_plan p USING(tenant_id,category_id);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM sf067_plan WHERE prefix !~ '^[A-Z][A-Z0-9]{2,7}$') OR EXISTS(SELECT 1 FROM sf067_plan GROUP BY tenant_id,business_profile_code,prefix HAVING count(*)>1)
 THEN RAISE EXCEPTION 'Review duplicate/invalid SKU prefixes. Supply a unique sf_sku_prefix_plan; no changes applied.'; END IF;
END $$;
ALTER TABLE public.categories ADD COLUMN sku_prefix varchar(8), ADD COLUMN sku_prefix_used boolean NOT NULL DEFAULT false;
ALTER TABLE public.products ADD COLUMN identifier_profile varchar(40);
ALTER TABLE public.product_variants ADD COLUMN identifier_profile varchar(40);
-- Metadata backfill must not fire commercial synchronization or audit triggers.
CREATE TEMP TABLE sf067_triggers ON COMMIT DROP AS SELECT tgrelid::regclass relation,tgname,tgenabled FROM pg_trigger
 WHERE NOT tgisinternal AND tgrelid IN('public.categories'::regclass,'public.products'::regclass,'public.product_variants'::regclass);
DO $$ DECLARE r record; BEGIN FOR r IN SELECT * FROM sf067_triggers WHERE tgenabled<>'D' LOOP EXECUTE format('ALTER TABLE %s DISABLE TRIGGER %I',r.relation,r.tgname); END LOOP; END $$;
UPDATE public.categories c SET sku_prefix=p.prefix FROM sf067_plan p WHERE p.tenant_id=c.tenant_id AND p.category_id=c.category_id;
UPDATE public.products p SET identifier_profile=c.business_profile_code FROM public.categories c WHERE c.tenant_id=p.tenant_id AND c.category_id=p.category_id;
UPDATE public.product_variants v SET identifier_profile=p.identifier_profile FROM public.products p WHERE p.tenant_id=v.tenant_id AND p.product_id=v.product_id;
ALTER TABLE public.products ALTER COLUMN identifier_profile SET NOT NULL;
ALTER TABLE public.product_variants ALTER COLUMN identifier_profile SET NOT NULL;
ALTER TABLE public.categories ALTER COLUMN sku_prefix SET NOT NULL;
ALTER TABLE public.categories ADD CHECK(sku_prefix ~ '^[A-Z][A-Z0-9]{2,7}$');
DROP INDEX public.ux_product_parent_code;
DROP INDEX public.ux_products_sku;
DROP INDEX public.ux_variants_sku;
CREATE UNIQUE INDEX ux_product_parent_code ON public.products(tenant_id,identifier_profile,upper(btrim(parent_product_code))) WHERE parent_product_code IS NOT NULL;
CREATE UNIQUE INDEX ux_products_sku ON public.products(tenant_id,identifier_profile,upper(btrim(sku))) WHERE sku IS NOT NULL;
CREATE UNIQUE INDEX ux_variants_sku ON public.product_variants(tenant_id,identifier_profile,upper(btrim(sku)));
CREATE UNIQUE INDEX ux_category_sku_prefix ON public.categories(tenant_id,business_profile_code,sku_prefix);

CREATE TABLE public.catalog_prefix_registry(
 tenant_id bigint NOT NULL,profile text NOT NULL,prefix varchar(8) NOT NULL,category_id integer NOT NULL,
 last_number bigint NOT NULL DEFAULT 0 CHECK(last_number>=0),issued boolean NOT NULL DEFAULT false,
 PRIMARY KEY(tenant_id,profile,prefix));
INSERT INTO public.catalog_prefix_registry(tenant_id,profile,prefix,category_id)
 SELECT tenant_id,business_profile_code,sku_prefix,category_id FROM public.categories;
-- Permanent tombstones also protect manually assigned and later removed identifiers.
CREATE TABLE public.catalog_identifier_ledger(
 tenant_id bigint NOT NULL,profile text NOT NULL,code text NOT NULL,
 product_id integer,variant_id bigint,request_id uuid,owner_id integer,
 category_id integer,variant_counter bigint NOT NULL DEFAULT 0,
 created_at timestamptz NOT NULL DEFAULT now(),created_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),PRIMARY KEY(tenant_id,profile,code));
-- Stop on an existing parent/SKU ambiguity instead of silently changing identities.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM (
  SELECT tenant_id,identifier_profile profile,upper(btrim(parent_product_code)) code,product_id FROM public.products WHERE parent_product_code IS NOT NULL
  UNION ALL SELECT tenant_id,identifier_profile,upper(btrim(sku)),product_id FROM public.products WHERE NULLIF(btrim(sku),'') IS NOT NULL
  UNION ALL SELECT tenant_id,identifier_profile,upper(btrim(sku)),product_id FROM public.product_variants
 ) ids GROUP BY tenant_id,profile,code HAVING count(DISTINCT product_id)>1) THEN RAISE EXCEPTION 'Existing product code/SKU collisions require manual review'; END IF;
 IF EXISTS(SELECT 1 FROM public.products p JOIN public.product_variants v ON v.tenant_id=p.tenant_id AND v.identifier_profile=p.identifier_profile
 AND upper(btrim(v.sku))=upper(btrim(p.parent_product_code)) WHERE p.product_id<>v.product_id OR p.has_variants)
 THEN RAISE EXCEPTION 'Existing parent/SKU ambiguity requires manual review'; END IF;
END $$;
INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id,variant_id)
 SELECT tenant_id,identifier_profile,upper(btrim(sku)),product_id,variant_id FROM public.product_variants;
INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id)
 SELECT tenant_id,identifier_profile,upper(btrim(parent_product_code)),product_id FROM public.products WHERE parent_product_code IS NOT NULL
 ON CONFLICT(tenant_id,profile,code) DO NOTHING;
INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id)
 SELECT tenant_id,identifier_profile,upper(btrim(sku)),product_id FROM public.products WHERE NULLIF(btrim(sku),'') IS NOT NULL
 ON CONFLICT(tenant_id,profile,code) DO NOTHING;
-- Retain namespaces already present in legacy/manual codes, including prefixes
-- that are not selected for a current category mapping.
INSERT INTO public.catalog_prefix_registry(tenant_id,profile,prefix,category_id,last_number,issued)
 SELECT DISTINCT ON(l.tenant_id,l.profile,split_part(l.code,'-',1)) l.tenant_id,l.profile,split_part(l.code,'-',1),p.category_id,0,true
 FROM public.catalog_identifier_ledger l JOIN public.products p ON p.tenant_id=l.tenant_id AND p.product_id=l.product_id
 WHERE l.code ~ '^[A-Z][A-Z0-9]{2,7}-[0-9]{1,18}(-.*)?$'
 ORDER BY l.tenant_id,l.profile,split_part(l.code,'-',1),p.product_id
 ON CONFLICT(tenant_id,profile,prefix) DO NOTHING;
-- Scan ALL conventions resembling PREFIX-number or PREFIX-number-suffix (even
-- legacy/manual/inactive identifiers). Protected MAX initializes a durable counter
-- once; it is never used as an unlocked allocator.
UPDATE public.catalog_prefix_registry r SET last_number=q.n,issued=true FROM (
 SELECT l.tenant_id,l.profile,split_part(l.code,'-',1) prefix,max(split_part(l.code,'-',2)::bigint) n
 FROM public.catalog_identifier_ledger l WHERE l.code ~ '^[A-Z][A-Z0-9]{2,7}-[0-9]{1,18}(-.*)?$'
 GROUP BY l.tenant_id,l.profile,split_part(l.code,'-',1)) q
 WHERE r.tenant_id=q.tenant_id AND r.profile=q.profile AND r.prefix=q.prefix;

UPDATE public.categories c SET sku_prefix_used=r.issued FROM public.catalog_prefix_registry r WHERE r.tenant_id=c.tenant_id AND r.profile=c.business_profile_code AND r.prefix=c.sku_prefix;
DO $$ DECLARE r record; BEGIN FOR r IN SELECT * FROM sf067_triggers WHERE tgenabled<>'D' LOOP EXECUTE format('ALTER TABLE %s ENABLE %s TRIGGER %I',r.relation,CASE r.tgenabled WHEN 'A' THEN 'ALWAYS' WHEN 'R' THEN 'REPLICA' ELSE '' END,r.tgname); END LOOP; END $$;
CREATE FUNCTION public.sf_note_catalog_number(tid bigint,prof text,cat integer,identifier text) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF identifier ~ '^[A-Z][A-Z0-9]{2,7}-[0-9]{1,18}(-.*)?$' THEN
  INSERT INTO public.catalog_prefix_registry(tenant_id,profile,prefix,category_id,last_number,issued)
  VALUES(tid,prof,split_part(identifier,'-',1),cat,split_part(identifier,'-',2)::bigint,true)
  ON CONFLICT(tenant_id,profile,prefix) DO UPDATE SET last_number=greatest(catalog_prefix_registry.last_number,excluded.last_number),issued=true;
  UPDATE public.categories SET sku_prefix_used=true WHERE tenant_id=tid AND business_profile_code=prof AND sku_prefix=split_part(identifier,'-',1) AND NOT sku_prefix_used;
 END IF;
END $$;
CREATE FUNCTION public.sf_catalog_prefix_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE candidate text; base text; n integer:=0; r record;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF TG_OP='UPDATE' AND (NEW.tenant_id<>OLD.tenant_id OR NEW.business_profile_code<>OLD.business_profile_code) THEN RAISE EXCEPTION 'Category shop ownership cannot change'; END IF;
 IF TG_OP='UPDATE' AND NEW.sku_prefix IS NULL THEN NEW.sku_prefix:=OLD.sku_prefix; END IF;
 IF TG_OP='UPDATE' AND NEW.sku_prefix IS NOT DISTINCT FROM OLD.sku_prefix THEN RETURN NEW; END IF;
 IF TG_OP='UPDATE' AND EXISTS(SELECT 1 FROM public.catalog_prefix_registry WHERE tenant_id=OLD.tenant_id AND profile=OLD.business_profile_code AND prefix=OLD.sku_prefix AND issued) THEN RAISE EXCEPTION 'An issued SKU prefix is permanent' USING ERRCODE='23514'; END IF;
 base:=upper(rpad(left(regexp_replace(NEW.category_name,'[^a-zA-Z0-9]','','g'),3),3,'X'));
 candidate:=NULLIF(upper(btrim(NEW.sku_prefix)),'');
 IF candidate IS NULL THEN
  candidate:=base;
  WHILE EXISTS(SELECT 1 FROM public.catalog_prefix_registry WHERE tenant_id=NEW.tenant_id AND profile=NEW.business_profile_code AND prefix=candidate) LOOP n:=n+1;candidate:=base||n::text; END LOOP;
 END IF;
 IF candidate !~ '^[A-Z][A-Z0-9]{2,7}$' THEN RAISE EXCEPTION 'SKU Prefix requires 3 to 8 uppercase letters or digits, starting with a letter' USING ERRCODE='23514'; END IF;
 SELECT * INTO r FROM public.catalog_prefix_registry WHERE tenant_id=NEW.tenant_id AND profile=NEW.business_profile_code AND prefix=candidate;
 IF FOUND THEN RAISE EXCEPTION 'SKU prefix already assigned or retired; choose another prefix' USING ERRCODE='23514'; END IF;
 NEW.sku_prefix:=candidate;
 INSERT INTO public.catalog_prefix_registry VALUES(NEW.tenant_id,NEW.business_profile_code,candidate,NEW.category_id,0,false);
 RETURN NEW;
END $$;
CREATE TRIGGER a00_catalog_prefix BEFORE INSERT OR UPDATE ON public.categories FOR EACH ROW EXECUTE FUNCTION public.sf_catalog_prefix_guard();

CREATE FUNCTION public.sf_suggest_sku_prefix(category_name text) RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE base text;candidate text;n integer:=0;
BEGIN
 IF public.sf_visible_business_profile() IS NULL THEN RAISE EXCEPTION 'Return to the original shop before choosing a SKU prefix'; END IF;
 base:=upper(rpad(left(regexp_replace(category_name,'[^a-zA-Z0-9]','','g'),3),3,'X'));candidate:=base;
 WHILE EXISTS(SELECT 1 FROM public.catalog_prefix_registry WHERE tenant_id=public.sf_tenant() AND profile=public.sf_visible_business_profile() AND prefix=candidate) LOOP n:=n+1;candidate:=base||n::text; END LOOP;
 IF length(candidate)>8 THEN RAISE EXCEPTION 'Choose a different SKU prefix'; END IF;
 RETURN candidate;
END $$;

CREATE FUNCTION public.sf_reserve_product_code(cat integer,request uuid,desired text DEFAULT NULL) RETURNS text LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); prof text:=public.sf_visible_business_profile(); pref text; number_value bigint; code_value text;
BEGIN
 IF request IS NULL OR request='00000000-0000-0000-0000-000000000000'::uuid OR prof IS NULL THEN RAISE EXCEPTION 'Select the original shop and use a valid registration request' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 SELECT sku_prefix INTO pref FROM public.categories c WHERE c.tenant_id=tid AND c.business_profile_code=prof AND c.category_id=cat AND c.is_active AND NOT c.is_retired_grouping
 AND NOT EXISTS(SELECT 1 FROM public.categories ch WHERE ch.tenant_id=tid AND ch.parent_category_id=cat AND ch.is_active AND NOT ch.is_retired_grouping)
 AND (c.parent_category_id IS NULL OR EXISTS(SELECT 1 FROM public.categories p WHERE p.tenant_id=tid AND p.category_id=c.parent_category_id AND p.is_active));
 IF pref IS NULL THEN RAISE EXCEPTION 'Choose an active final category with a SKU prefix' USING ERRCODE='23514'; END IF;
 IF NULLIF(btrim(desired),'') IS NOT NULL THEN
  code_value:=upper(btrim(desired));
  IF code_value !~ '^[A-Z0-9][A-Z0-9_-]{2,39}$' THEN RAISE EXCEPTION 'Use a product code of 3 to 40 safe characters'; END IF;
  IF EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND code=code_value AND (request_id IS DISTINCT FROM request OR owner_id IS DISTINCT FROM NULLIF(current_setting('app.user_id',true),'')::integer OR category_id IS DISTINCT FROM cat OR product_id IS NOT NULL)) THEN RAISE EXCEPTION 'Product code already issued or reserved' USING ERRCODE='23514'; END IF;
  INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,request_id,owner_id,category_id) VALUES(tid,prof,code_value,request,NULLIF(current_setting('app.user_id',true),'')::integer,cat) ON CONFLICT DO NOTHING;
  PERFORM public.sf_note_catalog_number(tid,prof,cat,code_value);
  UPDATE public.catalog_prefix_registry SET issued=true,last_number=greatest(last_number,CASE WHEN code_value ~ ('^'||prefix||'-[0-9]{1,18}(-.*)?$') THEN split_part(code_value,'-',2)::bigint ELSE 0 END) WHERE tenant_id=tid AND profile=prof AND prefix=pref;
  UPDATE public.categories SET sku_prefix_used=true WHERE tenant_id=tid AND category_id=cat;
  RETURN code_value;
 END IF;
 SELECT code INTO code_value FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND request_id=request AND category_id=cat AND owner_id=NULLIF(current_setting('app.user_id',true),'')::integer ORDER BY created_at LIMIT 1;
 IF FOUND THEN RETURN code_value; END IF;
 LOOP
  UPDATE public.catalog_prefix_registry SET last_number=last_number+1,issued=true WHERE tenant_id=tid AND profile=prof AND prefix=pref RETURNING last_number INTO number_value;
  IF NOT FOUND THEN RAISE EXCEPTION 'SKU prefix is not configured'; END IF;
  code_value:=pref||'-'||lpad(number_value::text,greatest(4,length(number_value::text)),'0');
  EXIT WHEN NOT EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND (code=code_value OR starts_with(code,code_value||'-')));
 END LOOP;
 INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,request_id,owner_id,category_id) VALUES(tid,prof,code_value,request,NULLIF(current_setting('app.user_id',true),'')::integer,cat);
 UPDATE public.categories SET sku_prefix_used=true WHERE tenant_id=tid AND category_id=cat;
 RETURN code_value;
END $$;

CREATE TABLE public.catalog_variant_reservations(tenant_id bigint NOT NULL,profile text NOT NULL,operation_id uuid NOT NULL,parent_code text NOT NULL,owner_id integer NOT NULL,codes text[] NOT NULL,PRIMARY KEY(tenant_id,profile,operation_id));
CREATE FUNCTION public.sf_reserve_variant_codes(parent text,request uuid,operation uuid,amount integer,pid integer DEFAULT NULL) RETURNS text[] LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant();prof text:=public.sf_visible_business_profile();r record;out_codes text[]:=ARRAY[]::text[];n bigint;candidate text;
BEGIN
 IF amount NOT BETWEEN 1 AND 30 OR operation IS NULL OR prof IS NULL THEN RAISE EXCEPTION 'Request between 1 and 30 SKU reservations in the original shop'; END IF;
 parent:=upper(btrim(parent));
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 SELECT * INTO r FROM public.catalog_variant_reservations WHERE tenant_id=tid AND profile=prof AND operation_id=operation;
 IF FOUND THEN
  IF r.owner_id<>NULLIF(current_setting('app.user_id',true),'')::integer OR r.parent_code<>parent OR cardinality(r.codes)<>amount THEN RAISE EXCEPTION 'SKU reservation retry does not match'; END IF;
  RETURN r.codes;
 END IF;
 IF pid IS NOT NULL THEN
  IF NOT EXISTS(SELECT 1 FROM public.products WHERE tenant_id=tid AND identifier_profile=prof AND product_id=pid AND upper(parent_product_code)=parent) THEN RAISE EXCEPTION 'Product does not belong to this shop'; END IF;
 ELSE
  IF NOT EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND code=parent AND request_id=request AND owner_id=NULLIF(current_setting('app.user_id',true),'')::integer AND product_id IS NULL) THEN RAISE EXCEPTION 'Generate a product code before reserving variant SKUs'; END IF;
 END IF;
 SELECT greatest(variant_counter,COALESCE((SELECT max(substring(l.code,length(parent)+2)::bigint) FROM public.catalog_identifier_ledger l WHERE l.tenant_id=tid AND l.profile=prof AND starts_with(l.code,parent||'-') AND substring(l.code,length(parent)+2) ~ '^[0-9]{1,18}$'),0)) INTO n
 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND code=parent;
 -- substring syntax kept explicit below to avoid interpreting attributes as suffixes.
 FOR i IN 1..amount LOOP
  LOOP n:=n+1;candidate:=parent||'-'||lpad(n::text,greatest(2,length(n::text)),'0');EXIT WHEN NOT EXISTS(SELECT 1 FROM public.catalog_identifier_ledger WHERE tenant_id=tid AND profile=prof AND code=candidate); END LOOP;
  IF length(candidate)>40 THEN RAISE EXCEPTION 'Product code leaves no room for a variant suffix'; END IF;
  INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id,request_id,owner_id) VALUES(tid,prof,candidate,pid,request,NULLIF(current_setting('app.user_id',true),'')::integer);out_codes:=array_append(out_codes,candidate);
 END LOOP;
 UPDATE public.catalog_identifier_ledger SET variant_counter=n WHERE tenant_id=tid AND profile=prof AND code=parent;
 INSERT INTO public.catalog_variant_reservations VALUES(tid,prof,operation,parent,NULLIF(current_setting('app.user_id',true),'')::integer,out_codes);
 RETURN out_codes;
END $$;

CREATE FUNCTION public.sf_catalog_identifier_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE prof text;code_value text;r record;req uuid;is_parent boolean:=TG_TABLE_NAME='products';vid bigint;has_variants_value boolean:=false;category_value integer;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF is_parent THEN
  SELECT business_profile_code INTO prof FROM public.categories WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id;
  code_value:=NULLIF(upper(btrim(NEW.parent_product_code)),'');
  req:=NEW.registration_request_id;has_variants_value:=NEW.has_variants;category_value:=NEW.category_id;
 ELSE
  SELECT identifier_profile,registration_request_id,category_id INTO prof,req,category_value FROM public.products WHERE tenant_id=NEW.tenant_id AND product_id=NEW.product_id;
  code_value:=upper(btrim(NEW.sku));vid:=NEW.variant_id;
 END IF;
 IF prof IS NULL OR prof IS DISTINCT FROM public.sf_visible_business_profile() THEN RAISE EXCEPTION 'Identifier shop changed; reopen in the original shop'; END IF;
 NEW.identifier_profile:=prof;
 IF TG_OP='UPDATE' THEN
  IF OLD.tenant_id<>NEW.tenant_id OR OLD.identifier_profile<>prof THEN RAISE EXCEPTION 'Identifier shop ownership is permanent'; END IF;
  IF is_parent THEN
   IF NEW.sku IS DISTINCT FROM OLD.sku AND NEW.sku IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=NEW.tenant_id AND v.product_id=NEW.product_id AND upper(btrim(v.sku))=upper(btrim(NEW.sku))) THEN RAISE EXCEPTION 'Saved product SKU must reference its existing sellable variant'; END IF;
   IF NEW.has_variants AND NOT OLD.has_variants AND EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=NEW.tenant_id AND v.product_id=NEW.product_id AND upper(v.sku)=upper(NEW.parent_product_code)) THEN RAISE EXCEPTION 'This simple product already uses its product code as its SKU. Register a separate variant product to preserve its stock identity'; END IF;
   IF OLD.parent_product_code IS NOT NULL AND NEW.parent_product_code IS DISTINCT FROM OLD.parent_product_code THEN RAISE EXCEPTION 'Saved product codes are permanent' USING ERRCODE='23514'; END IF;
   IF NEW.parent_product_code IS NOT DISTINCT FROM OLD.parent_product_code THEN RETURN NEW; END IF;
  ELSE
   IF OLD.product_id<>NEW.product_id THEN RAISE EXCEPTION 'Saved variants cannot move between products'; END IF;
   IF NEW.sku IS NOT DISTINCT FROM OLD.sku THEN RETURN NEW; END IF;
   -- Only the automatic LEGACY-id placeholder created inside a NEW registration
   -- may be replaced. Saved identifiers require a separately reviewed migration.
   IF NOT(OLD.sku='LEGACY-'||NEW.product_id::text AND EXISTS(SELECT 1 FROM public.catalog_identifier_ledger l WHERE l.tenant_id=NEW.tenant_id AND l.profile=prof AND l.code=upper(OLD.sku) AND l.variant_id=vid AND l.created_xid=pg_current_xact_id())) THEN RAISE EXCEPTION 'Saved SKUs are permanent; add a new variant' USING ERRCODE='23514'; END IF;
  END IF;
 END IF;
 IF code_value IS NULL THEN RETURN NEW; END IF;
 SELECT * INTO r FROM public.catalog_identifier_ledger WHERE tenant_id=NEW.tenant_id AND profile=prof AND code=code_value;
 IF FOUND THEN
  IF r.product_id IS NOT NULL AND r.product_id<>NEW.product_id OR r.variant_id IS NOT NULL AND (is_parent AND (has_variants_value OR NOT EXISTS(SELECT 1 FROM public.product_variants WHERE variant_id=r.variant_id AND product_id=NEW.product_id AND is_default)) OR NOT is_parent AND r.variant_id<>vid)
   OR r.product_id IS NULL AND (r.request_id IS DISTINCT FROM req OR r.owner_id IS DISTINCT FROM NULLIF(current_setting('app.user_id',true),'')::integer) THEN RAISE EXCEPTION 'Identifier is already issued or reserved; keep your draft and choose another code' USING ERRCODE='23514'; END IF;
  IF is_parent AND r.category_id IS NOT NULL AND r.category_id<>category_value THEN RAISE EXCEPTION 'Reserved product code belongs to another category'; END IF;
  IF NOT is_parent AND EXISTS(SELECT 1 FROM public.products WHERE tenant_id=NEW.tenant_id AND product_id=NEW.product_id AND has_variants AND upper(parent_product_code)=code_value) THEN RAISE EXCEPTION 'Choose an exact variant SKU; family codes cannot be sellable variants'; END IF;
  UPDATE public.catalog_identifier_ledger SET product_id=NEW.product_id,variant_id=CASE WHEN is_parent THEN variant_id ELSE vid END WHERE tenant_id=NEW.tenant_id AND profile=prof AND code=code_value;
 ELSE
  INSERT INTO public.catalog_identifier_ledger(tenant_id,profile,code,product_id,variant_id) VALUES(NEW.tenant_id,prof,code_value,NEW.product_id,CASE WHEN is_parent THEN NULL ELSE vid END);
 END IF;
 PERFORM public.sf_note_catalog_number(NEW.tenant_id,prof,category_value,code_value);
 IF is_parent THEN NEW.parent_product_code:=code_value; ELSE NEW.sku:=code_value; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER a00_catalog_identifier BEFORE INSERT OR UPDATE ON public.products FOR EACH ROW EXECUTE FUNCTION public.sf_catalog_identifier_guard();
CREATE TRIGGER a00_catalog_identifier BEFORE INSERT OR UPDATE ON public.product_variants FOR EACH ROW EXECUTE FUNCTION public.sf_catalog_identifier_guard();
-- Only the guarded definer functions can mutate allocation state.
REVOKE ALL ON public.catalog_prefix_registry,public.catalog_identifier_ledger,public.catalog_variant_reservations FROM PUBLIC,stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_catalog_prefix_guard(),public.sf_catalog_identifier_guard(),public.sf_reserve_product_code(integer,uuid,text),public.sf_reserve_variant_codes(text,uuid,uuid,integer,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_reserve_product_code(integer,uuid,text),public.sf_reserve_variant_codes(text,uuid,uuid,integer,integer) TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_suggest_sku_prefix(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.sf_note_catalog_number(bigint,text,integer,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_suggest_sku_prefix(text) TO stockflow_runtime;
COMMIT;
