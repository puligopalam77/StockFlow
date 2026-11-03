-- MANUAL ONLY. Back up the complete database, stop application/workers; apply after 065 as PostgreSQL superuser.
-- Adds registration metadata/guards only. Existing IDs, SKUs, barcodes, prices and stock are preserved.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 066 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='categories' AND column_name='is_retired_grouping') THEN RAISE EXCEPTION 'Apply 065 first'; END IF;
 IF EXISTS(SELECT 1 FROM products p WHERE NOT EXISTS(SELECT 1 FROM product_variants v WHERE v.tenant_id=p.tenant_id AND v.product_id=p.product_id)) THEN RAISE EXCEPTION 'Products without SKUs require manual review; do not invent identities'; END IF;
END $$;
LOCK TABLE public.products,public.product_variants,public.variant_supplier_identifiers IN ACCESS EXCLUSIVE MODE;
ALTER TABLE public.products ADD COLUMN IF NOT EXISTS registration_request_id uuid;
ALTER TABLE public.products ADD COLUMN IF NOT EXISTS registration_request_hash varchar(64);
ALTER TABLE public.products ADD COLUMN IF NOT EXISTS registration_defaults jsonb;
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_registration_request ON public.products(tenant_id,registration_request_id) WHERE registration_request_id IS NOT NULL;
ALTER TABLE public.product_variants ADD COLUMN IF NOT EXISTS use_default_purchase_price boolean NOT NULL DEFAULT false;
ALTER TABLE public.product_variants ADD COLUMN IF NOT EXISTS use_default_selling_price boolean NOT NULL DEFAULT false;
ALTER TABLE public.variant_supplier_identifiers ADD COLUMN IF NOT EXISTS purchasing_details jsonb NOT NULL DEFAULT '{}'::jsonb;
-- Existing free-text fitment specifications stay untouched. Only new verified entries create master links.
CREATE TABLE IF NOT EXISTS public.vehicle_models(
 vehicle_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant() REFERENCES public.tenants,
 business_profile_code text NOT NULL DEFAULT public.sf_current_business_profile(),manufacturer varchar(60) NOT NULL CHECK(length(btrim(manufacturer))>0),
 model varchar(100) NOT NULL CHECK(length(btrim(model))>0),variant varchar(60) NOT NULL DEFAULT '',
 UNIQUE(tenant_id,vehicle_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_vehicle_model_identity ON public.vehicle_models(tenant_id,business_profile_code,lower(btrim(manufacturer)),lower(btrim(model)),lower(btrim(variant)));
CREATE TABLE IF NOT EXISTS public.product_vehicle_fitments(
 fitment_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),product_id integer NOT NULL,variant_id bigint,vehicle_id bigint NOT NULL,
 year_from integer CHECK(year_from BETWEEN 1900 AND 2100),year_to integer CHECK(year_to BETWEEN 1900 AND 2100),notes varchar(300),
 CHECK(year_from IS NULL OR year_to IS NULL OR year_from<=year_to),
 FOREIGN KEY(tenant_id,product_id) REFERENCES public.products(tenant_id,product_id),
 FOREIGN KEY(tenant_id,vehicle_id) REFERENCES public.vehicle_models(tenant_id,vehicle_id),
 FOREIGN KEY(product_id,variant_id) REFERENCES public.product_variants(product_id,variant_id),
 FOREIGN KEY(tenant_id,variant_id) REFERENCES public.product_variants(tenant_id,variant_id)
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_fitment ON public.product_vehicle_fitments(tenant_id,product_id,COALESCE(variant_id,0),vehicle_id,COALESCE(year_from,0),COALESCE(year_to,0));
CREATE INDEX IF NOT EXISTS ix_vehicle_fitment_lookup ON public.product_vehicle_fitments(tenant_id,vehicle_id,product_id,variant_id);
ALTER TABLE public.vehicle_models ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.vehicle_models FORCE ROW LEVEL SECURITY;
ALTER TABLE public.product_vehicle_fitments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.product_vehicle_fitments FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS vehicle_model_scope ON public.vehicle_models;
CREATE POLICY vehicle_model_scope ON public.vehicle_models USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile()) WITH CHECK(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_current_business_profile());
DROP POLICY IF EXISTS vehicle_fitment_scope ON public.product_vehicle_fitments;
CREATE POLICY vehicle_fitment_scope ON public.product_vehicle_fitments USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.products p JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id WHERE p.tenant_id=product_vehicle_fitments.tenant_id AND p.product_id=product_vehicle_fitments.product_id AND c.business_profile_code=public.sf_visible_business_profile()))
 WITH CHECK(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.products p JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id JOIN public.vehicle_models v ON v.tenant_id=p.tenant_id AND v.business_profile_code=c.business_profile_code WHERE p.tenant_id=product_vehicle_fitments.tenant_id AND p.product_id=product_vehicle_fitments.product_id AND v.vehicle_id=product_vehicle_fitments.vehicle_id));
