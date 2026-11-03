-- MANUAL ONLY. Back up the database and stop writers; apply after 067 as superuser.
-- Adds provenance and durable reservations only; no historical identifiers or stock are rewritten.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 068 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.catalog_identifier_ledger') IS NULL THEN RAISE EXCEPTION 'Apply 067 first'; END IF;
 IF to_regclass('public.unit_identifier_reservations') IS NOT NULL THEN RAISE EXCEPTION '068 already installed'; END IF;
END $$;
LOCK TABLE public.product_units IN ACCESS EXCLUSIVE MODE;
ALTER TABLE public.product_units ADD COLUMN serial_source varchar(20) NOT NULL DEFAULT 'LEGACY_UNKNOWN'
 CHECK(serial_source IN('MANUFACTURER','INTERNAL','LEGACY_UNKNOWN'));
COMMENT ON COLUMN public.product_units.serial_number IS 'Stable serial or internal unit identifier. serial_source records provenance; a unit barcode is a separate field.';
COMMENT ON COLUMN public.product_units.serial_source IS 'Existing serial provenance is unknown. Never infer manufacturer serials from a barcode or identifier pattern.';
CREATE SEQUENCE public.unit_identifier_number;
CREATE TABLE public.unit_identifier_requests(
 request_id uuid PRIMARY KEY,tenant_id bigint NOT NULL,profile text NOT NULL,location_id bigint NOT NULL,
 variant_id bigint NOT NULL,actor integer NOT NULL,quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 10000),
 codes text[] NOT NULL,created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE public.unit_identifier_reservations(
 code text PRIMARY KEY,request_id uuid NOT NULL REFERENCES public.unit_identifier_requests,
 tenant_id bigint NOT NULL,profile text NOT NULL,location_id bigint NOT NULL,variant_id bigint NOT NULL,
 unit_id bigint UNIQUE,created_at timestamptz NOT NULL DEFAULT now()
);
REVOKE ALL ON public.unit_identifier_requests,public.unit_identifier_reservations,public.unit_identifier_number FROM PUBLIC,stockflow_runtime;
-- Retain every reservation, including abandoned receipts. Sequence gaps are intentional.
CREATE FUNCTION public.sf_reserve_unit_identifiers(vid bigint,rid uuid,amount integer) RETURNS text[]
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=public.sf_tenant(); profile text:=public.sf_visible_business_profile(); loc bigint:=public.sf_location();
 actor integer:=public.sf_actor(); prior public.unit_identifier_requests; result text[]:=ARRAY[]::text[]; allocated_code text; n bigint;
