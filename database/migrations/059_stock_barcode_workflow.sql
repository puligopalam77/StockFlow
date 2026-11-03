-- MANUAL ONLY. Back up, stop application/workers; run after 058 as PostgreSQL superuser.
-- Preserves existing SFI labels, stock balances, manufacturer codes and history.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 059 as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_brand_registration_details()') IS NULL THEN RAISE EXCEPTION 'Apply 058 first'; END IF;
 IF EXISTS(SELECT 1 FROM public.variant_barcodes WHERE upper(btrim(barcode)) ~ '^SFL[0-9]+$')
 OR EXISTS(SELECT 1 FROM public.product_units WHERE EXISTS(SELECT 1 FROM unnest(ARRAY[serial_number,barcode,internal_barcode,manufacturer_barcode]) c WHERE upper(btrim(c)) ~ '^SFL[0-9]+$')) THEN
  RAISE EXCEPTION 'An existing identifier uses the reserved SFL label namespace; resolve before applying 059'; END IF;
END $$;
ALTER TABLE public.stock_item_labels ALTER COLUMN intake_item_id DROP NOT NULL;
ALTER TABLE public.stock_item_labels ADD COLUMN variant_id bigint;
ALTER TABLE public.stock_item_labels ADD FOREIGN KEY(tenant_id,variant_id) REFERENCES public.product_variants(tenant_id,variant_id);
ALTER TABLE public.stock_item_labels ADD CHECK((intake_item_id IS NOT NULL AND variant_id IS NULL) OR (intake_item_id IS NULL AND variant_id IS NOT NULL));
CREATE UNIQUE INDEX ux_stock_label_variant_slot ON public.stock_item_labels(tenant_id,location_id,variant_id,item_number) WHERE intake_item_id IS NULL;
-- DROP EXPRESSION retains every existing barcode value.
ALTER TABLE public.stock_item_labels ALTER COLUMN barcode DROP EXPRESSION;
CREATE FUNCTION public.sf_stock_label_identity() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 NEW.barcode:=CASE WHEN NEW.intake_item_id IS NOT NULL THEN 'SFI'||NEW.intake_item_id::text||'X'||NEW.item_number::text ELSE 'SFL'||NEW.label_id::text END;
 RETURN NEW;
END $$;
CREATE TRIGGER stock_label_identity BEFORE INSERT ON public.stock_item_labels FOR EACH ROW EXECUTE FUNCTION public.sf_stock_label_identity();
REVOKE ALL ON FUNCTION public.sf_stock_label_identity() FROM PUBLIC;
DROP POLICY item_label_owner ON public.stock_item_labels;
CREATE POLICY item_label_owner ON public.stock_item_labels AS RESTRICTIVE
 USING(public.sf_visible_location(location_id) AND (
  EXISTS(SELECT 1 FROM public.stock_intake_items i JOIN public.product_variants v USING(tenant_id,variant_id) WHERE i.tenant_id=stock_item_labels.tenant_id AND i.intake_item_id=stock_item_labels.intake_item_id)
  OR EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=stock_item_labels.tenant_id AND v.variant_id=stock_item_labels.variant_id)))
 WITH CHECK(location_id=public.sf_location() AND (
  EXISTS(SELECT 1 FROM public.stock_intake_items i JOIN public.product_variants v USING(tenant_id,variant_id) WHERE i.tenant_id=stock_item_labels.tenant_id AND i.intake_item_id=stock_item_labels.intake_item_id AND NOT v.is_serial_tracked AND cardinality(i.serial_numbers)=0 AND stock_item_labels.item_number<=i.quantity)
  OR EXISTS(SELECT 1 FROM public.product_variants v JOIN public.vw_variant_current_stock s USING(variant_id) WHERE v.tenant_id=stock_item_labels.tenant_id AND v.variant_id=stock_item_labels.variant_id AND NOT v.is_serial_tracked AND stock_item_labels.item_number<=s.current_stock)));
CREATE OR REPLACE FUNCTION public.sf_reserve_stock_item_label_codes() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE codes text[];
BEGIN
 IF TG_TABLE_NAME='product_units' THEN codes:=ARRAY[NEW.serial_number,NEW.barcode,NEW.internal_barcode,NEW.manufacturer_barcode]; ELSE codes:=ARRAY[NEW.barcode]; END IF;
 IF EXISTS(SELECT 1 FROM unnest(codes) c WHERE upper(btrim(c)) ~ '^(SFI[0-9]+X[0-9]+|SFL[0-9]+)$') THEN
  RAISE EXCEPTION 'This barcode is reserved for a stock item label' USING ERRCODE='23505'; END IF;
 RETURN NEW;
END $$;
COMMIT;
