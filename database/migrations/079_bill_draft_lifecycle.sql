-- MANUAL ONLY after 078. Back up, stop ALL app instances/writers, run ENTIRE file as postgres.
-- Deploy the updated backend together with this migration: old direct-write APIs fail closed.
-- No UUID, number, JSON payload, ownership or existing timestamp is rewritten.
-- Old saves carry no reliable Held/Active evidence. Preserve them as LEGACY recovery records.
-- Lease: 120 seconds; renew every 30 seconds. Expired ACTIVE requires explicit recovery.
-- Any error rolls back this entire migration. Rerun after correcting the error.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regclass('public.billing_draft_numbers') IS NULL THEN RAISE EXCEPTION 'Apply 078 first'; END IF;
END $$;
LOCK TABLE public.billing_drafts IN ACCESS EXCLUSIVE MODE;
ALTER TABLE public.billing_drafts ADD COLUMN IF NOT EXISTS draft_status text NOT NULL DEFAULT 'LEGACY' CHECK(draft_status IN('LEGACY','ACTIVE','HELD','COMPLETED','DISCARDED')),
 ADD COLUMN IF NOT EXISTS revision bigint NOT NULL DEFAULT 1 CHECK(revision>0),
 ADD COLUMN IF NOT EXISTS editor_id uuid,ADD COLUMN IF NOT EXISTS lease_until timestamptz,
 ADD COLUMN IF NOT EXISTS held_at timestamptz,ADD COLUMN IF NOT EXISTS completed_at timestamptz,
 ADD COLUMN IF NOT EXISTS discarded_at timestamptz,ADD COLUMN IF NOT EXISTS completed_sale_id bigint,
 ADD COLUMN IF NOT EXISTS last_operation_id uuid,ADD COLUMN IF NOT EXISTS last_operation_action text;
CREATE INDEX IF NOT EXISTS ix_draft_lifecycle ON public.billing_drafts(tenant_id,user_id,location_id,draft_status,updated_at DESC);
-- A matching posted sale is reliable terminal evidence; update only new lifecycle metadata.
ALTER TABLE public.billing_drafts DISABLE TRIGGER a_profile_posting_guard;
UPDATE public.billing_drafts d SET draft_status='COMPLETED',completed_sale_id=s.sale_id
 FROM public.sales s WHERE d.draft_status='LEGACY' AND s.tenant_id=d.tenant_id AND s.request_id=d.draft_id
 AND s.store_id=d.store_id AND s.location_id=d.location_id AND s.created_by=d.user_id AND s.status='COMPLETED';
ALTER TABLE public.billing_drafts ENABLE TRIGGER a_profile_posting_guard;
CREATE OR REPLACE FUNCTION public.sf_draft_reply(d public.billing_drafts) RETURNS jsonb LANGUAGE sql STABLE SET search_path=pg_catalog,public AS $$
 SELECT jsonb_build_object('draftId',d.draft_id,'draftNumber',public.sf_draft_number(d.draft_sequence),'revision',d.revision,'draftStatus',d.draft_status,'leaseUntil',d.lease_until)
