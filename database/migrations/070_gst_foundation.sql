-- MANUAL ONLY, after 069. Back up and stop writers; run the entire file as postgres.
-- Additive, opt-in GST configuration. No legacy tax backfill or operational changes.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run GST migration as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_protect_product_edit()') IS NULL THEN RAISE EXCEPTION 'Apply migration 069 before GST foundation'; END IF;
END $$;
CREATE FUNCTION public.sf_gst_allowed(action text, module text DEFAULT 'GST_SETTINGS') RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.users u JOIN public.user_action_permissions p ON p.user_id=u.user_id AND p.tenant_id=u.tenant_id
 JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
 WHERE u.tenant_id=public.sf_tenant() AND u.user_id=public.sf_actor() AND u.is_active
 AND p.is_allowed AND a.is_active AND m.is_active AND a.action_code=action AND m.module_code=module)
$$;
REVOKE ALL ON FUNCTION public.sf_gst_allowed(text,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_gst_allowed(text,text) TO stockflow_runtime;
CREATE FUNCTION public.sf_gst_valid_gstin(value text) RETURNS boolean LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
DECLARE alphabet text:='0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'; factor integer:=2; total integer:=0; product integer; i integer; BEGIN
 IF value IS NULL OR value !~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$' THEN RETURN false; END IF;
 FOR i IN REVERSE 14..1 LOOP product:=(strpos(alphabet,substr(value,i,1))-1)*factor;total:=total+product/36+product%36;factor:=CASE factor WHEN 2 THEN 1 ELSE 2 END; END LOOP;
 RETURN substr(alphabet,((36-total%36)%36)+1,1)=substr(value,15,1);
END $$;
REVOKE ALL ON FUNCTION public.sf_gst_valid_gstin(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_gst_valid_gstin(text) TO stockflow_runtime;

INSERT INTO public.modules(module_code,module_name,route,sort_order) VALUES
 ('GST_SETTINGS','GST Settings','/Admin/Gst',89),('GST_REPORTS','GST & Tax Reports','/Reports/Gst',79);
UPDATE public.modules SET parent_module_id=(SELECT module_id FROM public.modules WHERE module_code='SETUP'),show_in_navigation=true WHERE module_code='GST_SETTINGS';
INSERT INTO public.module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
 SELECT module_id,module_id,'VIEW' FROM public.modules WHERE module_code='GST_SETTINGS';
INSERT INTO public.module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
 SELECT r.module_id,g.module_id,'VIEW' FROM public.modules r CROSS JOIN public.modules g WHERE r.module_code='REPORTS' AND g.module_code='GST_REPORTS';
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,x.code,x.name,x.code<>'VIEW' FROM public.modules m CROSS JOIN (VALUES
 ('VIEW','View GST settings'),('EDIT','Manage GST settings')) x(code,name) WHERE m.module_code='GST_SETTINGS';
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,x.code,x.name,x.code<>'VIEW' FROM public.modules m CROSS JOIN (VALUES
 ('VIEW','View selected shop GST reports'),('VIEW_REGISTRATION','View complete registration tax reports'),
 ('CREATE','Record supplier tax invoices and notes'),('REVIEW','Review GST working documents'),('EXPORT','Export GST working reports')) x(code,name)
 WHERE m.module_code='GST_REPORTS';
-- New sensitive permissions go only to verified tenant owners; other staff need explicit assignment.
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a
 JOIN public.modules m USING(module_id) WHERE u.is_tenant_owner AND m.module_code IN('GST_SETTINGS','GST_REPORTS');
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a
 JOIN public.modules m USING(module_id) WHERE m.module_code IN('GST_SETTINGS','GST_REPORTS') ON CONFLICT DO NOTHING;

CREATE TABLE public.gst_legal_businesses(
 business_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 company_settings_id integer,legal_name varchar(200) NOT NULL CHECK(length(btrim(legal_name))>=2),trade_name varchar(200),
 constitution varchar(80),pan varchar(10),address varchar(1000) NOT NULL CHECK(length(btrim(address))>=3),
 state_code varchar(2) NOT NULL CHECK(state_code~'^[0-9]{2}$'),pin_code varchar(6) NOT NULL CHECK(pin_code~'^[1-9][0-9]{5}$'),
 mobile varchar(10),email varchar(150),created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,business_id),FOREIGN KEY(tenant_id,company_settings_id) REFERENCES public.company_settings(tenant_id,company_settings_id));
CREATE TABLE public.gst_registrations(
 registration_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),business_id bigint NOT NULL,
 gstin varchar(15),registration_type varchar(20) NOT NULL CHECK(registration_type IN('REGULAR','COMPOSITION','UNREGISTERED','OTHER')),
 other_type varchar(100),state_code varchar(2) NOT NULL CHECK(state_code~'^[0-9]{2}$'),
 status varchar(12) NOT NULL DEFAULT 'ACTIVE' CHECK(status IN('ACTIVE','INACTIVE')),
 effective_from date NOT NULL,effective_to date,authorized_contact varchar(200),
 address varchar(1000) NOT NULL CHECK(length(btrim(address))>=3),
 created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,registration_id),FOREIGN KEY(tenant_id,business_id) REFERENCES public.gst_legal_businesses(tenant_id,business_id),
 CHECK(effective_to IS NULL OR effective_to>=effective_from),
 CHECK((registration_type='UNREGISTERED' AND gstin IS NULL) OR (registration_type<>'UNREGISTERED' AND gstin~'^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$')),
 CHECK(gstin IS NULL OR (left(gstin,2)=state_code AND public.sf_gst_valid_gstin(gstin))),CHECK(registration_type<>'OTHER' OR length(btrim(other_type))>0));