BEGIN
 IF rid IS NULL OR rid='00000000-0000-0000-0000-000000000000'::uuid OR amount IS NULL OR amount NOT BETWEEN 1 AND 10000 OR tid<=0 OR loc<=0 OR actor<=0 OR profile IS NULL THEN
  RAISE EXCEPTION 'Choose a shop, location and valid unit identifier request' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tid::text,0));
 IF NOT EXISTS(SELECT 1 FROM public.product_variants v JOIN public.products p USING(tenant_id,product_id)
  JOIN public.categories c USING(tenant_id,category_id) WHERE v.tenant_id=tid AND v.variant_id=vid AND v.is_active AND p.is_active
  AND v.is_serial_tracked AND c.business_profile_code=profile) THEN
  RAISE EXCEPTION 'Choose an active individually tracked SKU in this shop' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended(rid::text,0));
 SELECT * INTO prior FROM public.unit_identifier_requests WHERE request_id=rid;
 IF FOUND THEN
  IF prior.tenant_id<>tid OR prior.profile<>profile OR prior.location_id<>loc OR prior.variant_id<>vid OR prior.actor<>actor OR prior.quantity<>amount THEN
   RAISE EXCEPTION 'Unit identifier request belongs to different receipt details' USING ERRCODE='23514'; END IF;
  RETURN prior.codes;
 END IF;
 WHILE cardinality(result)<amount LOOP
  n:=nextval('public.unit_identifier_number');allocated_code:='SFN'||lpad(n::text,greatest(10,length(n::text)),'0');
  -- Scan all existing identifier sources before issuing; never assume this namespace was unused.
  IF EXISTS(SELECT 1 FROM public.product_units u WHERE upper(btrim(u.serial_number))=allocated_code OR upper(btrim(u.barcode))=allocated_code OR upper(btrim(u.internal_barcode))=allocated_code OR upper(btrim(u.manufacturer_barcode))=allocated_code)
   OR EXISTS(SELECT 1 FROM public.variant_barcodes b WHERE upper(btrim(b.barcode))=allocated_code)
   OR EXISTS(SELECT 1 FROM public.product_variants v WHERE upper(btrim(v.sku))=allocated_code)
   OR EXISTS(SELECT 1 FROM public.unit_identifier_reservations r WHERE r.code=allocated_code) THEN CONTINUE; END IF;
  result:=array_append(result,allocated_code);
 END LOOP;
 INSERT INTO public.unit_identifier_requests VALUES(rid,tid,profile,loc,vid,actor,amount,result,now());
 INSERT INTO public.unit_identifier_reservations(code,request_id,tenant_id,profile,location_id,variant_id)
  SELECT x,rid,tid,profile,loc,vid FROM unnest(result) x;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION public.sf_reserve_unit_identifiers(bigint,uuid,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_reserve_unit_identifiers(bigint,uuid,integer) TO stockflow_runtime;

CREATE FUNCTION public.sf_unit_identifier_source() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE reservation public.unit_identifier_reservations;
BEGIN
 IF TG_OP='UPDATE' THEN
  IF NEW.serial_number IS DISTINCT FROM OLD.serial_number OR NEW.serial_source IS DISTINCT FROM OLD.serial_source
   OR (OLD.internal_barcode IS NOT NULL AND NEW.internal_barcode IS DISTINCT FROM OLD.internal_barcode)
   OR (OLD.manufacturer_barcode IS NOT NULL AND NEW.manufacturer_barcode IS DISTINCT FROM OLD.manufacturer_barcode)
   OR (OLD.barcode IS NOT NULL AND NEW.barcode IS DISTINCT FROM OLD.barcode)
   OR NEW.product_unit_id IS DISTINCT FROM OLD.product_unit_id OR NEW.tenant_id IS DISTINCT FROM OLD.tenant_id OR NEW.product_id IS DISTINCT FROM OLD.product_id OR NEW.variant_id IS DISTINCT FROM OLD.variant_id THEN
   RAISE EXCEPTION 'Saved unit identifiers and their source cannot change; use a reviewed correction' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 SELECT * INTO reservation FROM public.unit_identifier_reservations WHERE code=upper(btrim(NEW.serial_number)) FOR UPDATE;
 IF FOUND THEN
  IF reservation.tenant_id<>NEW.tenant_id OR reservation.profile IS DISTINCT FROM public.sf_visible_business_profile()
   OR reservation.location_id<>NEW.location_id OR reservation.variant_id<>NEW.variant_id OR reservation.unit_id IS NOT NULL THEN
   RAISE EXCEPTION 'Internal unit identifier is unavailable for this shop, SKU or receipt' USING ERRCODE='23514'; END IF;
  NEW.serial_source:='INTERNAL';
  UPDATE public.unit_identifier_reservations SET unit_id=NEW.product_unit_id WHERE code=reservation.code;
 ELSE
  NEW.serial_source:='MANUFACTURER';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER z_unit_identifier_source BEFORE INSERT OR UPDATE ON public.product_units
 FOR EACH ROW EXECUTE FUNCTION public.sf_unit_identifier_source();
REVOKE ALL ON FUNCTION public.sf_unit_identifier_source() FROM PUBLIC;
COMMIT;