$$;
CREATE OR REPLACE FUNCTION public.sf_draft_action(action text,id uuid,editor uuid,expected bigint,operation uuid,label text DEFAULT NULL,body jsonb DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE d public.billing_drafts; BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 IF editor IS NULL OR editor='00000000-0000-0000-0000-000000000000'::uuid OR operation IS NULL OR NOT public.sf_gst_allowed('CREATE','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Authorized billing editor required' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('StockFlow.Sale:'||id,0));
 SELECT * INTO d FROM public.billing_drafts WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND draft_id=id FOR UPDATE;
 IF NOT FOUND THEN
  IF action<>'SAVE' OR expected IS DISTINCT FROM 0 OR EXISTS(SELECT 1 FROM public.billing_draft_numbers WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND draft_id=id) THEN RAISE EXCEPTION 'This draft is not available. Refresh held bills or recover an interrupted bill.'; END IF;
  IF EXISTS(SELECT 1 FROM public.sales WHERE tenant_id=public.sf_tenant() AND request_id=id) THEN RAISE EXCEPTION 'This bill has already been completed.'; END IF;
  IF length(btrim(coalesce(label,''))) NOT BETWEEN 1 AND 150 OR jsonb_typeof(body)<>'object' OR body->>'draftId' IS DISTINCT FROM id::text OR length(body::text)>250000 OR jsonb_typeof(body->'cart') IS DISTINCT FROM 'array' THEN RAISE EXCEPTION 'Invalid draft contents.'; END IF;
  INSERT INTO public.billing_drafts(draft_id,label,payload,draft_status,editor_id,lease_until,last_operation_id,last_operation_action) VALUES(id,label,body,'ACTIVE',editor,clock_timestamp()+interval '120 seconds',operation,'SAVE') RETURNING * INTO d;
  RETURN public.sf_draft_reply(d);
 END IF;
 IF EXISTS(SELECT 1 FROM public.sales WHERE tenant_id=d.tenant_id AND request_id=id) THEN RAISE EXCEPTION 'This bill has already been completed and cannot be resumed.'; END IF;
 IF d.last_operation_id=operation AND d.editor_id=editor AND action IN('SAVE','HOLD','RESUME','RECOVER','DISCARD') THEN
  IF d.last_operation_action IS DISTINCT FROM action OR action='SAVE' AND (d.payload IS DISTINCT FROM body OR d.label IS DISTINCT FROM label) THEN RAISE EXCEPTION 'This operation ID already belongs to different draft changes.'; END IF;
  RETURN public.sf_draft_reply(d);
 END IF;
 IF d.draft_status IN('COMPLETED','DISCARDED') THEN RAISE EXCEPTION 'This draft is completed or discarded and cannot be edited or resumed.'; END IF;
 IF action='HEARTBEAT' THEN
  IF d.draft_status<>'ACTIVE' OR d.editor_id IS DISTINCT FROM editor OR d.lease_until IS NULL OR d.lease_until<=clock_timestamp() THEN RAISE EXCEPTION 'This editing session expired or the bill is being edited elsewhere. Recover it before continuing.'; END IF;
  UPDATE public.billing_drafts SET lease_until=clock_timestamp()+interval '120 seconds' WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;RETURN public.sf_draft_reply(d);
 END IF;
 IF action='RELEASE' THEN
  IF d.draft_status='ACTIVE' AND d.editor_id=editor THEN UPDATE public.billing_drafts SET lease_until=NULL WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;END IF;
  RETURN public.sf_draft_reply(d);
 END IF;
 IF expected IS DISTINCT FROM d.revision THEN RAISE EXCEPTION 'This bill changed in another session. Your device backup is retained; refresh and recover the latest server copy.'; END IF;
 IF action IN('RESUME','RECOVER') THEN
  IF action='RESUME' AND d.draft_status<>'HELD' THEN RAISE EXCEPTION 'This bill is no longer held. It may already be open in another session.'; END IF;
  IF action='RECOVER' AND NOT(d.draft_status='LEGACY' OR d.draft_status='ACTIVE' AND (d.lease_until IS NULL OR d.lease_until<=clock_timestamp())) THEN RAISE EXCEPTION 'This bill is currently being edited in another session.'; END IF;
  IF EXISTS(SELECT 1 FROM public.billing_drafts a WHERE a.tenant_id=d.tenant_id AND a.user_id=d.user_id AND a.location_id=d.location_id AND a.draft_status='ACTIVE' AND a.editor_id=editor AND a.lease_until>clock_timestamp() AND a.draft_id<>id) THEN RAISE EXCEPTION 'Hold or discard your current bill before resuming another.'; END IF;
  UPDATE public.billing_drafts SET draft_status='ACTIVE',editor_id=editor,lease_until=clock_timestamp()+interval '120 seconds',revision=revision+1,last_operation_id=operation,last_operation_action=action WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;
 ELSIF action IN('SAVE','HOLD','DISCARD') THEN
  IF d.draft_status='ACTIVE' AND (d.editor_id IS DISTINCT FROM editor OR d.lease_until IS NULL OR d.lease_until<=clock_timestamp()) THEN RAISE EXCEPTION 'This bill is currently being edited in another session or its lease expired.'; END IF;
  IF action IN('SAVE','HOLD') AND d.draft_status<>'ACTIVE' THEN RAISE EXCEPTION 'Resume or recover this bill before editing it. Delayed autosave was rejected.'; END IF;
  IF action='SAVE' THEN
   IF length(btrim(coalesce(label,''))) NOT BETWEEN 1 AND 150 OR jsonb_typeof(body) IS DISTINCT FROM 'object' OR body->>'draftId' IS DISTINCT FROM id::text OR length(body::text)>250000 OR jsonb_typeof(body->'cart') IS DISTINCT FROM 'array' THEN RAISE EXCEPTION 'Invalid draft contents.'; END IF;
   UPDATE public.billing_drafts SET payload=body,label=sf_draft_action.label,updated_at=clock_timestamp(),revision=revision+1,lease_until=clock_timestamp()+interval '120 seconds',last_operation_id=operation,last_operation_action=action WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;
  ELSIF action='HOLD' THEN
   IF jsonb_array_length(d.payload->'cart')=0 THEN RAISE EXCEPTION 'Add an item before holding this bill.'; END IF;
   UPDATE public.billing_drafts SET draft_status='HELD',held_at=clock_timestamp(),updated_at=clock_timestamp(),lease_until=NULL,revision=revision+1,last_operation_id=operation,last_operation_action=action WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;
  ELSE
   UPDATE public.billing_drafts SET draft_status='DISCARDED',discarded_at=clock_timestamp(),lease_until=NULL,revision=revision+1,last_operation_id=operation,last_operation_action=action,editor_id=editor WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;
  END IF;
 ELSE RAISE EXCEPTION 'Unsupported draft action.';
 END IF;
 RETURN public.sf_draft_reply(d);
END $$;
CREATE OR REPLACE FUNCTION public.sf_draft_switch(id uuid,editor uuid,expected bigint,operation uuid,recover boolean,current_id uuid,current_revision bigint,current_action text) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE key uuid; result jsonb; BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 -- Use identical sale locks in sorted order for two-draft switches and checkout races.
 FOR key IN SELECT x FROM unnest(ARRAY[id,current_id]) x WHERE x IS NOT NULL ORDER BY x LOOP PERFORM pg_advisory_xact_lock(hashtextextended('StockFlow.Sale:'||key,0)); END LOOP;
 IF EXISTS(SELECT 1 FROM public.billing_drafts WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND draft_id=id AND last_operation_id=operation AND editor_id=editor AND draft_status='ACTIVE') THEN
  SELECT public.sf_draft_reply(d) INTO result FROM public.billing_drafts d WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND draft_id=id;RETURN result;
 END IF;
 IF current_id IS NOT NULL AND current_id<>id THEN
  IF current_action NOT IN('HOLD','DISCARD','RELEASE') OR current_action IS NULL THEN RAISE EXCEPTION 'Choose how to protect the current bill.'; END IF;
  PERFORM public.sf_draft_action(current_action,current_id,editor,current_revision,operation);
 END IF;
 RETURN public.sf_draft_action(CASE WHEN recover THEN 'RECOVER' ELSE 'RESUME' END,id,editor,expected,operation);
END $$;
CREATE OR REPLACE FUNCTION public.sf_draft_checkout(id uuid,editor uuid,expected bigint) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE d public.billing_drafts; BEGIN
 SELECT * INTO d FROM public.billing_drafts WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND draft_id=id FOR UPDATE;
 IF NOT FOUND THEN
  IF EXISTS(SELECT 1 FROM public.billing_draft_numbers WHERE tenant_id=public.sf_tenant() AND draft_id=id) THEN RAISE EXCEPTION 'This draft is not available for checkout.';END IF;RETURN;
 END IF;
 IF d.draft_status<>'ACTIVE' OR d.editor_id IS DISTINCT FROM editor OR d.lease_until IS NULL OR d.lease_until<=clock_timestamp() OR expected IS DISTINCT FROM d.revision THEN RAISE EXCEPTION 'Resume or recover this bill in the current session before checkout. The saved draft is unchanged.'; END IF;
END $$;
CREATE OR REPLACE FUNCTION public.sf_draft_complete(id uuid,sale bigint) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.sales s WHERE s.tenant_id=public.sf_tenant() AND s.request_id=id AND s.sale_id=sale AND s.created_by=public.sf_actor() AND s.location_id=public.sf_location() AND s.store_id=public.sf_store() AND s.status='COMPLETED' AND s.xmin=pg_current_xact_id()::xid) THEN RAISE EXCEPTION 'Complete a draft only in its successful checkout transaction.'; END IF;
 UPDATE public.billing_drafts SET draft_status='COMPLETED',completed_at=clock_timestamp(),completed_sale_id=sale,lease_until=NULL,revision=revision+1
 WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND location_id=public.sf_location() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND draft_id=id AND draft_status='ACTIVE';
END $$;
REVOKE INSERT,UPDATE,DELETE ON public.billing_drafts FROM stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_draft_reply(public.billing_drafts),public.sf_draft_action(text,uuid,uuid,bigint,uuid,text,jsonb),public.sf_draft_switch(uuid,uuid,bigint,uuid,boolean,uuid,bigint,text),public.sf_draft_checkout(uuid,uuid,bigint),public.sf_draft_complete(uuid,bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_draft_action(text,uuid,uuid,bigint,uuid,text,jsonb),public.sf_draft_switch(uuid,uuid,bigint,uuid,boolean,uuid,bigint,text),public.sf_draft_checkout(uuid,uuid,bigint),public.sf_draft_complete(uuid,bigint) TO stockflow_runtime;
SELECT tenant_id,store_id,draft_status,count(*) FROM public.billing_drafts GROUP BY tenant_id,store_id,draft_status ORDER BY tenant_id,store_id,draft_status;
COMMIT;
