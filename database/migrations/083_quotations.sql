-- MANUAL ONLY, after 082. Back up and stop writers; run ENTIRE file as postgres.
-- Non-posting quotations. Does not repartition masters, rewrite invoices or select a costing policy.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_billing_policy()') IS NULL THEN RAISE EXCEPTION 'Apply 082 first'; END IF;
END $$;
INSERT INTO public.modules(module_code,module_name,route,sort_order,parent_module_id,show_in_navigation,navigation_icon)
 SELECT 'QUOTATIONS','Quotations & Estimates','/Quotations',3,module_id,true,'document' FROM public.modules WHERE module_name='Sell / Billing';
-- Some installations use a differently captioned sales group.
INSERT INTO public.modules(module_code,module_name,route,sort_order,show_in_navigation,navigation_icon)
 SELECT 'QUOTATIONS','Quotations & Estimates','/Quotations',35,true,'document' WHERE NOT EXISTS(SELECT 1 FROM public.modules WHERE module_code='QUOTATIONS');
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,x.code,x.name,x.code NOT IN('VIEW','PRINT','EXPORT') FROM public.modules m CROSS JOIN (VALUES
 ('VIEW','View quotations'),('CREATE','Create quotations'),('EDIT','Revise quotations'),('STATUS','Record quotation lifecycle decisions'),
 ('CONVERT','Open accepted quotation in checkout'),('PRINT','Print quotation revisions'),('EXPORT','Export quotations')) x(code,name) WHERE m.module_code='QUOTATIONS';
INSERT INTO public.module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
 SELECT module_id,module_id,'VIEW' FROM public.modules WHERE module_code='QUOTATIONS';
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE u.is_tenant_owner AND m.module_code='QUOTATIONS' ON CONFLICT DO NOTHING;
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE m.module_code='QUOTATIONS' ON CONFLICT DO NOTHING;
CREATE FUNCTION public.sf_quotation_allowed(act text) RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_store()>0 AND public.sf_location()>0 AND public.sf_can_location(public.sf_location())
 AND EXISTS(SELECT 1 FROM public.store_locations l WHERE l.tenant_id=public.sf_tenant() AND l.location_id=public.sf_location() AND l.store_id=public.sf_store() AND l.is_active)
 AND public.sf_visible_business_profile() IS NOT NULL AND public.sf_transfer_permission('QUOTATIONS',act)
$$;
CREATE TABLE public.quotation_counters(tenant_id bigint NOT NULL,store_id bigint NOT NULL,last_number bigint NOT NULL CHECK(last_number>0),
 PRIMARY KEY(tenant_id,store_id),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id));
CREATE TABLE public.quotations(
 tenant_id bigint NOT NULL,store_id bigint NOT NULL,location_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,
 quotation_id uuid NOT NULL,quotation_number text NOT NULL,version bigint NOT NULL DEFAULT 1 CHECK(version>0),current_revision integer NOT NULL DEFAULT 0,
 status text NOT NULL CHECK(status IN('DRAFT','SENT','ACCEPTED','REJECTED','EXPIRED','CONVERTED','CANCELLED')),
 customer_id integer NOT NULL,customer_name text NOT NULL,document_date date NOT NULL,valid_until date NOT NULL CHECK(valid_until>=document_date),
 total numeric(14,2) NOT NULL CHECK(total>=0),item_count integer NOT NULL CHECK(item_count BETWEEN 1 AND 500),created_by integer NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,quotation_id),UNIQUE(tenant_id,store_id,quotation_number),
 UNIQUE(tenant_id,store_id,location_id,business_profile_code,quotation_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,customer_id) REFERENCES public.customers(tenant_id,customer_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id));
