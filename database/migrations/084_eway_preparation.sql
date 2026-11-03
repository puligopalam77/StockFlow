-- MANUAL ONLY, after 083. Back up; stop writers; run ENTIRE file as postgres.
-- Preparation only: no provider credentials, official EBN generation or stock/payment posting.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_quotation_allowed(text)') IS NULL THEN RAISE EXCEPTION 'Apply 083 first'; END IF;
END $$;
-- Verified while exercising actual transfer request/approval: 051's alias h collides
-- with the function's h row variable. Preserve the same profile guard with a distinct alias.
DO $$ DECLARE definition text; needle text:='SELECT 1 FROM public.stock_transfers h WHERE h.tenant_id=tid AND h.transfer_id=id AND h.business_profile_code=public.sf_visible_business_profile()';
 replacement text:='SELECT 1 FROM public.stock_transfers transfer_scope WHERE transfer_scope.tenant_id=tid AND transfer_scope.transfer_id=id AND transfer_scope.business_profile_code=public.sf_visible_business_profile()';
BEGIN
 definition:=pg_get_functiondef('public.sf_transfer_action(bigint,text,jsonb,uuid)'::regprocedure);
 IF strpos(definition,needle)>0 THEN EXECUTE replace(definition,needle,replacement);
 ELSIF strpos(definition,replacement)=0 THEN RAISE EXCEPTION 'Transfer profile guard changed; review migration 084 before applying'; END IF;
END $$;
INSERT INTO public.modules(module_code,module_name,route,sort_order,show_in_navigation,navigation_icon,parent_module_id)
 VALUES('EWAY_BILLS','E-Way Bills','/EWayBills',36,true,'document',(SELECT module_id FROM public.modules WHERE module_code='GST_REPORTS'));
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,a.code,a.label,a.code NOT IN('VIEW','EXPORT') FROM public.modules m CROSS JOIN (VALUES
 ('VIEW','View scoped e-way preparations'),('PREPARE','Prepare e-way movement'),('EDIT','Revise e-way preparation'),
 ('VALIDATE','Review preparation data'),('RECORD_EXTERNAL','Record unverified external EBN with evidence'),
 ('EXPORT','Export preparation worksheet'),('VIEW_USAGE','View scoped e-way usage'),('CONFIGURE_PROVIDER','Review provider integration readiness')) a(code,label)
 WHERE m.module_code='EWAY_BILLS';
INSERT INTO public.module_navigation_permissions SELECT module_id,module_id,'VIEW' FROM public.modules WHERE module_code='EWAY_BILLS';
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE u.is_tenant_owner AND m.module_code='EWAY_BILLS' ON CONFLICT DO NOTHING;
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE m.module_code='EWAY_BILLS' ON CONFLICT DO NOTHING;
CREATE FUNCTION public.sf_eway_allowed(act text) RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_store()>0 AND public.sf_location()>0 AND public.sf_can_location(public.sf_location())
 AND public.sf_transfer_permission('EWAY_BILLS',act) AND public.sf_gst_allowed('VIEW','GST_REPORTS')
 AND EXISTS(SELECT 1 FROM public.store_locations l WHERE l.tenant_id=public.sf_tenant() AND l.store_id=public.sf_store() AND l.location_id=public.sf_location() AND l.is_active)
