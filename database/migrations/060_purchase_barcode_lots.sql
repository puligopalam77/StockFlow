-- MANUAL ONLY. Back up, stop application/workers; run after 059 as PostgreSQL superuser.
-- Adds purchase-receipt labels and navigation. No stock or existing barcode is changed.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 060 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='stock_item_labels' AND column_name='variant_id') THEN RAISE EXCEPTION 'Apply 059 first'; END IF;
 IF EXISTS(SELECT 1 FROM public.variant_barcodes WHERE upper(btrim(barcode)) ~ '^SFR[0-9]+X[0-9]+$')
 OR EXISTS(SELECT 1 FROM public.product_units WHERE EXISTS(SELECT 1 FROM unnest(ARRAY[serial_number,barcode,internal_barcode,manufacturer_barcode]) c WHERE upper(btrim(c)) ~ '^SFR[0-9]+X[0-9]+$')) THEN RAISE EXCEPTION 'Existing identifiers conflict with the reserved purchase-label namespace'; END IF;
END $$;
ALTER TABLE public.purchase_receipt_items ADD CONSTRAINT ux_purchase_label_owner UNIQUE(tenant_id,location_id,receipt_item_id);
CREATE TABLE public.purchase_item_labels(
 label_id bigint PRIMARY KEY DEFAULT nextval('public.stock_item_labels_label_id_seq'),
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 location_id bigint NOT NULL DEFAULT NULLIF(public.sf_location(),0),
 receipt_item_id bigint NOT NULL,
 item_number integer NOT NULL CHECK(item_number BETWEEN 1 AND 10000),
 barcode text GENERATED ALWAYS AS ('SFR'||receipt_item_id::text||'X'||item_number::text) STORED UNIQUE,
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,receipt_item_id,item_number),
 FOREIGN KEY(tenant_id,location_id,receipt_item_id) REFERENCES public.purchase_receipt_items(tenant_id,location_id,receipt_item_id)
);
ALTER TABLE public.purchase_item_labels ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.purchase_item_labels FORCE ROW LEVEL SECURITY;
CREATE POLICY purchase_label_tenant ON public.purchase_item_labels USING(tenant_id=public.sf_tenant()) WITH CHECK(tenant_id=public.sf_tenant());
CREATE POLICY purchase_label_owner ON public.purchase_item_labels AS RESTRICTIVE
 USING(public.sf_visible_location(location_id) AND EXISTS(
  SELECT 1 FROM public.purchase_receipt_items ri JOIN public.purchase_items pi USING(tenant_id,purchase_item_id)
  JOIN public.product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id
  WHERE ri.tenant_id=purchase_item_labels.tenant_id AND ri.receipt_item_id=purchase_item_labels.receipt_item_id))
 WITH CHECK(location_id=public.sf_location() AND EXISTS(
  SELECT 1 FROM public.purchase_receipt_items ri JOIN public.purchase_receipts r USING(tenant_id,receipt_id)
  JOIN public.purchase_items pi ON pi.tenant_id=ri.tenant_id AND pi.purchase_item_id=ri.purchase_item_id
  JOIN public.product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id
  WHERE ri.tenant_id=purchase_item_labels.tenant_id AND ri.receipt_item_id=purchase_item_labels.receipt_item_id
  AND r.status='COMPLETED' AND NOT v.is_serial_tracked AND purchase_item_labels.item_number<=ri.quantity));
GRANT SELECT,INSERT ON public.purchase_item_labels TO stockflow_runtime;
CREATE OR REPLACE FUNCTION public.sf_reserve_stock_item_label_codes() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE codes text[];
BEGIN
 IF TG_TABLE_NAME='product_units' THEN codes:=ARRAY[NEW.serial_number,NEW.barcode,NEW.internal_barcode,NEW.manufacturer_barcode]; ELSE codes:=ARRAY[NEW.barcode]; END IF;
 IF EXISTS(SELECT 1 FROM unnest(codes) c WHERE upper(btrim(c)) ~ '^(SFI[0-9]+X[0-9]+|SFL[0-9]+|SFR[0-9]+X[0-9]+)$') THEN
  RAISE EXCEPTION 'This barcode is reserved for a stock item label' USING ERRCODE='23505'; END IF;
 RETURN NEW;
END $$;
UPDATE public.modules SET parent_module_id=(SELECT module_id FROM public.modules WHERE module_code='RECEIVE_STOCK'),
 module_name='Barcodes',route='/Labels',sort_order=40,show_in_navigation=true WHERE module_code='BARCODES';
INSERT INTO public.module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
 SELECT module_id,module_id,'VIEW' FROM public.modules WHERE module_code='BARCODES' ON CONFLICT DO NOTHING;
COMMIT;