GRANT SELECT,INSERT ON public.vehicle_models TO stockflow_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON public.product_vehicle_fitments TO stockflow_runtime;
GRANT USAGE,SELECT ON SEQUENCE public.vehicle_models_vehicle_id_seq,public.product_vehicle_fitments_fitment_id_seq TO stockflow_runtime;
COMMENT ON COLUMN public.products.registration_defaults IS 'Reference defaults: purchase costs exclude GST; selling prices follow shop tax mode. Historical prices are immutable snapshots; null retains legacy explicit variant prices.';
COMMENT ON COLUMN public.products.parent_product_code IS 'Family identifier. Never substitute for an exact variant SKU in inventory or transactions.';
CREATE OR REPLACE FUNCTION public.sf_product_registration_integrity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE used boolean;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 IF TG_TABLE_NAME='products' THEN
  IF NEW.registration_defaults IS NOT NULL AND NEW.registration_defaults->>'preferredSupplierId' IS NOT NULL AND (TG_OP='INSERT' OR NEW.registration_defaults->>'preferredSupplierId' IS DISTINCT FROM OLD.registration_defaults->>'preferredSupplierId') THEN
   IF NOT EXISTS(SELECT 1 FROM suppliers s WHERE s.tenant_id=NEW.tenant_id AND s.supplier_id=(NEW.registration_defaults->>'preferredSupplierId')::integer AND s.is_active AND s.business_profile_code=(SELECT business_profile_code FROM categories WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id)) THEN RAISE EXCEPTION 'Choose an active preferred supplier from this shop' USING ERRCODE='23514'; END IF;
  END IF;
  IF TG_OP='UPDATE' THEN
   IF OLD.registration_defaults IS NOT NULL AND COALESCE(NEW.registration_defaults->'variantAxes','[]'::jsonb) IS DISTINCT FROM COALESCE(OLD.registration_defaults->'variantAxes','[]'::jsonb) THEN RAISE EXCEPTION 'Saved variant attributes cannot change; preserve existing combinations' USING ERRCODE='23514'; END IF;
   IF NEW.registration_request_id IS DISTINCT FROM OLD.registration_request_id AND OLD.registration_request_id IS NOT NULL OR NEW.registration_request_hash IS DISTINCT FROM OLD.registration_request_hash AND OLD.registration_request_hash IS NOT NULL THEN RAISE EXCEPTION 'Saved registration request identity cannot change' USING ERRCODE='23514'; END IF;
   IF NEW.parent_product_code IS DISTINCT FROM OLD.parent_product_code OR NEW.unit_of_measure IS DISTINCT FROM OLD.unit_of_measure THEN
    SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id) OR EXISTS(SELECT 1 FROM purchase_items WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id) OR EXISTS(SELECT 1 FROM product_units WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id) INTO used;
    IF used THEN RAISE EXCEPTION 'Product code and stock unit cannot change after transactions; use a reviewed correction' USING ERRCODE='23514'; END IF;
   END IF;
  END IF;
  IF TG_OP='INSERT' OR NEW.category_id IS DISTINCT FROM OLD.category_id THEN
   IF EXISTS(SELECT 1 FROM categories c WHERE c.tenant_id=NEW.tenant_id AND c.parent_category_id=NEW.category_id AND c.is_active AND NOT c.is_retired_grouping) THEN RAISE EXCEPTION 'Choose an active subcategory under this parent' USING ERRCODE='23514'; END IF;
  END IF;
 ELSE
  IF NEW.mrp>0 AND NEW.selling_price>NEW.mrp THEN RAISE EXCEPTION 'Selling price cannot exceed MRP' USING ERRCODE='23514'; END IF;
  IF TG_OP='INSERT' AND NEW.sku !~ '^[A-Za-z0-9][A-Za-z0-9_-]{2,39}$' THEN RAISE EXCEPTION 'New SKUs require 3 to 40 letters, numbers, hyphens or underscores' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND NEW.sku IS DISTINCT FROM OLD.sku THEN
   SELECT EXISTS(SELECT 1 FROM inventory_transactions WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id) OR EXISTS(SELECT 1 FROM purchase_items WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id) OR EXISTS(SELECT 1 FROM product_units WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id) INTO used;
   IF used THEN RAISE EXCEPTION 'SKU cannot change after transactions; preserve the saved stock identity' USING ERRCODE='23514'; END IF;
   IF NEW.sku !~ '^[A-Za-z0-9][A-Za-z0-9_-]{2,39}$' THEN RAISE EXCEPTION 'Use a valid SKU' USING ERRCODE='23514'; END IF;
  END IF;
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS product_registration_integrity ON public.products;
CREATE TRIGGER product_registration_integrity BEFORE INSERT OR UPDATE ON public.products FOR EACH ROW EXECUTE FUNCTION public.sf_product_registration_integrity();
DROP TRIGGER IF EXISTS variant_registration_integrity ON public.product_variants;
CREATE TRIGGER variant_registration_integrity BEFORE INSERT OR UPDATE ON public.product_variants FOR EACH ROW EXECUTE FUNCTION public.sf_product_registration_integrity();
REVOKE ALL ON FUNCTION public.sf_product_registration_integrity() FROM PUBLIC;
-- A registered family code never substitutes for a sellable SKU. Keep legacy simple-item callers working.
CREATE OR REPLACE FUNCTION public.sf_registration_exact_variant() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE unit_id bigint;
BEGIN
 IF NEW.variant_id IS NULL AND EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=NEW.tenant_id AND p.product_id=NEW.product_id AND p.has_variants AND p.parent_product_code IS NOT NULL) THEN
  unit_id:=NULLIF(to_jsonb(NEW)->>'product_unit_id','')::bigint;
  IF unit_id IS NOT NULL THEN SELECT variant_id INTO NEW.variant_id FROM public.product_units WHERE tenant_id=NEW.tenant_id AND product_id=NEW.product_id AND product_unit_id=unit_id; END IF;
  IF NEW.variant_id IS NULL THEN RAISE EXCEPTION 'Select the exact variant SKU; the parent product code is a family identifier' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