CREATE INDEX ix_quotation_list ON public.quotations(tenant_id,store_id,location_id,business_profile_code,document_date DESC,quotation_id);
CREATE TABLE public.quotation_revisions(
 tenant_id bigint NOT NULL,store_id bigint NOT NULL,location_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,
 quotation_id uuid NOT NULL,revision integer NOT NULL CHECK(revision>0),body jsonb NOT NULL,prepared_by integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,quotation_id,revision),
 FOREIGN KEY(tenant_id,store_id,location_id,business_profile_code,quotation_id) REFERENCES public.quotations(tenant_id,store_id,location_id,business_profile_code,quotation_id),
 FOREIGN KEY(tenant_id,prepared_by) REFERENCES public.users(tenant_id,user_id));
CREATE TABLE public.quotation_revision_lines(
 tenant_id bigint NOT NULL,quotation_id uuid NOT NULL,revision integer NOT NULL,line_number integer NOT NULL CHECK(line_number>0),snapshot jsonb NOT NULL,
 PRIMARY KEY(tenant_id,quotation_id,revision,line_number),
 FOREIGN KEY(tenant_id,quotation_id,revision) REFERENCES public.quotation_revisions(tenant_id,quotation_id,revision));
CREATE TABLE public.quotation_events(
 event_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,quotation_id uuid NOT NULL,revision integer NOT NULL,action text NOT NULL,notes text NOT NULL,
 actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,quotation_id,revision) REFERENCES public.quotation_revisions(tenant_id,quotation_id,revision),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id));
CREATE TABLE public.quotation_operations(
 tenant_id bigint NOT NULL,operation_id uuid NOT NULL,quotation_id uuid NOT NULL,actor_id integer NOT NULL,action text NOT NULL,payload jsonb NOT NULL,result_version bigint NOT NULL,
 PRIMARY KEY(tenant_id,operation_id),FOREIGN KEY(tenant_id,quotation_id) REFERENCES public.quotations(tenant_id,quotation_id));
CREATE TABLE public.quotation_conversions(
 tenant_id bigint NOT NULL,quotation_id uuid NOT NULL,revision integer NOT NULL,sale_id bigint NOT NULL,actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,quotation_id),UNIQUE(tenant_id,sale_id),
 FOREIGN KEY(tenant_id,quotation_id,revision) REFERENCES public.quotation_revisions(tenant_id,quotation_id,revision),
 FOREIGN KEY(tenant_id,sale_id) REFERENCES public.sales(tenant_id,sale_id));
-- Ordinary runtime can only read. Narrow writer functions enforce every mutation.
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['quotation_counters','quotations','quotation_revisions','quotation_revision_lines','quotation_events','quotation_operations','quotation_conversions'] LOOP
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab);
  EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab);
 END LOOP;
 GRANT SELECT ON public.quotations,public.quotation_revisions,public.quotation_revision_lines,public.quotation_events,public.quotation_conversions TO stockflow_runtime;
END $$;
CREATE POLICY quotation_read ON public.quotations FOR SELECT TO stockflow_runtime USING(
 tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND location_id=public.sf_location()
 AND business_profile_code=public.sf_visible_business_profile() AND public.sf_quotation_allowed('VIEW'));
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['quotation_revisions','quotation_revision_lines','quotation_events','quotation_conversions'] LOOP
  EXECUTE format('CREATE POLICY quotation_child_read ON public.%I FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.quotations q WHERE q.tenant_id=%I.tenant_id AND q.quotation_id=%I.quotation_id))',tab,tab,tab);
 END LOOP;
END $$;
CREATE FUNCTION public.sf_quotation_immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$ BEGIN
 RAISE EXCEPTION 'Quotation history is immutable; create a revision or lifecycle event' USING ERRCODE='23514';
END $$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['quotation_revisions','quotation_revision_lines','quotation_events','quotation_operations','quotation_conversions'] LOOP
  EXECUTE format('CREATE TRIGGER quotation_immutable BEFORE UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_immutable()',tab);
 END LOOP;
