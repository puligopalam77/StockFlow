-- MANUAL ONLY. Back up, stop writers, run the ENTIRE file as postgres after 071.
-- No customer copies, invoice rewrites, stock posting or outbound messaging.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regclass('public.gst_documents') IS NULL THEN RAISE EXCEPTION 'Apply 071 first'; END IF;
END $$;
INSERT INTO public.modules(module_code,module_name,route,sort_order,show_in_navigation)
 VALUES('CRM','Customer Relationships',NULL,65,true);
UPDATE public.modules SET navigation_icon='users' WHERE module_code='CRM';
INSERT INTO public.modules(module_code,module_name,route,sort_order,parent_module_id,show_in_navigation)
 SELECT x.code,x.name,x.route,x.ord,m.module_id,true FROM public.modules m CROSS JOIN (VALUES
 ('CRM_OVERVIEW','Customer Overview','/Crm',1),('CRM_ENQUIRIES','Enquiries & Follow-ups','/Crm/Enquiries',2)) x(code,name,route,ord) WHERE m.module_code='CRM';
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,x.code,x.name,x.code<>'VIEW' FROM public.modules m CROSS JOIN (VALUES
 ('VIEW','View CRM customers and assigned enquiries'),('CREATE','Create enquiries and link existing customers'),('EDIT','Edit enquiries and customer CRM preferences'),
 ('ASSIGN','Assign enquiries to shop staff'),('FOLLOW_UP','Record follow-ups'),('CLOSE','Close and reopen enquiries'),('VIEW_TEAM','View team enquiries'),
 ('VIEW_HISTORY','View customer purchase history'),('VIEW_FINANCIAL','View customer financial information')) x(code,name) WHERE m.module_code='CRM';
INSERT INTO public.module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
 SELECT n.module_id,m.module_id,'VIEW' FROM public.modules n CROSS JOIN public.modules m WHERE n.module_code IN('CRM','CRM_OVERVIEW','CRM_ENQUIRIES') AND m.module_code='CRM';
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id) WHERE u.is_tenant_owner AND m.module_code='CRM';
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id) WHERE m.module_code='CRM' ON CONFLICT DO NOTHING;
CREATE FUNCTION public.sf_crm_allowed(action text) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.users u JOIN public.user_action_permissions p USING(tenant_id,user_id)
 JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
 WHERE u.tenant_id=public.sf_tenant() AND u.user_id=public.sf_actor() AND u.is_active AND p.is_allowed AND a.is_active AND m.is_active AND m.module_code='CRM' AND a.action_code=action)
$$;
CREATE FUNCTION public.sf_crm_staff(id integer) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.users u WHERE u.tenant_id=public.sf_tenant() AND u.user_id=id AND u.is_active
 AND (u.is_tenant_owner OR EXISTS(SELECT 1 FROM public.user_store_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.store_id=public.sf_store() AND a.is_active)
 OR EXISTS(SELECT 1 FROM public.user_location_access a JOIN public.store_locations l USING(tenant_id,location_id) WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND l.store_id=public.sf_store()))
 AND EXISTS(SELECT 1 FROM public.user_action_permissions p JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
 WHERE p.tenant_id=u.tenant_id AND p.user_id=u.user_id AND p.is_allowed AND a.is_active AND m.is_active AND m.module_code='CRM' AND a.action_code='VIEW'))
$$;
CREATE TABLE public.crm_customers(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),store_id bigint NOT NULL DEFAULT public.sf_store(),
 customer_id integer NOT NULL,customer_type varchar(12) NOT NULL DEFAULT 'INDIVIDUAL' CHECK(customer_type IN('INDIVIDUAL','BUSINESS')),
 preferred_method varchar(12) CHECK(preferred_method IN('CALL','WHATSAPP','EMAIL','IN_PERSON','OTHER')),preferences varchar(2000),
 created_at timestamptz NOT NULL DEFAULT now(),created_by integer NOT NULL DEFAULT public.sf_actor(),version integer NOT NULL DEFAULT 1,
 PRIMARY KEY(tenant_id,business_profile_code,store_id,customer_id),FOREIGN KEY(tenant_id,customer_id) REFERENCES public.customers(tenant_id,customer_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id));
-- Only proven shop relationships are backfilled. Unsold legacy contacts can be linked explicitly through CRM.
INSERT INTO public.crm_customers(tenant_id,business_profile_code,store_id,customer_id,created_at,created_by)
 SELECT tenant_id,business_profile_code,store_id,customer_id,min(sale_date AT TIME ZONE current_setting('TimeZone')),min(created_by)
 FROM public.sales WHERE customer_id IS NOT NULL GROUP BY tenant_id,business_profile_code,store_id,customer_id;