CREATE UNIQUE INDEX ux_gst_registration_identity ON public.gst_registrations(tenant_id,gstin) WHERE gstin IS NOT NULL;
CREATE TABLE public.gst_shop_mappings(
 mapping_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),registration_id bigint NOT NULL,
 business_profile_code varchar(40) NOT NULL CHECK(business_profile_code IN('ELECTRICAL','BIKE_ACCESSORIES','CLOTHING','FOOTWEAR')),
 store_id bigint NOT NULL,effective_from date NOT NULL,effective_to date,
 status varchar(12) NOT NULL DEFAULT 'ACTIVE' CHECK(status IN('ACTIVE','INACTIVE')),
 created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,registration_id) REFERENCES public.gst_registrations(tenant_id,registration_id),
 FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id),CHECK(effective_to IS NULL OR effective_to>=effective_from));
CREATE INDEX ix_gst_mapping_lookup ON public.gst_shop_mappings(tenant_id,business_profile_code,store_id,effective_from);
CREATE TABLE public.gst_tax_configurations(
 tax_config_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 business_profile_code varchar(40) NOT NULL,product_id integer NOT NULL,
 classification varchar(10) NOT NULL CHECK(classification IN('GOODS','SERVICE')),
 hsn_sac varchar(8) NOT NULL CHECK(hsn_sac~'^[0-9]{4}([0-9]{2})?([0-9]{2})?$'),uqc varchar(3) NOT NULL CHECK(uqc~'^[A-Z]{3}$'),
 treatment varchar(12) NOT NULL CHECK(treatment IN('TAXABLE','NIL','EXEMPT','NON_GST','ZERO_RATED')),
 tax_rate numeric(7,4) NOT NULL CHECK(tax_rate BETWEEN 0 AND 100),cess_rate numeric(7,4) NOT NULL DEFAULT 0 CHECK(cess_rate BETWEEN 0 AND 100),
 reverse_charge boolean NOT NULL DEFAULT false,prices_include_tax boolean,
 effective_from date NOT NULL,effective_to date,review_reference varchar(300) NOT NULL CHECK(length(btrim(review_reference))>=3),
 created_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,product_id) REFERENCES public.products(tenant_id,product_id),
 CHECK(effective_to IS NULL OR effective_to>=effective_from),CHECK(treatment='TAXABLE' OR tax_rate=0));
CREATE TABLE public.gst_audit_history(
 audit_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),actor_id integer NOT NULL DEFAULT public.sf_actor(),
 entity varchar(50) NOT NULL,record_id bigint NOT NULL,action varchar(20) NOT NULL,old_value jsonb,new_value jsonb,
 created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX ix_gst_tax_lookup ON public.gst_tax_configurations(tenant_id,business_profile_code,product_id,effective_from);
CREATE INDEX ix_gst_audit_history ON public.gst_audit_history(tenant_id,audit_id DESC);