END $$;
CREATE FUNCTION public.sf_quotation_write(id uuid,expected bigint,op uuid,act text,body jsonb) RETURNS bigint
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE q public.quotations; old public.quotation_operations; n bigint; rev integer; target text; customer integer; today date:=(now() AT TIME ZONE 'Asia/Kolkata')::date;
BEGIN
 IF NOT public.sf_quotation_allowed('VIEW') OR NOT public.sf_quotation_allowed(CASE WHEN act='SAVE' THEN CASE WHEN expected=0 THEN 'CREATE' ELSE 'EDIT' END ELSE 'STATUS' END)
 THEN RAISE EXCEPTION 'Quotation action not permitted in selected shop/location' USING ERRCODE='42501'; END IF;
 IF id IS NULL OR op IS NULL OR act NOT IN('SAVE','STATE') OR body IS NULL THEN RAISE EXCEPTION 'Invalid quotation request' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('quotation-operation:'||public.sf_tenant()::text||':'||op::text,0));
 SELECT * INTO old FROM public.quotation_operations WHERE tenant_id=public.sf_tenant() AND operation_id=op;
 IF FOUND THEN
  IF old.quotation_id<>id OR old.actor_id<>public.sf_actor() OR old.action<>act OR (old.payload->>'expected')::bigint<>expected
   OR (CASE WHEN act='SAVE' THEN (old.payload->'body'->'input') IS DISTINCT FROM (body->'input') ELSE old.payload->'body' IS DISTINCT FROM body END)
  THEN RAISE EXCEPTION 'Retry identity already used for different quotation values' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM public.quotations h WHERE h.tenant_id=public.sf_tenant() AND h.quotation_id=id AND h.store_id=public.sf_store() AND h.location_id=public.sf_location() AND h.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Quotation unavailable' USING ERRCODE='42501'; END IF;
  RETURN old.result_version;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('quotation:'||public.sf_tenant()::text||':'||id::text,0));
 SELECT * INTO q FROM public.quotations WHERE tenant_id=public.sf_tenant() AND quotation_id=id FOR UPDATE;
 IF FOUND THEN
  IF q.store_id<>public.sf_store() OR q.location_id<>public.sf_location() OR q.business_profile_code<>public.sf_visible_business_profile() THEN RAISE EXCEPTION 'Quotation unavailable in this shop' USING ERRCODE='42501'; END IF;
  IF q.version<>expected THEN RAISE EXCEPTION 'Quotation changed. Reload the saved revision before continuing' USING ERRCODE='40001'; END IF;
 ELSE
  IF expected<>0 OR act<>'SAVE' THEN RAISE EXCEPTION 'Quotation not found' USING ERRCODE='23514'; END IF;
 END IF;
 IF q.status IN('CONVERTED','CANCELLED') THEN RAISE EXCEPTION 'Converted or cancelled quotations cannot change' USING ERRCODE='23514'; END IF;
 IF act='SAVE' THEN
  customer:=(body->'request'->>'CustomerId')::integer;
  IF customer IS NULL OR NOT EXISTS(SELECT 1 FROM public.customers c WHERE c.tenant_id=public.sf_tenant() AND c.customer_id=customer AND c.is_active
   AND (c.business_profile_code=public.sf_visible_business_profile() OR public.sf_shared_contact_profile('CUSTOMER',customer))) THEN RAISE EXCEPTION 'Choose an active accessible customer' USING ERRCODE='23514'; END IF;
  IF jsonb_array_length(body->'lines') NOT BETWEEN 1 AND 500 OR jsonb_array_length(body->'request'->'Items')<>jsonb_array_length(body->'lines')
   OR (body->>'validUntil')::date<(body->>'documentDate')::date OR (body->>'documentDate')::date>today
   OR (body->'quote'->>'TotalAmount')::numeric<0 THEN RAISE EXCEPTION 'Invalid quotation lines, dates or totals' USING ERRCODE='23514'; END IF;
  IF q.quotation_id IS NULL THEN
   INSERT INTO public.quotation_counters VALUES(public.sf_tenant(),public.sf_store(),1)
    ON CONFLICT(tenant_id,store_id) DO UPDATE SET last_number=quotation_counters.last_number+1 RETURNING last_number INTO n;
   INSERT INTO public.quotations(tenant_id,store_id,location_id,business_profile_code,quotation_id,quotation_number,status,customer_id,customer_name,document_date,valid_until,total,item_count,created_by)
    VALUES(public.sf_tenant(),public.sf_store(),public.sf_location(),public.sf_visible_business_profile(),id,'QTN-'||lpad(n::text,greatest(4,length(n::text)),'0'),'DRAFT',customer,body->'customer'->>'CustomerName',(body->>'documentDate')::date,(body->>'validUntil')::date,(body->'quote'->>'TotalAmount')::numeric,jsonb_array_length(body->'lines'),public.sf_actor()) RETURNING * INTO q;
  END IF;
  rev:=q.current_revision+1;
  INSERT INTO public.quotation_revisions VALUES(q.tenant_id,q.store_id,q.location_id,q.business_profile_code,id,rev,body,public.sf_actor(),now());
  INSERT INTO public.quotation_revision_lines SELECT q.tenant_id,id,rev,ordinality::integer,value FROM jsonb_array_elements(body->'lines') WITH ORDINALITY;
  UPDATE public.quotations SET current_revision=rev,status='DRAFT',customer_id=customer,customer_name=body->'customer'->>'CustomerName',document_date=(body->>'documentDate')::date,
   valid_until=(body->>'validUntil')::date,total=(body->'quote'->>'TotalAmount')::numeric,item_count=jsonb_array_length(body->'lines'),version=CASE WHEN expected=0 THEN 1 ELSE q.version+1 END,updated_at=now()
   WHERE tenant_id=q.tenant_id AND quotation_id=id RETURNING version INTO n;
  target:=CASE WHEN expected=0 THEN 'CREATED' ELSE 'REVISED' END;
 ELSE
  target:=body->>'status'; rev:=q.current_revision;
  IF length(btrim(coalesce(body->>'notes','')))<3 OR length(body->>'notes')>1000 THEN RAISE EXCEPTION 'Record how this lifecycle decision was confirmed (3-1000 characters)' USING ERRCODE='23514'; END IF;
  IF NOT ((target='SENT' AND q.status='DRAFT' AND q.valid_until>=today)
   OR (target IN('ACCEPTED','REJECTED') AND q.status='SENT' AND q.valid_until>=today)
   OR (target='EXPIRED' AND q.valid_until<today)
   OR target='CANCELLED') THEN RAISE EXCEPTION 'Invalid quotation lifecycle transition; revise expired or rejected quotations first' USING ERRCODE='23514'; END IF;
  UPDATE public.quotations SET status=target,version=version+1,updated_at=now() WHERE tenant_id=q.tenant_id AND quotation_id=id RETURNING version INTO n;
 END IF;
 INSERT INTO public.quotation_events(tenant_id,quotation_id,revision,action,notes,actor_id) VALUES(q.tenant_id,id,rev,target,CASE WHEN act='SAVE' THEN 'Commercial revision saved; no stock or financial posting' ELSE body->>'notes' END,public.sf_actor());
 INSERT INTO public.quotation_operations VALUES(q.tenant_id,op,id,public.sf_actor(),act,jsonb_build_object('expected',expected,'body',body),n);
 RETURN n;