CREATE TABLE public.crm_enquiries(
 enquiry_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),store_id bigint NOT NULL DEFAULT public.sf_store(),
 customer_id integer NOT NULL,enquiry_type varchar(30) NOT NULL CHECK(enquiry_type IN('AVAILABILITY','PRICE','PRODUCT_REQUEST','ORDER_FOLLOW_UP','GENERAL','OTHER')),
 subject varchar(200) NOT NULL CHECK(length(btrim(subject))>=3),description varchar(2000),product_id integer,variant_id bigint,requested_quantity numeric(12,3) CHECK(requested_quantity>0 AND requested_quantity<=10000),
 priority varchar(10) NOT NULL CHECK(priority IN('LOW','NORMAL','HIGH')),assigned_to integer NOT NULL,
 status varchar(30) NOT NULL DEFAULT 'NEW' CHECK(status IN('NEW','IN_PROGRESS','WAITING_CUSTOMER','WAITING_STOCK','RESOLVED','CLOSED','CANCELLED')),
 next_follow_up timestamptz,contact_method varchar(12) NOT NULL CHECK(contact_method IN('CALL','WHATSAPP','EMAIL','IN_PERSON','OTHER')),
 created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),created_by integer NOT NULL DEFAULT public.sf_actor(),version integer NOT NULL DEFAULT 1,
 request_id uuid NOT NULL,request_payload jsonb NOT NULL,
 UNIQUE(tenant_id,enquiry_id),UNIQUE(tenant_id,request_id),
 FOREIGN KEY(tenant_id,business_profile_code,store_id,customer_id) REFERENCES public.crm_customers(tenant_id,business_profile_code,store_id,customer_id),
 FOREIGN KEY(tenant_id,assigned_to) REFERENCES public.users(tenant_id,user_id),FOREIGN KEY(tenant_id,product_id) REFERENCES public.products(tenant_id,product_id),
 FOREIGN KEY(tenant_id,variant_id) REFERENCES public.product_variants(tenant_id,variant_id),FOREIGN KEY(product_id,variant_id) REFERENCES public.product_variants(product_id,variant_id),
 CHECK(variant_id IS NULL OR product_id IS NOT NULL),CHECK(status NOT IN('RESOLVED','CLOSED','CANCELLED') OR next_follow_up IS NULL));
CREATE TABLE public.crm_activities(
 activity_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),enquiry_id bigint NOT NULL,
 kind varchar(20) NOT NULL CHECK(kind IN('CREATED','EDITED','FOLLOW_UP','SALE_LINKED')),occurred_at timestamptz NOT NULL DEFAULT now(),recorded_at timestamptz NOT NULL DEFAULT now(),
 method varchar(12),outcome varchar(30),notes varchar(2000),next_follow_up timestamptz,status varchar(30) NOT NULL,
 actor_id integer NOT NULL DEFAULT public.sf_actor(),request_id uuid NOT NULL,payload jsonb NOT NULL,
 UNIQUE(tenant_id,request_id),FOREIGN KEY(tenant_id,enquiry_id) REFERENCES public.crm_enquiries(tenant_id,enquiry_id),FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id));
CREATE TABLE public.crm_customer_notes(
 note_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),store_id bigint NOT NULL DEFAULT public.sf_store(),
 customer_id integer NOT NULL,notes varchar(2000) NOT NULL CHECK(length(btrim(notes))>=3),created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT now(),request_id uuid NOT NULL,
 UNIQUE(tenant_id,request_id),FOREIGN KEY(tenant_id,business_profile_code,store_id,customer_id) REFERENCES public.crm_customers(tenant_id,business_profile_code,store_id,customer_id));
CREATE TABLE public.crm_sale_links(
 tenant_id bigint NOT NULL,enquiry_id bigint NOT NULL,sale_id bigint NOT NULL,location_id bigint NOT NULL,converted_at timestamptz NOT NULL DEFAULT now(),converted_by integer NOT NULL,
 PRIMARY KEY(tenant_id,enquiry_id),UNIQUE(tenant_id,sale_id),FOREIGN KEY(tenant_id,enquiry_id) REFERENCES public.crm_enquiries(tenant_id,enquiry_id),
 FOREIGN KEY(tenant_id,location_id,sale_id) REFERENCES public.sales(tenant_id,location_id,sale_id));