$$;
CREATE FUNCTION public.sf_eway_source(kind text,id bigint,reg bigint) RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE result jsonb;
BEGIN
 IF NOT public.sf_eway_allowed('VIEW') THEN RAISE EXCEPTION 'E-way source access denied' USING ERRCODE='42501'; END IF;
 IF kind='GST' THEN
  SELECT jsonb_build_object('type',sf_eway_source.kind,'id',d.document_id,'number',d.document_number,'date',d.document_date,'direction',d.direction,
   'registrationId',d.registration_id,'registration',d.registration_snapshot,'business',d.business_snapshot,
   'partyName',d.party_name,'partyGstin',d.party_gstin,'partyAddress',d.party_address,'partyState',d.party_state_code,
   'deliveryAddress',d.delivery_address,'lines',d.lines,'total',d.total,'warnings',d.warnings,
   'saleId',d.sale_id,'purchaseIds',d.purchase_ids) INTO result
  FROM public.gst_documents d WHERE d.tenant_id=public.sf_tenant() AND d.document_id=id AND d.registration_id=reg
   AND d.store_id=public.sf_store() AND d.location_id=public.sf_location() AND d.business_profile_code=public.sf_visible_business_profile() AND d.kind='INVOICE'
   AND ((d.direction='SALES' AND public.sf_transfer_permission('SALES','VIEW') AND EXISTS(SELECT 1 FROM public.sales s WHERE s.tenant_id=d.tenant_id AND s.sale_id=d.sale_id AND s.status='COMPLETED'))
    OR (d.direction='PURCHASE' AND public.sf_transfer_permission('PURCHASES','VIEW')));
 ELSIF kind='TRANSFER' THEN
  SELECT jsonb_build_object('type',kind,'id',t.transfer_id,'number',t.transfer_number,'date',t.requested_at::date,'direction','OUTWARD',
   'registrationId',r.registration_id,'registration',to_jsonb(r)-'authorized_contact','status',t.status,
   'toLocationId',t.to_location_id,'lines',(SELECT jsonb_agg(jsonb_build_object('variantId',i.variant_id,'requestedQuantity',i.requested_quantity,'dispatchedQuantity',i.dispatched_quantity) ORDER BY i.transfer_item_id)
     FROM public.stock_transfer_items i WHERE i.tenant_id=t.tenant_id AND i.transfer_id=t.transfer_id)) INTO result
  FROM public.stock_transfers t JOIN public.gst_shop_mappings m ON m.tenant_id=t.tenant_id AND m.store_id=public.sf_store()
  JOIN public.gst_registrations r ON r.tenant_id=m.tenant_id AND r.registration_id=m.registration_id
  WHERE t.tenant_id=public.sf_tenant() AND t.transfer_id=id AND t.from_location_id=public.sf_location()
   AND coalesce(to_jsonb(t)->>'business_profile_code','ELECTRICAL')=public.sf_visible_business_profile()
   AND t.status IN('APPROVED','DISPATCHED','PARTIALLY_RECEIVED','RECEIVED') AND r.registration_id=reg AND r.status='ACTIVE'
   AND m.status='ACTIVE' AND m.business_profile_code=public.sf_visible_business_profile()
   AND t.requested_at::date BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity')
   AND t.requested_at::date BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity')
   AND public.sf_transfer_permission('STOCK_TRANSFER','VIEW');
 END IF;
 IF result IS NULL THEN RAISE EXCEPTION 'Source unavailable in this shop/profile or missing effective registration' USING ERRCODE='42501'; END IF;
 RETURN result;
END $$;
CREATE TABLE public.eway_documents(
 tenant_id bigint NOT NULL,document_id uuid NOT NULL,store_id bigint NOT NULL,location_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,
 registration_id bigint NOT NULL,source_type text NOT NULL CHECK(source_type IN('GST','TRANSFER')),source_id bigint NOT NULL,
 source_snapshot jsonb NOT NULL,body jsonb NOT NULL,version bigint NOT NULL CHECK(version>0),
 status text NOT NULL CHECK(status IN('DRAFT','EXTERNAL_UNVERIFIED')),external_ebn varchar(12),external_evidence varchar(1000),
 created_by integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,document_id),
 FOREIGN KEY(tenant_id,registration_id) REFERENCES public.gst_registrations(tenant_id,registration_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id),
 CHECK((status='DRAFT' AND external_ebn IS NULL) OR (status='EXTERNAL_UNVERIFIED' AND external_ebn IS NOT NULL AND external_evidence IS NOT NULL AND external_ebn~'^[0-9]{12}$' AND length(btrim(external_evidence))>=10)));
CREATE UNIQUE INDEX ux_eway_external ON public.eway_documents(tenant_id,external_ebn) WHERE external_ebn IS NOT NULL;
CREATE INDEX ix_eway_list ON public.eway_documents(tenant_id,store_id,location_id,business_profile_code,created_at DESC);
CREATE TABLE public.eway_events(
 tenant_id bigint NOT NULL,operation_id uuid NOT NULL,document_id uuid NOT NULL,version bigint NOT NULL,action text NOT NULL,
 input jsonb NOT NULL,snapshot jsonb NOT NULL,actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,operation_id),FOREIGN KEY(tenant_id,document_id) REFERENCES public.eway_documents(tenant_id,document_id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id));