DO $$ DECLARE tab text; BEGIN FOREACH tab IN ARRAY ARRAY['purchase_items','sale_items','inventory_transactions','product_units'] LOOP
 EXECUTE format('DROP TRIGGER IF EXISTS a00_registration_exact_variant ON public.%I',tab);
 EXECUTE format('CREATE TRIGGER a00_registration_exact_variant BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_registration_exact_variant()',tab);
END LOOP; END $$;
REVOKE ALL ON FUNCTION public.sf_registration_exact_variant() FROM PUBLIC;
-- Extend the existing deferred attribute validator; retained category specification rules still apply.
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

 SELECT array_agg(DISTINCT code ORDER BY code) INTO axes FROM (
  SELECT x->'rule'->>'code' code FROM jsonb_array_elements(cfg) x WHERE (x->'rule'->>'variantAxis')::boolean
  UNION SELECT jsonb_array_elements_text(COALESCE(registration_defaults->'variantAxes','[]'::jsonb)) FROM public.products WHERE tenant_id=tid AND product_id=pid
 ) options;
 IF cardinality(axes)>3 OR EXISTS(SELECT 1 FROM unnest(axes) code WHERE code !~ '^[a-z][a-z0-9_]{0,39}$') THEN RAISE EXCEPTION 'Choose up to three valid variant attributes' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM jsonb_array_elements(cfg) x WHERE NOT (x->'rule'->>'variantAxis')::boolean AND x->'rule'->>'code'=ANY(axes)) THEN RAISE EXCEPTION 'Common specifications cannot become variant attributes' USING ERRCODE='23514'; END IF;
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
   IF EXISTS(SELECT 1 FROM unnest(axes) k WHERE btrim(COALESCE(row_value.attributes->>k,''))='') THEN RAISE EXCEPTION 'Choose all variant attribute values' USING ERRCODE='23514'; END IF;
   SELECT jsonb_object_agg(k,lower(btrim(row_value.attributes->>k))) INTO combo FROM unnest(axes) k;
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
REVOKE ALL ON FUNCTION public.sf_validate_category_attributes() FROM PUBLIC;
COMMIT;