CREATE INDEX ix_crm_due ON public.crm_enquiries(tenant_id,business_profile_code,store_id,assigned_to,next_follow_up);
CREATE INDEX ix_crm_customer ON public.crm_enquiries(tenant_id,customer_id,updated_at DESC);
CREATE INDEX ix_crm_activity ON public.crm_activities(tenant_id,enquiry_id,activity_id);
CREATE FUNCTION public.sf_crm_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT public.sf_crm_allowed('VIEW') OR NEW.tenant_id<>public.sf_tenant() OR NEW.store_id<>public.sf_store() OR NEW.business_profile_code<>public.sf_visible_business_profile() OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Choose an authorized CRM shop and location' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND (NEW.tenant_id,NEW.business_profile_code,NEW.store_id,NEW.customer_id,NEW.created_by,NEW.created_at,NEW.request_id,NEW.request_payload) IS DISTINCT FROM (OLD.tenant_id,OLD.business_profile_code,OLD.store_id,OLD.customer_id,OLD.created_by,OLD.created_at,OLD.request_id,OLD.request_payload) THEN RAISE EXCEPTION 'Enquiry identity is permanent' USING ERRCODE='23514'; END IF;
 IF NOT public.sf_crm_staff(NEW.assigned_to) THEN RAISE EXCEPTION 'Assigned staff must have CRM access to this shop' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' AND NEW.assigned_to<>public.sf_actor() AND NOT public.sf_crm_allowed('ASSIGN') THEN RAISE EXCEPTION 'Assignment permission required' USING ERRCODE='42501'; END IF;
 IF TG_OP='UPDATE' AND NEW.assigned_to<>OLD.assigned_to AND NOT public.sf_crm_allowed('ASSIGN') THEN RAISE EXCEPTION 'Assignment permission required' USING ERRCODE='42501'; END IF;
 IF TG_OP='UPDATE' AND NEW.status<>OLD.status THEN
  IF (OLD.status IN('RESOLVED','CLOSED','CANCELLED') OR NEW.status IN('RESOLVED','CLOSED','CANCELLED')) AND NOT public.sf_crm_allowed('CLOSE') THEN RAISE EXCEPTION 'Close/reopen permission required' USING ERRCODE='42501'; END IF;
  IF OLD.status IN('RESOLVED','CLOSED','CANCELLED') AND NEW.status<>'IN_PROGRESS' THEN RAISE EXCEPTION 'Reopen as In Progress before changing status' USING ERRCODE='23514'; END IF;
 END IF;
 IF TG_OP='UPDATE' AND NOT public.sf_crm_allowed('EDIT') AND (to_jsonb(NEW)-ARRAY['status','next_follow_up','contact_method','version','updated_at']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','next_follow_up','contact_method','version','updated_at']) THEN RAISE EXCEPTION 'Edit permission required to change enquiry details' USING ERRCODE='42501'; END IF;
 IF NEW.product_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=NEW.tenant_id AND p.product_id=NEW.product_id AND p.identifier_profile=NEW.business_profile_code AND p.is_active) THEN RAISE EXCEPTION 'Select an active product in this shop' USING ERRCODE='23514'; END IF;
 IF NEW.variant_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=NEW.tenant_id AND v.product_id=NEW.product_id AND v.variant_id=NEW.variant_id AND v.is_active) THEN RAISE EXCEPTION 'Select an active variant of this product' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' THEN NEW.version:=OLD.version+1; END IF; NEW.updated_at:=now(); RETURN NEW;
END $$;
CREATE TRIGGER crm_enquiry_guard BEFORE INSERT OR UPDATE ON public.crm_enquiries FOR EACH ROW EXECUTE FUNCTION public.sf_crm_guard();
CREATE FUNCTION public.sf_crm_append_only() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'CRM history is append-only' USING ERRCODE='23514'; END $$;
CREATE TRIGGER crm_activity_immutable BEFORE UPDATE OR DELETE ON public.crm_activities FOR EACH ROW EXECUTE FUNCTION public.sf_crm_append_only();
CREATE TRIGGER crm_notes_immutable BEFORE UPDATE OR DELETE ON public.crm_customer_notes FOR EACH ROW EXECUTE FUNCTION public.sf_crm_append_only();
CREATE TRIGGER crm_links_immutable BEFORE UPDATE OR DELETE ON public.crm_sale_links FOR EACH ROW EXECUTE FUNCTION public.sf_crm_append_only();
DO $$ DECLARE tab text; BEGIN FOREACH tab IN ARRAY ARRAY['crm_customers','crm_enquiries','crm_activities','crm_customer_notes','crm_sale_links'] LOOP
 EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab); EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab);
 EXECUTE format('GRANT SELECT ON public.%I TO stockflow_runtime',tab); END LOOP; END $$;