END $$;
-- The existing sale transaction is the ONLY place a quotation becomes converted.
CREATE FUNCTION public.sf_quotation_saved_result(id uuid,expected bigint,op uuid,input jsonb) RETURNS bigint
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE old public.quotation_operations;
BEGIN
 IF NOT public.sf_quotation_allowed(CASE WHEN expected=0 THEN 'CREATE' ELSE 'EDIT' END) THEN RAISE EXCEPTION 'Quotation access denied' USING ERRCODE='42501'; END IF;
 SELECT * INTO old FROM public.quotation_operations WHERE tenant_id=public.sf_tenant() AND operation_id=op;
 IF NOT FOUND THEN RETURN NULL; END IF;
 IF old.quotation_id<>id OR old.actor_id<>public.sf_actor() OR old.action<>'SAVE' OR (old.payload->>'expected')::bigint<>expected OR old.payload->'body'->'input' IS DISTINCT FROM input
  THEN RAISE EXCEPTION 'Retry identity already used for different quotation values' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.quotations q WHERE q.tenant_id=public.sf_tenant() AND q.quotation_id=id AND q.store_id=public.sf_store() AND q.location_id=public.sf_location() AND q.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Quotation unavailable' USING ERRCODE='42501'; END IF;
 RETURN old.result_version;