CREATE FUNCTION public.sf_gst_config_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE r public.gst_registrations; BEGIN
 IF current_user='stockflow_runtime' AND NOT public.sf_gst_allowed('EDIT') THEN RAISE EXCEPTION 'GST configuration permission required' USING ERRCODE='42501'; END IF;
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'GST configuration history cannot be deleted' USING ERRCODE='23514'; END IF;
 IF TG_OP='UPDATE' AND NEW.tenant_id<>OLD.tenant_id THEN RAISE EXCEPTION 'Tenant cannot change' USING ERRCODE='23514'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('gst-config:'||NEW.tenant_id::text,0));
 IF TG_TABLE_NAME='gst_shop_mappings' THEN
  IF NOT EXISTS(SELECT 1 FROM public.stores WHERE tenant_id=NEW.tenant_id AND store_id=NEW.store_id AND is_active) THEN RAISE EXCEPTION 'Choose an authorized active store' USING ERRCODE='23514'; END IF;
  SELECT * INTO STRICT r FROM public.gst_registrations WHERE tenant_id=NEW.tenant_id AND registration_id=NEW.registration_id;
  IF NEW.effective_from<r.effective_from OR (r.effective_to IS NOT NULL AND (NEW.effective_to IS NULL OR NEW.effective_to>r.effective_to)) THEN
   RAISE EXCEPTION 'Mapping dates must be within registration dates' USING ERRCODE='23514'; END IF;
  IF NEW.status='ACTIVE' AND EXISTS(SELECT 1 FROM public.gst_shop_mappings m WHERE m.tenant_id=NEW.tenant_id AND m.store_id=NEW.store_id
    AND m.business_profile_code=NEW.business_profile_code AND m.status='ACTIVE' AND m.mapping_id<>coalesce(NEW.mapping_id,0)
    AND daterange(m.effective_from,m.effective_to,'[]') && daterange(NEW.effective_from,NEW.effective_to,'[]')) THEN
   RAISE EXCEPTION 'Shop GST mapping overlaps an existing mapping' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='gst_tax_configurations' THEN
  IF TG_OP='UPDATE' AND (to_jsonb(NEW)-'effective_to') IS DISTINCT FROM (to_jsonb(OLD)-'effective_to') THEN
   RAISE EXCEPTION 'Create a new tax version instead of changing its values' USING ERRCODE='23514'; END IF;
  IF NOT EXISTS(SELECT 1 FROM public.products p JOIN public.categories c USING(tenant_id,category_id)
    WHERE p.tenant_id=NEW.tenant_id AND p.product_id=NEW.product_id AND c.business_profile_code=NEW.business_profile_code) THEN
   RAISE EXCEPTION 'Select a product in the selected shop' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM public.gst_tax_configurations c WHERE c.tenant_id=NEW.tenant_id AND c.product_id=NEW.product_id
    AND c.tax_config_id<>coalesce(NEW.tax_config_id,0) AND daterange(c.effective_from,c.effective_to,'[]') && daterange(NEW.effective_from,NEW.effective_to,'[]')) THEN
   RAISE EXCEPTION 'Tax version dates overlap' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='gst_registrations' THEN
  IF NEW.gstin IS NOT NULL AND EXISTS(SELECT 1 FROM public.gst_legal_businesses b WHERE b.tenant_id=NEW.tenant_id AND b.business_id=NEW.business_id AND b.pan IS NOT NULL AND b.pan<>substr(NEW.gstin,3,10)) THEN
   RAISE EXCEPTION 'GSTIN PAN differs from the legal business' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM public.gst_shop_mappings m WHERE m.tenant_id=NEW.tenant_id AND m.registration_id=NEW.registration_id AND m.status='ACTIVE'
   AND (m.effective_from<NEW.effective_from OR coalesce(m.effective_to,'infinity')>coalesce(NEW.effective_to,'infinity'))) THEN
   RAISE EXCEPTION 'Registration dates must contain its active mapping dates' USING ERRCODE='23514'; END IF;
 ELSIF TG_TABLE_NAME='gst_legal_businesses' AND NEW.pan IS NOT NULL THEN
  IF EXISTS(SELECT 1 FROM public.gst_registrations existing_reg WHERE existing_reg.tenant_id=NEW.tenant_id AND existing_reg.business_id=NEW.business_id AND existing_reg.gstin IS NOT NULL AND substr(existing_reg.gstin,3,10)<>NEW.pan) THEN
   RAISE EXCEPTION 'PAN cannot conflict with linked GST registrations' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE FUNCTION public.sf_gst_audit() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE body jsonb:=to_jsonb(NEW); identity_key text:=TG_ARGV[0]; BEGIN
 INSERT INTO public.gst_audit_history(tenant_id,actor_id,entity,record_id,action,old_value,new_value)
 VALUES(NEW.tenant_id,public.sf_actor(),TG_TABLE_NAME,(body->>identity_key)::bigint,TG_OP,CASE WHEN TG_OP='UPDATE' THEN to_jsonb(OLD) END,body);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION public.sf_gst_config_guard(),public.sf_gst_audit() FROM PUBLIC;