CREATE POLICY crm_customer_read ON public.crm_customers FOR SELECT USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND public.sf_crm_allowed('VIEW') AND public.sf_can_location(public.sf_location()) AND EXISTS(SELECT 1 FROM public.customers c WHERE c.customer_id=crm_customers.customer_id));
CREATE POLICY crm_customer_insert ON public.crm_customers FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND created_by=public.sf_actor() AND public.sf_crm_allowed('CREATE') AND public.sf_can_location(public.sf_location()) AND EXISTS(SELECT 1 FROM public.customers c WHERE c.customer_id=crm_customers.customer_id));
CREATE POLICY crm_customer_edit ON public.crm_customers FOR UPDATE USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND public.sf_crm_allowed('EDIT')) WITH CHECK(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store());
CREATE POLICY crm_enquiry_read ON public.crm_enquiries FOR SELECT USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND public.sf_can_location(public.sf_location()) AND public.sf_crm_allowed('VIEW') AND (assigned_to=public.sf_actor() OR public.sf_crm_allowed('VIEW_TEAM')));
CREATE POLICY crm_enquiry_insert ON public.crm_enquiries FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND public.sf_crm_allowed('CREATE') AND created_by=public.sf_actor());
CREATE POLICY crm_enquiry_edit ON public.crm_enquiries FOR UPDATE USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND (assigned_to=public.sf_actor() OR public.sf_crm_allowed('VIEW_TEAM')) AND (public.sf_crm_allowed('EDIT') OR public.sf_crm_allowed('FOLLOW_UP'))) WITH CHECK(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store());
CREATE POLICY crm_activity_read ON public.crm_activities FOR SELECT USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.crm_enquiries e WHERE e.enquiry_id=crm_activities.enquiry_id));
CREATE POLICY crm_activity_insert ON public.crm_activities FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND actor_id=public.sf_actor() AND EXISTS(SELECT 1 FROM public.crm_enquiries e WHERE e.enquiry_id=crm_activities.enquiry_id) AND (public.sf_crm_allowed('CREATE') OR public.sf_crm_allowed('EDIT') OR public.sf_crm_allowed('FOLLOW_UP')));
CREATE POLICY crm_note_read ON public.crm_customer_notes FOR SELECT USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.crm_customers c WHERE c.tenant_id=crm_customer_notes.tenant_id AND c.customer_id=crm_customer_notes.customer_id AND c.store_id=crm_customer_notes.store_id AND c.business_profile_code=crm_customer_notes.business_profile_code));
CREATE POLICY crm_note_insert ON public.crm_customer_notes FOR INSERT WITH CHECK(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND created_by=public.sf_actor() AND public.sf_crm_allowed('EDIT') AND EXISTS(SELECT 1 FROM public.crm_customers c WHERE c.customer_id=crm_customer_notes.customer_id));
CREATE POLICY crm_link_read ON public.crm_sale_links FOR SELECT USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.crm_enquiries e WHERE e.enquiry_id=crm_sale_links.enquiry_id));
GRANT INSERT ON public.crm_customers,public.crm_enquiries,public.crm_activities,public.crm_customer_notes TO stockflow_runtime;
GRANT UPDATE(customer_type,preferred_method,preferences,version) ON public.crm_customers TO stockflow_runtime;
GRANT UPDATE(subject,description,product_id,variant_id,requested_quantity,priority,assigned_to,status,next_follow_up,contact_method,version,updated_at) ON public.crm_enquiries TO stockflow_runtime;
GRANT USAGE ON SEQUENCE public.crm_enquiries_enquiry_id_seq,public.crm_activities_activity_id_seq,public.crm_customer_notes_note_id_seq TO stockflow_runtime;
-- Definer link is narrowly authorized and only runs inside original checkout transaction.
CREATE FUNCTION public.sf_crm_link_sale(enquiry bigint,id bigint) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE e public.crm_enquiries; s public.sales; BEGIN
 IF NOT public.sf_crm_allowed('VIEW') OR NOT public.sf_gst_allowed('CREATE','SALES') THEN RAISE EXCEPTION 'CRM and Sales creation permissions required' USING ERRCODE='42501'; END IF;
 SELECT * INTO STRICT e FROM public.crm_enquiries WHERE tenant_id=public.sf_tenant() AND enquiry_id=enquiry AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND (assigned_to=public.sf_actor() OR public.sf_crm_allowed('VIEW_TEAM')) FOR UPDATE;
 SELECT * INTO STRICT s FROM public.sales WHERE tenant_id=e.tenant_id AND sale_id=id AND store_id=e.store_id AND business_profile_code=e.business_profile_code AND location_id=public.sf_location() AND public.sf_can_location(location_id) AND status='COMPLETED' AND customer_id=e.customer_id AND created_by=public.sf_actor() AND xmin=pg_current_xact_id()::xid;
 IF e.status IN('CLOSED','CANCELLED','RESOLVED') OR EXISTS(SELECT 1 FROM public.crm_sale_links WHERE tenant_id=e.tenant_id AND enquiry_id=enquiry) THEN RAISE EXCEPTION 'Enquiry is closed or already linked to an invoice' USING ERRCODE='23514'; END IF;
 INSERT INTO public.crm_sale_links(tenant_id,enquiry_id,sale_id,location_id,converted_by) VALUES(e.tenant_id,enquiry,id,s.location_id,public.sf_actor());
 INSERT INTO public.crm_activities(tenant_id,enquiry_id,kind,status,request_id,payload,notes) VALUES(e.tenant_id,enquiry,'SALE_LINKED',e.status,s.request_id,jsonb_build_object('saleId',id),'Invoice linked after normal checkout');
