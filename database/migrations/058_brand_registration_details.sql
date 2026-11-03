-- MANUAL ONLY. Back up, stop application/workers; run after 057 as PostgreSQL superuser.
-- Existing brand IDs, codes, category links, products and history are retained.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 058 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='display_order') THEN RAISE EXCEPTION 'Apply 057 first'; END IF;
END $$;
ALTER TABLE public.brands ALTER COLUMN brand_code DROP EXPRESSION IF EXISTS;
ALTER TABLE public.brands ADD COLUMN IF NOT EXISTS official_website varchar(2048);
ALTER TABLE public.brands ADD COLUMN IF NOT EXISTS brand_logo jsonb;
CREATE OR REPLACE FUNCTION public.sf_brand_logo_valid(logo jsonb) RETURNS boolean
LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE bytes bytea; mime text;
BEGIN
 IF logo IS NULL THEN RETURN true; END IF;
 IF jsonb_typeof(logo) IS DISTINCT FROM 'object' OR length(COALESCE(logo->>'fileName','')) NOT BETWEEN 1 AND 255 OR length(COALESCE(logo->>'data',''))>2700000 THEN RETURN false; END IF;
 bytes:=decode(COALESCE(logo->>'data',''),'base64');mime:=logo->>'contentType';
 IF octet_length(bytes) NOT BETWEEN 1 AND 2000000 THEN RETURN false; END IF;
 RETURN CASE mime WHEN 'image/png' THEN encode(substring(bytes FROM 1 FOR 8),'hex')='89504e470d0a1a0a'
 WHEN 'image/jpeg' THEN encode(substring(bytes FROM 1 FOR 3),'hex')='ffd8ff'
 WHEN 'image/webp' THEN substring(bytes FROM 1 FOR 4)=decode('52494646','hex') AND substring(bytes FROM 9 FOR 4)=decode('57454250','hex') ELSE false END;
EXCEPTION WHEN OTHERS THEN RETURN false;
END $$;
REVOKE ALL ON FUNCTION public.sf_brand_logo_valid(jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_brand_logo_valid(jsonb) TO stockflow_runtime;
ALTER TABLE public.brands ADD CONSTRAINT ck_brand_registration_logo CHECK(public.sf_brand_logo_valid(brand_logo));
ALTER TABLE public.brands ADD CONSTRAINT ck_brand_registration_website CHECK(official_website IS NULL OR official_website ~ '^https?://[^[:space:]]+$');
CREATE OR REPLACE FUNCTION public.sf_brand_registration_details() RETURNS boolean LANGUAGE sql IMMUTABLE AS $$ SELECT true $$;
REVOKE ALL ON FUNCTION public.sf_brand_registration_details() FROM PUBLIC;
CREATE OR REPLACE FUNCTION public.sf_brand_registration_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE identifiers text[]; identifier text;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 NEW.brand_code:=COALESCE(NULLIF(upper(btrim(NEW.brand_code)),''),'BRD'||lpad(NEW.brand_id::text,greatest(4,length(NEW.brand_id::text)),'0'));
 IF NEW.brand_code!~'^[A-Z0-9][A-Z0-9-]{1,19}$' THEN RAISE EXCEPTION 'Use a brand code of 2 to 20 letters, numbers or hyphens' USING ERRCODE='23514'; END IF;
 -- Preserve pre-existing cross-profile duplicates when their identities are unchanged.
 IF TG_OP='INSERT' OR NEW.brand_name IS DISTINCT FROM OLD.brand_name OR NEW.aliases IS DISTINCT FROM OLD.aliases THEN
  identifiers:=array_append(NEW.aliases,NEW.brand_name);
  FOREACH identifier IN ARRAY identifiers LOOP
   IF EXISTS(SELECT 1 FROM public.brands b WHERE b.tenant_id=NEW.tenant_id AND b.brand_id<>NEW.brand_id
    AND (lower(regexp_replace(btrim(b.brand_name),'\s+',' ','g'))=lower(regexp_replace(btrim(identifier),'\s+',' ','g'))
     OR EXISTS(SELECT 1 FROM unnest(b.aliases) a WHERE lower(regexp_replace(btrim(a),'\s+',' ','g'))=lower(regexp_replace(btrim(identifier),'\s+',' ','g')))))
   THEN RAISE EXCEPTION 'Brand name or alias is already registered in this shop' USING ERRCODE='23505'; END IF;
  END LOOP;
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_brand_registration_guard ON public.brands;
CREATE TRIGGER aa_brand_registration_guard BEFORE INSERT OR UPDATE ON public.brands FOR EACH ROW EXECUTE FUNCTION public.sf_brand_registration_guard();
REVOKE ALL ON FUNCTION public.sf_brand_registration_guard() FROM PUBLIC;
-- Keep logo metadata/change fingerprints in audit history without copying the
-- image payload on every brand edit. Existing audit records remain unchanged.
CREATE OR REPLACE FUNCTION public.sf_audit_brand_registration_details() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE old_doc jsonb; new_doc jsonb;
BEGIN
 new_doc:=to_jsonb(NEW);
 IF NEW.brand_logo IS NOT NULL THEN new_doc:=jsonb_set(new_doc,'{brand_logo}',(NEW.brand_logo-'data')||jsonb_build_object('fingerprint',md5(NEW.brand_logo->>'data'))); END IF;
 IF TG_OP='UPDATE' THEN
  old_doc:=to_jsonb(OLD);
  IF OLD.brand_logo IS NOT NULL THEN old_doc:=jsonb_set(old_doc,'{brand_logo}',(OLD.brand_logo-'data')||jsonb_build_object('fingerprint',md5(OLD.brand_logo->>'data'))); END IF;
 END IF;
 INSERT INTO public.audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,old_values,new_values)
 VALUES(NEW.tenant_id,NULLIF(public.sf_actor(),0),'Brands',TG_OP,'brands',NEW.brand_id,old_doc,new_doc);
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS audit_brand_registration ON public.brands;
CREATE TRIGGER audit_brand_registration AFTER INSERT OR UPDATE ON public.brands FOR EACH ROW EXECUTE FUNCTION public.sf_audit_brand_registration_details();
REVOKE ALL ON FUNCTION public.sf_audit_brand_registration_details() FROM PUBLIC;
-- Existing tenant code uniqueness, audit triggers, RLS and category FKs continue to apply.
COMMIT;