DO $$ DECLARE spec text[]; BEGIN
 FOREACH spec SLICE 1 IN ARRAY ARRAY[['gst_legal_businesses','business_id'],['gst_registrations','registration_id'],['gst_shop_mappings','mapping_id'],['gst_tax_configurations','tax_config_id']] LOOP
  EXECUTE format('CREATE TRIGGER gst_guard BEFORE INSERT OR UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_gst_config_guard()',spec[1]);
  EXECUTE format('CREATE TRIGGER gst_audit AFTER INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_gst_audit(%L)',spec[1],spec[2]);
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',spec[1]);
  EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',spec[1]);
  EXECUTE format('CREATE POLICY gst_tenant ON public.%I USING(tenant_id=public.sf_tenant()) WITH CHECK(tenant_id=public.sf_tenant())',spec[1]);
  EXECUTE format('CREATE POLICY gst_read ON public.%I AS RESTRICTIVE FOR SELECT USING(public.sf_gst_allowed(''VIEW'') OR public.sf_gst_allowed(''EDIT'') OR public.sf_gst_allowed(''VIEW'',''GST_REPORTS''))',spec[1]);
  EXECUTE format('CREATE POLICY gst_insert ON public.%I AS RESTRICTIVE FOR INSERT WITH CHECK(public.sf_gst_allowed(''EDIT''))',spec[1]);
  EXECUTE format('CREATE POLICY gst_update ON public.%I AS RESTRICTIVE FOR UPDATE USING(public.sf_gst_allowed(''EDIT'')) WITH CHECK(public.sf_gst_allowed(''EDIT''))',spec[1]);
  EXECUTE format('GRANT SELECT,INSERT,UPDATE ON public.%I TO stockflow_runtime',spec[1]);
 END LOOP;
END $$;
-- Report readers see only registrations mapped to their selected shop unless explicitly authorized for registration-wide tax reporting.
CREATE POLICY gst_registration_scope ON public.gst_registrations AS RESTRICTIVE FOR SELECT USING(
 public.sf_gst_allowed('VIEW') OR public.sf_gst_allowed('EDIT') OR public.sf_gst_allowed('VIEW_REGISTRATION','GST_REPORTS')
 OR EXISTS(SELECT 1 FROM public.gst_shop_mappings m WHERE m.tenant_id=gst_registrations.tenant_id AND m.registration_id=gst_registrations.registration_id
 AND m.business_profile_code=public.sf_visible_business_profile() AND m.store_id=public.sf_store()));
CREATE POLICY gst_mapping_scope ON public.gst_shop_mappings AS RESTRICTIVE FOR SELECT USING(
 public.sf_gst_allowed('VIEW') OR public.sf_gst_allowed('EDIT') OR public.sf_gst_allowed('VIEW_REGISTRATION','GST_REPORTS')
 OR (business_profile_code=public.sf_visible_business_profile() AND store_id=public.sf_store()));
CREATE POLICY gst_tax_scope ON public.gst_tax_configurations AS RESTRICTIVE FOR SELECT USING(business_profile_code=public.sf_visible_business_profile());
CREATE POLICY gst_business_scope ON public.gst_legal_businesses AS RESTRICTIVE FOR SELECT USING(
 public.sf_gst_allowed('VIEW') OR public.sf_gst_allowed('EDIT') OR EXISTS(SELECT 1 FROM public.gst_registrations r WHERE r.tenant_id=gst_legal_businesses.tenant_id AND r.business_id=gst_legal_businesses.business_id));
ALTER TABLE public.gst_audit_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.gst_audit_history FORCE ROW LEVEL SECURITY;
CREATE POLICY gst_audit_read ON public.gst_audit_history FOR SELECT USING(tenant_id=public.sf_tenant() AND public.sf_gst_allowed('VIEW')
 AND entity IN('gst_legal_businesses','gst_registrations','gst_shop_mappings','gst_tax_configurations')
 AND (entity<>'gst_tax_configurations' OR new_value->>'business_profile_code'=public.sf_visible_business_profile()));
GRANT SELECT ON public.gst_audit_history TO stockflow_runtime;
GRANT USAGE ON SEQUENCE public.gst_legal_businesses_business_id_seq,public.gst_registrations_registration_id_seq,public.gst_shop_mappings_mapping_id_seq,public.gst_tax_configurations_tax_config_id_seq TO stockflow_runtime;
COMMIT;