END $$;
REVOKE ALL ON FUNCTION public.sf_crm_allowed(text),public.sf_crm_staff(integer),public.sf_crm_link_sale(bigint,bigint),public.sf_crm_guard(),public.sf_crm_append_only() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_crm_allowed(text),public.sf_crm_staff(integer),public.sf_crm_link_sale(bigint,bigint) TO stockflow_runtime;
-- Registration reuses the same customer. Only the active authorized shop is linked.
CREATE FUNCTION public.sf_crm_customer_created() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF public.sf_store()>0 AND public.sf_can_location(public.sf_location()) THEN
  INSERT INTO public.crm_customers(tenant_id,business_profile_code,store_id,customer_id,created_by)
  VALUES(NEW.tenant_id,NEW.business_profile_code,public.sf_store(),NEW.customer_id,public.sf_actor()) ON CONFLICT DO NOTHING;
 END IF; RETURN NEW; END $$;
CREATE TRIGGER crm_customer_created AFTER INSERT ON public.customers FOR EACH ROW EXECUTE FUNCTION public.sf_crm_customer_created();
REVOKE ALL ON FUNCTION public.sf_crm_customer_created() FROM PUBLIC;
CREATE TABLE public.crm_preference_history(
 history_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,store_id bigint NOT NULL,customer_id integer NOT NULL,
 before_value jsonb NOT NULL,after_value jsonb NOT NULL,actor_id integer NOT NULL,recorded_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,business_profile_code,store_id,customer_id) REFERENCES public.crm_customers(tenant_id,business_profile_code,store_id,customer_id));
CREATE FUNCTION public.sf_crm_preference_audit() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 INSERT INTO public.crm_preference_history(tenant_id,business_profile_code,store_id,customer_id,before_value,after_value,actor_id)
 VALUES(OLD.tenant_id,OLD.business_profile_code,OLD.store_id,OLD.customer_id,to_jsonb(OLD),to_jsonb(NEW),public.sf_actor()); RETURN NEW; END $$;
CREATE TRIGGER crm_preference_audit AFTER UPDATE ON public.crm_customers FOR EACH ROW EXECUTE FUNCTION public.sf_crm_preference_audit();
CREATE TRIGGER crm_preference_immutable BEFORE UPDATE OR DELETE ON public.crm_preference_history FOR EACH ROW EXECUTE FUNCTION public.sf_crm_append_only();
ALTER TABLE public.crm_preference_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.crm_preference_history FORCE ROW LEVEL SECURITY;
CREATE POLICY crm_preference_read ON public.crm_preference_history FOR SELECT USING(tenant_id=public.sf_tenant() AND business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store() AND EXISTS(SELECT 1 FROM public.crm_customers c WHERE c.customer_id=crm_preference_history.customer_id));
GRANT SELECT ON public.crm_preference_history TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_crm_preference_audit() FROM PUBLIC;
COMMIT;
