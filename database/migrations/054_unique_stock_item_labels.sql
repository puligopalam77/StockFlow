-- MANUAL ONLY. Back up and stop application/workers. Run after 053 as PostgreSQL superuser.
-- Adds label identities only. Stock quantities, serials, existing codes and history are unchanged.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 054 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='product_image_gallery_enabled') THEN RAISE EXCEPTION 'Apply 053 first'; END IF;
 IF EXISTS(SELECT 1 FROM public.variant_barcodes WHERE upper(btrim(barcode)) ~ '^SFI[0-9]+X[0-9]+$')
 OR EXISTS(SELECT 1 FROM public.product_units WHERE EXISTS(SELECT 1 FROM unnest(ARRAY[serial_number,barcode,internal_barcode,manufacturer_barcode]) c WHERE upper(btrim(c)) ~ '^SFI[0-9]+X[0-9]+$')) THEN
  RAISE EXCEPTION 'An existing identifier uses the reserved stock-item label namespace; resolve before applying 054'; END IF;
END $$;
ALTER TABLE public.stock_intake_items ADD CONSTRAINT ux_intake_item_label_owner UNIQUE(tenant_id,location_id,intake_item_id);
CREATE TABLE public.stock_item_labels(
 label_id bigserial PRIMARY KEY,
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 location_id bigint NOT NULL DEFAULT NULLIF(public.sf_location(),0),
 intake_item_id bigint NOT NULL,
 item_number integer NOT NULL CHECK(item_number BETWEEN 1 AND 10000),
 barcode text GENERATED ALWAYS AS ('SFI'||intake_item_id::text||'X'||item_number::text) STORED,
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,intake_item_id,item_number), UNIQUE(barcode),
 FOREIGN KEY(tenant_id,location_id,intake_item_id) REFERENCES public.stock_intake_items(tenant_id,location_id,intake_item_id)
);
ALTER TABLE public.stock_item_labels ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.stock_item_labels FORCE ROW LEVEL SECURITY;
CREATE POLICY item_label_tenant ON public.stock_item_labels USING(tenant_id=public.sf_tenant()) WITH CHECK(tenant_id=public.sf_tenant());
CREATE POLICY item_label_owner ON public.stock_item_labels AS RESTRICTIVE
 USING(public.sf_visible_location(location_id) AND EXISTS(
  SELECT 1 FROM public.stock_intake_items i JOIN public.product_variants v USING(tenant_id,variant_id)
  WHERE i.tenant_id=stock_item_labels.tenant_id AND i.intake_item_id=stock_item_labels.intake_item_id))
 WITH CHECK(location_id=public.sf_location() AND EXISTS(
  SELECT 1 FROM public.stock_intake_items i JOIN public.product_variants v USING(tenant_id,variant_id)
  WHERE i.tenant_id=stock_item_labels.tenant_id AND i.intake_item_id=stock_item_labels.intake_item_id
  AND NOT v.is_serial_tracked AND cardinality(i.serial_numbers)=0 AND stock_item_labels.item_number<=i.quantity));
GRANT SELECT,INSERT ON public.stock_item_labels TO stockflow_runtime;
GRANT USAGE,SELECT ON SEQUENCE public.stock_item_labels_label_id_seq TO stockflow_runtime;
-- Reserve generated codes across both scan namespaces, including future imports.
CREATE FUNCTION public.sf_reserve_stock_item_label_codes() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE codes text[];
BEGIN
 IF TG_TABLE_NAME='product_units' THEN codes:=ARRAY[NEW.serial_number,NEW.barcode,NEW.internal_barcode,NEW.manufacturer_barcode];
 ELSE codes:=ARRAY[NEW.barcode]; END IF;
 IF EXISTS(SELECT 1 FROM unnest(codes) c WHERE upper(btrim(c)) ~ '^SFI[0-9]+X[0-9]+$') THEN
  RAISE EXCEPTION 'This barcode is reserved for a received stock item' USING ERRCODE='23505'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER reserve_stock_item_codes BEFORE INSERT OR UPDATE ON public.product_units FOR EACH ROW EXECUTE FUNCTION public.sf_reserve_stock_item_label_codes();
CREATE TRIGGER reserve_stock_item_codes BEFORE INSERT OR UPDATE ON public.variant_barcodes FOR EACH ROW EXECUTE FUNCTION public.sf_reserve_stock_item_label_codes();
REVOKE ALL ON FUNCTION public.sf_reserve_stock_item_label_codes() FROM PUBLIC;
COMMIT;