CREATE TRIGGER eway_history_immutable BEFORE UPDATE OR DELETE ON public.eway_events FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_immutable();
ALTER TABLE public.eway_documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.eway_documents FORCE ROW LEVEL SECURITY;
ALTER TABLE public.eway_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.eway_events FORCE ROW LEVEL SECURITY;
CREATE POLICY eway_read ON public.eway_documents FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant()
 AND store_id=public.sf_store() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile()
 AND public.sf_eway_allowed('VIEW') AND CASE WHEN source_type='GST' THEN
  EXISTS(SELECT 1 FROM public.gst_documents d WHERE d.tenant_id=eway_documents.tenant_id AND d.document_id=source_id
   AND ((d.direction='SALES' AND public.sf_transfer_permission('SALES','VIEW')) OR (d.direction='PURCHASE' AND public.sf_transfer_permission('PURCHASES','VIEW'))))
 ELSE public.sf_transfer_permission('STOCK_TRANSFER','VIEW') END);
CREATE POLICY eway_event_read ON public.eway_events FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant()
 AND EXISTS(SELECT 1 FROM public.eway_documents d WHERE d.tenant_id=eway_events.tenant_id AND d.document_id=eway_events.document_id));
GRANT SELECT ON public.eway_documents,public.eway_events TO stockflow_runtime;
CREATE FUNCTION public.sf_eway_write(id uuid,op uuid,expected bigint,act text,kind text,source bigint,reg bigint,payload jsonb) RETURNS bigint
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE d public.eway_documents; prior public.eway_events; snap jsonb; n bigint; original jsonb; movement_day date;
BEGIN
 IF NOT public.sf_eway_allowed('VIEW') OR NOT public.sf_eway_allowed(CASE WHEN act='SAVE' THEN CASE WHEN expected=0 THEN 'PREPARE' ELSE 'EDIT' END ELSE act END)
 THEN RAISE EXCEPTION 'E-way action denied' USING ERRCODE='42501'; END IF;
 IF id IS NULL OR op IS NULL OR expected IS NULL OR expected<0 OR act IS NULL OR kind IS NULL OR source IS NULL OR reg IS NULL OR act NOT IN('SAVE','RECORD_EXTERNAL','EXPORT','VALIDATE') OR payload IS NULL OR pg_column_size(payload)>200000
 THEN RAISE EXCEPTION 'Invalid e-way request' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('eway:'||public.sf_tenant()::text||':'||op::text,0));
 original:=jsonb_build_object('expected',expected,'kind',kind,'source',source,'registration',reg,'body',payload);
 SELECT * INTO prior FROM public.eway_events WHERE tenant_id=public.sf_tenant() AND operation_id=op;
 IF FOUND THEN
  IF prior.document_id<>id OR prior.actor_id<>public.sf_actor() OR prior.action<>act OR prior.input IS DISTINCT FROM original
   OR NOT EXISTS(SELECT 1 FROM public.eway_documents x WHERE x.tenant_id=public.sf_tenant() AND x.document_id=id AND x.store_id=public.sf_store() AND x.location_id=public.sf_location() AND x.business_profile_code=public.sf_visible_business_profile())
   THEN RAISE EXCEPTION 'E-way retry identity conflicts with saved operation' USING ERRCODE='23514'; END IF;
  PERFORM public.sf_eway_source(kind,source,reg);
  RETURN prior.version;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('eway-document:'||public.sf_tenant()::text||':'||id::text,0));
 SELECT * INTO d FROM public.eway_documents WHERE tenant_id=public.sf_tenant() AND document_id=id FOR UPDATE;
 IF FOUND THEN
  IF d.store_id<>public.sf_store() OR d.location_id<>public.sf_location() OR d.business_profile_code<>public.sf_visible_business_profile()
   OR d.source_type<>kind OR d.source_id<>source OR d.registration_id<>reg THEN RAISE EXCEPTION 'E-way source identity cannot change' USING ERRCODE='42501'; END IF;
  IF d.version<>expected THEN RAISE EXCEPTION 'Preparation changed; reload before continuing' USING ERRCODE='40001'; END IF;
 ELSE
  IF expected<>0 OR act<>'SAVE' THEN RAISE EXCEPTION 'Preparation not found' USING ERRCODE='23514'; END IF;
 END IF;
 -- Invoker function inside this definer still explicitly validates source/shop/action authority.
 snap:=public.sf_eway_source(kind,source,reg);
 IF kind='TRANSFER' AND act IN('SAVE','RECORD_EXTERNAL') THEN
  BEGIN movement_day:=NULLIF(CASE WHEN act='SAVE' THEN payload->>'movementDocumentDate' ELSE d.body->>'movementDocumentDate' END,'')::date;
  EXCEPTION WHEN invalid_datetime_format OR datetime_field_overflow THEN RAISE EXCEPTION 'Enter a valid movement document date' USING ERRCODE='23514'; END;
  IF movement_day IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.gst_shop_mappings m JOIN public.gst_registrations r USING(tenant_id,registration_id)
   WHERE m.tenant_id=public.sf_tenant() AND m.store_id=public.sf_store() AND m.business_profile_code=public.sf_visible_business_profile()
    AND m.registration_id=reg AND m.status='ACTIVE' AND r.status='ACTIVE'
    AND movement_day BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity')
    AND movement_day BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity'))
   THEN RAISE EXCEPTION 'Movement date needs an effective selected-shop GST registration mapping' USING ERRCODE='23514'; END IF;
 END IF;
 IF act='SAVE' THEN
  IF jsonb_typeof(payload) IS DISTINCT FROM 'object' OR jsonb_typeof(payload->'billFrom') IS DISTINCT FROM 'object'
   OR jsonb_typeof(payload->'billTo') IS DISTINCT FROM 'object' OR jsonb_typeof(payload->'dispatchFrom') IS DISTINCT FROM 'object'
   OR jsonb_typeof(payload->'shipTo') IS DISTINCT FROM 'object' OR jsonb_typeof(payload->'transferGoods') IS DISTINCT FROM 'array'
   THEN RAISE EXCEPTION 'Preparation needs structured parties and goods' USING ERRCODE='23514'; END IF;
  IF jsonb_array_length(payload->'transferGoods')>200 OR (kind='GST' AND jsonb_array_length(payload->'transferGoods')<>0)
   THEN RAISE EXCEPTION 'GST source goods cannot be replaced; transfer worksheets allow at most 200 lines' USING ERRCODE='23514'; END IF;
  IF d.status='EXTERNAL_UNVERIFIED' THEN RAISE EXCEPTION 'External reference recorded; preserve its immutable preparation' USING ERRCODE='23514'; END IF;
  n:=expected+1;
  IF expected=0 THEN
   INSERT INTO public.eway_documents VALUES(public.sf_tenant(),id,public.sf_store(),public.sf_location(),public.sf_visible_business_profile(),reg,kind,source,snap,payload,n,'DRAFT',NULL,NULL,public.sf_actor(),now(),now());
  ELSE
   UPDATE public.eway_documents SET source_snapshot=snap,body=payload,version=n,updated_at=now() WHERE tenant_id=d.tenant_id AND document_id=id;
  END IF;
 ELSIF act='RECORD_EXTERNAL' THEN
  IF d.status<>'DRAFT' OR coalesce(payload->>'ebn','') !~ '^[0-9]{12}$' OR length(btrim(coalesce(payload->>'evidence','')))<10 OR length(payload->>'evidence')>1000
   THEN RAISE EXCEPTION 'Record a 12-digit external EBN and evidence (10-1000 characters)' USING ERRCODE='23514'; END IF;
  IF snap IS DISTINCT FROM d.source_snapshot THEN RAISE EXCEPTION 'Source changed; revise and review before recording external evidence' USING ERRCODE='23514'; END IF;
  n:=expected+1;
  UPDATE public.eway_documents SET external_ebn=payload->>'ebn',external_evidence=payload->>'evidence',status='EXTERNAL_UNVERIFIED',version=n,updated_at=now() WHERE tenant_id=d.tenant_id AND document_id=id;
 ELSE
  IF snap IS DISTINCT FROM d.source_snapshot THEN RAISE EXCEPTION 'Source changed; revise preparation first' USING ERRCODE='23514'; END IF;
  n:=d.version;
 END IF;
 INSERT INTO public.eway_events VALUES(public.sf_tenant(),op,id,n,act,original,
  (SELECT jsonb_build_object('source',x.source_snapshot,'body',x.body,'status',x.status,'externalEbn',x.external_ebn,'externalEvidence',x.external_evidence)
   FROM public.eway_documents x WHERE x.tenant_id=public.sf_tenant() AND x.document_id=id),public.sf_actor(),now());
 RETURN n;
END $$;
REVOKE ALL ON FUNCTION public.sf_eway_allowed(text),public.sf_eway_source(text,bigint,bigint),public.sf_eway_write(uuid,uuid,bigint,text,text,bigint,bigint,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_eway_allowed(text),public.sf_eway_source(text,bigint,bigint),public.sf_eway_write(uuid,uuid,bigint,text,text,bigint,bigint,jsonb) TO stockflow_runtime;
COMMIT;