END $$;
CREATE FUNCTION public.sf_quotation_sale_link() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE id uuid; q public.quotations; rev integer;
BEGIN
 id:=(NEW.request_payload->>'SourceQuotationId')::uuid; IF id IS NULL THEN RETURN NEW; END IF;
 IF NOT public.sf_quotation_allowed('CONVERT') OR NOT public.sf_transfer_permission('SALES','CREATE') THEN RAISE EXCEPTION 'Quotation conversion permission required' USING ERRCODE='42501'; END IF;
 SELECT * INTO q FROM public.quotations WHERE tenant_id=NEW.tenant_id AND quotation_id=id FOR UPDATE;
 rev:=(NEW.request_payload->>'SourceQuotationRevision')::integer;
 IF q.quotation_id IS NULL OR q.tenant_id<>public.sf_tenant() OR q.store_id<>public.sf_store() OR q.location_id<>public.sf_location()
  OR q.business_profile_code<>public.sf_visible_business_profile() OR NEW.business_profile_code<>q.business_profile_code OR NEW.created_by<>public.sf_actor() OR NEW.status<>'COMPLETED'
  OR NEW.store_id<>q.store_id OR NEW.location_id<>q.location_id OR NEW.customer_id IS DISTINCT FROM q.customer_id
  OR q.status<>'ACCEPTED' OR q.valid_until<(now() AT TIME ZONE 'Asia/Kolkata')::date
  OR rev IS DISTINCT FROM q.current_revision OR (NEW.request_payload->>'SourceQuotationVersion')::bigint IS DISTINCT FROM q.version
 THEN RAISE EXCEPTION 'Quotation changed, expired, already converted or belongs to another shop/customer. Reload it before checkout' USING ERRCODE='23514'; END IF;
 INSERT INTO public.quotation_conversions VALUES(q.tenant_id,id,rev,NEW.sale_id,public.sf_actor(),now());
 UPDATE public.quotations SET status='CONVERTED',version=version+1,updated_at=now() WHERE tenant_id=q.tenant_id AND quotation_id=id;
 INSERT INTO public.quotation_events(tenant_id,quotation_id,revision,action,notes,actor_id) VALUES(q.tenant_id,id,rev,'CONVERTED','Finalized sale '||NEW.sale_id::text,public.sf_actor());
 RETURN NEW;
END $$;
CREATE TRIGGER quotation_final_sale AFTER INSERT ON public.sales FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_sale_link();
REVOKE ALL ON FUNCTION public.sf_quotation_allowed(text),public.sf_quotation_write(uuid,bigint,uuid,text,jsonb),public.sf_quotation_saved_result(uuid,bigint,uuid,jsonb),public.sf_quotation_sale_link(),public.sf_quotation_immutable() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_quotation_allowed(text),public.sf_quotation_write(uuid,bigint,uuid,text,jsonb),public.sf_quotation_saved_result(uuid,bigint,uuid,jsonb) TO stockflow_runtime;
COMMIT;
