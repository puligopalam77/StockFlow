-- MANUAL ONLY. Back up, stop application/workers; run after 062 as PostgreSQL superuser.
-- Retains purchase/item IDs, receipts, payments and barcode identities.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 063 as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='barcode_print_requests' AND column_name='sheet_template') THEN RAISE EXCEPTION 'Apply 062 first'; END IF;
END $$;
ALTER TABLE public.purchases ADD COLUMN edit_version bigint NOT NULL DEFAULT 0;
ALTER TABLE public.purchases ADD COLUMN internal_reference varchar(100);
CREATE FUNCTION public.sf_purchase_edit_version() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN NEW.edit_version:=OLD.edit_version+1; RETURN NEW; END $$;
CREATE TRIGGER purchase_edit_version BEFORE UPDATE ON public.purchases FOR EACH ROW EXECUTE FUNCTION public.sf_purchase_edit_version();
REVOKE ALL ON FUNCTION public.sf_purchase_edit_version() FROM PUBLIC;
CREATE TABLE public.purchase_changes(
 change_id bigserial PRIMARY KEY,
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 location_id bigint NOT NULL DEFAULT NULLIF(public.sf_location(),0),
 purchase_id bigint NOT NULL,
 request_id uuid NOT NULL,
 change_type text NOT NULL CHECK(change_type IN('DRAFT_EDIT','DETAILS','ORDER','FINANCIAL','STOCK')),
 reason text NOT NULL CHECK(length(btrim(reason)) BETWEEN 3 AND 1000),
 old_values jsonb NOT NULL,new_values jsonb NOT NULL,request_payload jsonb NOT NULL,
 amount_delta numeric(16,2) NOT NULL DEFAULT 0,
 adjustment_id bigint,
 old_attachment_name text,old_attachment_content_type text,old_attachment_data bytea,
 changed_by integer NOT NULL DEFAULT public.sf_actor(),changed_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,purchase_id) REFERENCES public.purchases(tenant_id,purchase_id),
 FOREIGN KEY(tenant_id,changed_by) REFERENCES public.users(tenant_id,user_id)
);
CREATE INDEX ix_purchase_changes_document ON public.purchase_changes(tenant_id,location_id,purchase_id,change_id);
ALTER TABLE public.purchase_changes ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.purchase_changes FORCE ROW LEVEL SECURITY;
CREATE POLICY purchase_change_tenant ON public.purchase_changes USING(tenant_id=public.sf_tenant()) WITH CHECK(tenant_id=public.sf_tenant());
CREATE POLICY purchase_change_owner ON public.purchase_changes AS RESTRICTIVE
 USING(public.sf_visible_location(location_id) AND EXISTS(SELECT 1 FROM public.purchases p WHERE p.tenant_id=purchase_changes.tenant_id AND p.purchase_id=purchase_changes.purchase_id))
 WITH CHECK(location_id=public.sf_location() AND changed_by=public.sf_actor() AND EXISTS(SELECT 1 FROM public.purchases p WHERE p.tenant_id=purchase_changes.tenant_id AND p.purchase_id=purchase_changes.purchase_id AND p.location_id=purchase_changes.location_id));
GRANT SELECT,INSERT ON public.purchase_changes TO stockflow_runtime;
REVOKE UPDATE,DELETE,TRUNCATE ON public.purchase_changes FROM stockflow_runtime;
GRANT USAGE,SELECT ON SEQUENCE public.purchase_changes_change_id_seq TO stockflow_runtime;
COMMIT;
