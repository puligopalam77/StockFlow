-- Minimum items for persisted Draft/Hold writes. Apply after 089 and before app rollout.
-- Existing drafts are retained; recovery, release, heartbeat and discard are unchanged.
BEGIN;
CREATE OR REPLACE FUNCTION public.sf_draft_has_valid_item(body jsonb) RETURNS boolean
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof(body->'cart')='array' THEN body->'cart' ELSE '[]'::jsonb END) AS lines(item)
 WHERE CASE WHEN jsonb_typeof(item->'quantity')='number' THEN (item->>'quantity')::numeric>0 AND (item->>'quantity')::numeric<=10000 AND trunc((item->>'quantity')::numeric)=(item->>'quantity')::numeric ELSE false END
 AND ((CASE WHEN jsonb_typeof(item->'productId')='number' THEN (item->>'productId')::numeric>0 AND trunc((item->>'productId')::numeric)=(item->>'productId')::numeric ELSE false END
 AND CASE WHEN jsonb_typeof(item->'variantId')='number' THEN (item->>'variantId')::numeric>0 AND trunc((item->>'variantId')::numeric)=(item->>'variantId')::numeric ELSE false END)
 OR jsonb_typeof(item->'manualItem')='object' AND jsonb_typeof(item->'manualItem'->'name')='string' AND length(btrim(item->'manualItem'->>'name'))>0))
$$;
REVOKE ALL ON FUNCTION public.sf_draft_has_valid_item(jsonb) FROM PUBLIC;
CREATE OR REPLACE FUNCTION public.sf_draft_action(action text,id uuid,editor uuid,expected bigint,operation uuid,label text DEFAULT NULL,body jsonb DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE d public.billing_drafts; BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 IF editor IS NULL OR editor='00000000-0000-0000-0000-000000000000'::uuid OR operation IS NULL OR NOT public.sf_gst_allowed('CREATE','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Authorized billing editor required' USING ERRCODE='42501'; END IF;
 IF action='SAVE' AND NOT public.sf_draft_has_valid_item(body) THEN RAISE EXCEPTION 'Add at least one item before saving this bill.'; END IF;
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
   IF NOT public.sf_draft_has_valid_item(d.payload) THEN RAISE EXCEPTION 'Add at least one item before putting this bill on hold.'; END IF;
   UPDATE public.billing_drafts SET draft_status='HELD',held_at=clock_timestamp(),updated_at=clock_timestamp(),lease_until=NULL,revision=revision+1,last_operation_id=operation,last_operation_action=action WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;
  ELSE
   UPDATE public.billing_drafts SET draft_status='DISCARDED',discarded_at=clock_timestamp(),lease_until=NULL,revision=revision+1,last_operation_id=operation,last_operation_action=action,editor_id=editor WHERE (tenant_id,user_id,location_id,draft_id)=(d.tenant_id,d.user_id,d.location_id,id) RETURNING * INTO d;
  END IF;
 ELSE RAISE EXCEPTION 'Unsupported draft action.';
 END IF;
 RETURN public.sf_draft_reply(d);
END $$;
COMMIT;
