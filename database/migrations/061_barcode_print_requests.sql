-- MANUAL ONLY. Back up, stop application/workers; run after 060 as PostgreSQL superuser.
-- Tracks print requests, not printer confirmations. No stock or barcode identities change.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 061 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.purchase_item_labels') IS NULL THEN RAISE EXCEPTION 'Apply 060 first'; END IF;
END $$;
CREATE TABLE public.barcode_print_requests(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 location_id bigint NOT NULL DEFAULT NULLIF(public.sf_location(),0),
 request_id uuid NOT NULL,
 lot_kind text NOT NULL CHECK(lot_kind IN('intake','receipt')),
 lot_id bigint NOT NULL,
 action text NOT NULL CHECK(action IN('PRINT','REPRINT')),
 format text NOT NULL CHECK(format IN('sheet','thermal')),
 requested_by integer NOT NULL DEFAULT public.sf_actor(),
 requested_by_name text NOT NULL,
 requested_at timestamptz NOT NULL DEFAULT now(),
 selection jsonb NOT NULL CHECK(jsonb_typeof(selection)='array' AND jsonb_array_length(selection) BETWEEN 1 AND 500),
 PRIMARY KEY(tenant_id,request_id),
 FOREIGN KEY(tenant_id,requested_by) REFERENCES public.users(tenant_id,user_id)
);
CREATE INDEX ix_barcode_print_lot ON public.barcode_print_requests(tenant_id,location_id,lot_kind,lot_id,requested_at DESC);
ALTER TABLE public.barcode_print_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.barcode_print_requests FORCE ROW LEVEL SECURITY;
CREATE POLICY barcode_print_tenant ON public.barcode_print_requests USING(tenant_id=public.sf_tenant()) WITH CHECK(tenant_id=public.sf_tenant());
CREATE POLICY barcode_print_owner ON public.barcode_print_requests AS RESTRICTIVE
 USING(public.sf_visible_location(location_id) AND (
  lot_kind='intake' AND EXISTS(SELECT 1 FROM public.stock_intake_items i JOIN public.product_variants v USING(tenant_id,variant_id) WHERE i.tenant_id=barcode_print_requests.tenant_id AND i.location_id=barcode_print_requests.location_id AND i.intake_id=barcode_print_requests.lot_id)
  OR lot_kind='receipt' AND EXISTS(SELECT 1 FROM public.purchase_receipt_items ri JOIN public.purchase_items pi USING(tenant_id,purchase_item_id) JOIN public.product_variants v ON v.tenant_id=pi.tenant_id AND v.variant_id=pi.variant_id WHERE ri.tenant_id=barcode_print_requests.tenant_id AND ri.location_id=barcode_print_requests.location_id AND ri.receipt_id=barcode_print_requests.lot_id)))
 WITH CHECK(location_id=public.sf_location() AND requested_by=public.sf_actor() AND (
  lot_kind='intake' AND EXISTS(SELECT 1 FROM public.stock_intakes i WHERE i.tenant_id=barcode_print_requests.tenant_id AND i.location_id=barcode_print_requests.location_id AND i.intake_id=barcode_print_requests.lot_id)
  OR lot_kind='receipt' AND EXISTS(SELECT 1 FROM public.purchase_receipts r WHERE r.tenant_id=barcode_print_requests.tenant_id AND r.location_id=barcode_print_requests.location_id AND r.receipt_id=barcode_print_requests.lot_id AND r.status='COMPLETED')));
GRANT SELECT,INSERT ON public.barcode_print_requests TO stockflow_runtime;
COMMIT;
