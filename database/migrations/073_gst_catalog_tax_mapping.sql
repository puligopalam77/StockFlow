-- MANUAL ONLY, after 072. Back up, stop writers; run this ENTIRE file as postgres.
-- Adds catalog administration; no product/SKU/price/stock or posted-document backfill.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regclass('public.crm_enquiries') IS NULL THEN RAISE EXCEPTION 'Apply 072 first'; END IF;
END $$;
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT m.module_id,x.code,x.name,true FROM public.modules m CROSS JOIN (VALUES
 ('APPROVE_TAX','Review reusable tax classifications'),('OVERRIDE_TAX','Replace reviewed tax mappings'),
 ('BULK_TAX','Assign tax rules in bulk'),('IMPORT_TAX','Import product tax mappings'),('EXPORT_TAX','Export product tax mappings')) x(code,name) WHERE m.module_code='GST_SETTINGS';
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE u.is_tenant_owner AND m.module_code='GST_SETTINGS' AND a.action_code IN('APPROVE_TAX','OVERRIDE_TAX','BULK_TAX','IMPORT_TAX','EXPORT_TAX');
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id)
 WHERE m.module_code='GST_SETTINGS' AND a.action_code IN('APPROVE_TAX','OVERRIDE_TAX','BULK_TAX','IMPORT_TAX','EXPORT_TAX') ON CONFLICT DO NOTHING;

CREATE TABLE public.gst_tax_rules(
 rule_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),
 store_id bigint NOT NULL DEFAULT public.sf_store(),business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),
 rule_name varchar(150) NOT NULL CHECK(length(btrim(rule_name))>=3),description varchar(2000) NOT NULL CHECK(length(btrim(description))>=3),
 tax jsonb NOT NULL CHECK(jsonb_typeof(tax)='object'),effective_from date NOT NULL,effective_to date,
 review_status varchar(12) NOT NULL DEFAULT 'DRAFT' CHECK(review_status IN('DRAFT','REVIEWED')),
 status varchar(10) NOT NULL CHECK(status IN('ACTIVE','INACTIVE')),notes varchar(2000),
 reviewed_by integer,reviewed_at timestamptz,created_by integer NOT NULL DEFAULT public.sf_actor(),created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,rule_id),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 CHECK(effective_to IS NULL OR effective_to>=effective_from),CHECK((review_status='REVIEWED')=(reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)));
CREATE INDEX ix_gst_rule_catalog ON public.gst_tax_rules(tenant_id,store_id,business_profile_code,effective_from);
ALTER TABLE public.gst_tax_configurations ADD COLUMN store_id bigint,
 ADD COLUMN variant_id bigint,ADD COLUMN rule_id bigint,ADD COLUMN change_reason varchar(1000),ADD COLUMN created_by integer DEFAULT public.sf_actor();
ALTER TABLE public.gst_tax_configurations ADD CONSTRAINT gst_tax_store_fk FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),
 ADD CONSTRAINT gst_tax_variant_fk FOREIGN KEY(tenant_id,variant_id) REFERENCES public.product_variants(tenant_id,variant_id),
 ADD CONSTRAINT gst_tax_rule_fk FOREIGN KEY(tenant_id,rule_id) REFERENCES public.gst_tax_rules(tenant_id,rule_id),
 ADD CONSTRAINT gst_tax_identity UNIQUE(tenant_id,tax_config_id);
CREATE INDEX ix_gst_tax_scoped ON public.gst_tax_configurations(tenant_id,store_id,business_profile_code,product_id,variant_id,effective_from);
CREATE INDEX ix_gst_catalog_products ON public.products(tenant_id,identifier_profile,is_active,product_name,product_id);
CREATE INDEX ix_gst_catalog_variants ON public.product_variants(tenant_id,product_id,is_active,variant_id) INCLUDE(sku);
-- Keep legacy profile-wide versions as explicit fallback; all newly written mappings are shop scoped.
CREATE POLICY gst_tax_shop ON public.gst_tax_configurations AS RESTRICTIVE FOR SELECT USING(store_id IS NULL OR store_id=public.sf_store());
CREATE POLICY gst_tax_shop_insert ON public.gst_tax_configurations AS RESTRICTIVE FOR INSERT WITH CHECK(store_id=public.sf_store() AND public.sf_can_location(public.sf_location()));
CREATE POLICY gst_tax_shop_update ON public.gst_tax_configurations AS RESTRICTIVE FOR UPDATE USING(store_id=public.sf_store()) WITH CHECK(store_id=public.sf_store());

CREATE FUNCTION public.sf_gst_rule_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('gst-config:'||NEW.tenant_id::text,0));
 IF NOT public.sf_gst_allowed('EDIT') OR NEW.store_id<>public.sf_store() OR NEW.business_profile_code<>public.sf_visible_business_profile() OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Authorized GST shop required' USING ERRCODE='42501'; END IF;
 IF TG_OP='UPDATE' AND OLD.review_status='REVIEWED' THEN
  IF NOT public.sf_gst_allowed('OVERRIDE_TAX') OR NOT public.sf_gst_allowed('APPROVE_TAX') OR NEW.effective_to IS NULL OR NEW.effective_to<CURRENT_DATE OR (OLD.effective_to IS NOT NULL AND NEW.effective_to>OLD.effective_to)
   OR (to_jsonb(NEW)-ARRAY['effective_to','tax','notes']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['effective_to','tax','notes']) OR (NEW.tax-'effectiveTo') IS DISTINCT FROM (OLD.tax-'effectiveTo')
   OR EXISTS(SELECT 1 FROM public.gst_tax_configurations t WHERE t.rule_id=OLD.rule_id AND (t.effective_to IS NULL OR t.effective_to>NEW.effective_to)) THEN RAISE EXCEPTION 'Only an authorized prospective rule end date is allowed; existing assignments must fit its validity' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 IF TG_OP='UPDATE' AND (NEW.tenant_id,NEW.store_id,NEW.business_profile_code,NEW.rule_id,NEW.created_by,NEW.created_at) IS DISTINCT FROM (OLD.tenant_id,OLD.store_id,OLD.business_profile_code,OLD.rule_id,OLD.created_by,OLD.created_at) THEN RAISE EXCEPTION 'Rule identity is permanent' USING ERRCODE='23514'; END IF;
 IF NEW.review_status='REVIEWED' THEN
  IF NOT public.sf_gst_allowed('APPROVE_TAX') OR length(btrim(NEW.tax->>'reviewReference'))<3 THEN RAISE EXCEPTION 'Classification review permission and evidence required' USING ERRCODE='42501'; END IF;
  NEW.reviewed_by:=public.sf_actor();NEW.reviewed_at:=now();
 ELSE NEW.reviewed_by:=NULL;NEW.reviewed_at:=NULL; END IF;
 IF EXISTS(SELECT 1 FROM public.gst_tax_rules r WHERE r.tenant_id=NEW.tenant_id AND r.store_id=NEW.store_id AND r.business_profile_code=NEW.business_profile_code AND r.rule_id<>coalesce(NEW.rule_id,0) AND upper(btrim(r.rule_name))=upper(btrim(NEW.rule_name)) AND daterange(r.effective_from,r.effective_to,'[]')&&daterange(NEW.effective_from,NEW.effective_to,'[]')) THEN RAISE EXCEPTION 'Rule name has overlapping versions; use distinct names for conditional classifications' USING ERRCODE='23514'; END IF;
 RETURN NEW; END $$;
CREATE TRIGGER gst_rule_guard BEFORE INSERT OR UPDATE ON public.gst_tax_rules FOR EACH ROW EXECUTE FUNCTION public.sf_gst_rule_guard();
CREATE TRIGGER gst_rule_no_delete BEFORE DELETE ON public.gst_tax_rules FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_rule_audit AFTER INSERT OR UPDATE ON public.gst_tax_rules FOR EACH ROW EXECUTE FUNCTION public.sf_gst_audit('rule_id');

CREATE TABLE public.gst_tax_operations(
 operation_id uuid PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),store_id bigint NOT NULL DEFAULT public.sf_store(),
 business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),created_by integer NOT NULL DEFAULT public.sf_actor(),
 request jsonb NOT NULL,selection_summary jsonb NOT NULL DEFAULT '{}',status varchar(12) NOT NULL DEFAULT 'PREVIEW' CHECK(status IN('PREVIEW','RUNNING','COMPLETED')),
 created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),UNIQUE(tenant_id,operation_id));
CREATE TABLE public.gst_tax_operation_rows(
 tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),operation_id uuid NOT NULL,row_number integer NOT NULL,
 product_id integer NOT NULL,variant_id bigint,product_name varchar(250) NOT NULL,
 status varchar(24) NOT NULL CHECK(status IN('READY','MATCHING','REVIEW','INVALID','UPDATED','FAILED')),
 message varchar(2000) NOT NULL,before_value jsonb,proposed_value jsonb,fingerprint text NOT NULL,
 PRIMARY KEY(tenant_id,operation_id,row_number),FOREIGN KEY(tenant_id,operation_id) REFERENCES public.gst_tax_operations(tenant_id,operation_id));
CREATE INDEX ix_gst_operation_scope ON public.gst_tax_operations(tenant_id,store_id,business_profile_code,created_by,created_at DESC);
CREATE INDEX ix_gst_operation_pending ON public.gst_tax_operation_rows(tenant_id,operation_id,status,row_number);
DO $$ DECLARE tab text; BEGIN FOREACH tab IN ARRAY ARRAY['gst_tax_rules','gst_tax_operations'] LOOP
 EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab);EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab);
 EXECUTE format('CREATE POLICY gst_catalog_scope ON public.%I USING(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_can_location(public.sf_location()) AND public.sf_gst_allowed(''VIEW'')) WITH CHECK(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_can_location(public.sf_location()) AND public.sf_gst_allowed(''EDIT''))',tab);
 EXECUTE format('GRANT SELECT,INSERT,UPDATE ON public.%I TO stockflow_runtime',tab);
 END LOOP;END $$;
CREATE POLICY gst_operation_owner ON public.gst_tax_operations AS RESTRICTIVE USING(created_by=public.sf_actor()) WITH CHECK(created_by=public.sf_actor());
ALTER TABLE public.gst_tax_operation_rows ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.gst_tax_operation_rows FORCE ROW LEVEL SECURITY;
CREATE POLICY gst_operation_row_scope ON public.gst_tax_operation_rows USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.gst_tax_operations o WHERE o.operation_id=gst_tax_operation_rows.operation_id)) WITH CHECK(tenant_id=public.sf_tenant() AND public.sf_gst_allowed('EDIT') AND EXISTS(SELECT 1 FROM public.gst_tax_operations o WHERE o.operation_id=gst_tax_operation_rows.operation_id));
GRANT SELECT,INSERT,UPDATE ON public.gst_tax_operation_rows TO stockflow_runtime;
GRANT USAGE ON SEQUENCE public.gst_tax_rules_rule_id_seq TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_gst_rule_guard() FROM PUBLIC;
CREATE FUNCTION public.sf_gst_operation_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF TG_TABLE_NAME='gst_tax_operations' THEN
  IF (to_jsonb(NEW)-ARRAY['status','updated_at','selection_summary']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','updated_at','selection_summary']) OR OLD.status='COMPLETED'
   OR (NEW.selection_summary IS DISTINCT FROM OLD.selection_summary AND (OLD.status<>'PREVIEW' OR NEW.status<>'PREVIEW')) OR (OLD.status='RUNNING' AND NEW.status='PREVIEW') THEN RAISE EXCEPTION 'Operation identity and completed results are permanent' USING ERRCODE='23514'; END IF;
 ELSE
  IF OLD.status<>'READY' OR NEW.status NOT IN('UPDATED','FAILED') OR (to_jsonb(NEW)-ARRAY['status','message']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','message']) THEN RAISE EXCEPTION 'Only pending tax rows may be completed once' USING ERRCODE='23514'; END IF;
 END IF;RETURN NEW;END $$;
CREATE TRIGGER gst_operation_guard BEFORE UPDATE ON public.gst_tax_operations FOR EACH ROW EXECUTE FUNCTION public.sf_gst_operation_guard();
CREATE TRIGGER gst_operation_row_guard BEFORE UPDATE ON public.gst_tax_operation_rows FOR EACH ROW EXECUTE FUNCTION public.sf_gst_operation_guard();
CREATE TRIGGER gst_operation_no_delete BEFORE DELETE ON public.gst_tax_operations FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
CREATE TRIGGER gst_operation_row_no_delete BEFORE DELETE ON public.gst_tax_operation_rows FOR EACH ROW EXECUTE FUNCTION public.sf_gst_immutable();
REVOKE ALL ON FUNCTION public.sf_gst_operation_guard() FROM PUBLIC;

-- Replace only the existing product-tax branch; retain all registration and shop guards.
DO $migration$ DECLARE definition text; old_branch text; new_branch text; BEGIN
 definition:=pg_get_functiondef('public.sf_gst_config_guard()'::regprocedure);
 old_branch:=substring(definition FROM position(' ELSIF TG_TABLE_NAME=''gst_tax_configurations'' THEN' IN definition) FOR position(' ELSIF TG_TABLE_NAME=''gst_registrations'' THEN' IN definition)-position(' ELSIF TG_TABLE_NAME=''gst_tax_configurations'' THEN' IN definition));
 IF old_branch IS NULL OR old_branch='' THEN RAISE EXCEPTION 'Unexpected GST guard definition'; END IF;
 new_branch:=$branch$ ELSIF TG_TABLE_NAME='gst_tax_configurations' THEN
  IF TG_OP='UPDATE' AND NOT public.sf_gst_allowed('OVERRIDE_TAX') THEN RAISE EXCEPTION 'Tax override permission required to end a mapping' USING ERRCODE='42501'; END IF;
  IF TG_OP='UPDATE' AND (to_jsonb(NEW)-'effective_to') IS DISTINCT FROM (to_jsonb(OLD)-'effective_to') THEN RAISE EXCEPTION 'Create a new tax version instead of changing values' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND (NEW.store_id IS NULL OR NEW.effective_to IS NULL OR NEW.effective_to<CURRENT_DATE OR (OLD.effective_to IS NOT NULL AND NEW.effective_to>OLD.effective_to)) THEN RAISE EXCEPTION 'Only prospective end dates on shop mappings may change' USING ERRCODE='23514'; END IF;
  IF NEW.store_id<>public.sf_store() OR NEW.business_profile_code<>public.sf_visible_business_profile() OR NOT public.sf_can_location(public.sf_location()) OR NOT EXISTS(SELECT 1 FROM public.products p WHERE p.tenant_id=NEW.tenant_id AND p.product_id=NEW.product_id AND p.identifier_profile=NEW.business_profile_code) THEN RAISE EXCEPTION 'Select an authorized shop product' USING ERRCODE='23514'; END IF;
  IF NEW.variant_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=NEW.tenant_id AND v.variant_id=NEW.variant_id AND v.product_id=NEW.product_id AND v.is_active) THEN RAISE EXCEPTION 'Variant does not belong to product' USING ERRCODE='23514'; END IF;
  IF TG_OP='INSERT' AND EXISTS(SELECT 1 FROM public.gst_tax_configurations prior_tax WHERE prior_tax.tenant_id=NEW.tenant_id AND prior_tax.product_id=NEW.product_id AND (prior_tax.store_id=NEW.store_id OR prior_tax.store_id IS NULL) AND (prior_tax.variant_id=NEW.variant_id OR prior_tax.variant_id IS NULL) AND daterange(prior_tax.effective_from,prior_tax.effective_to,'[]')&&daterange(NEW.effective_from,NEW.effective_to,'[]')
   AND (prior_tax.hsn_sac,prior_tax.treatment,prior_tax.tax_rate,prior_tax.cess_rate,prior_tax.prices_include_tax) IS DISTINCT FROM (NEW.hsn_sac,NEW.treatment,NEW.tax_rate,NEW.cess_rate,NEW.prices_include_tax))
   AND (NOT public.sf_gst_allowed('OVERRIDE_TAX') OR length(btrim(coalesce(NEW.change_reason,'')))<3) THEN RAISE EXCEPTION 'A different reviewed mapping requires override permission and a reason' USING ERRCODE='42501'; END IF;
  IF NEW.rule_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.gst_tax_rules rule_row WHERE rule_row.tenant_id=NEW.tenant_id AND rule_row.rule_id=NEW.rule_id AND rule_row.store_id=NEW.store_id AND rule_row.business_profile_code=NEW.business_profile_code AND rule_row.review_status='REVIEWED' AND rule_row.status='ACTIVE' AND NEW.effective_from>=rule_row.effective_from AND (rule_row.effective_to IS NULL OR NEW.effective_to IS NOT NULL AND NEW.effective_to<=rule_row.effective_to)
   AND (NEW.classification,NEW.hsn_sac,NEW.uqc,NEW.treatment,NEW.tax_rate,NEW.cess_rate,NEW.reverse_charge,NEW.prices_include_tax,NEW.review_reference) IS NOT DISTINCT FROM (rule_row.tax->>'classification',rule_row.tax->>'hsnSac',rule_row.tax->>'uqc',rule_row.tax->>'treatment',(rule_row.tax->>'taxRate')::numeric,(rule_row.tax->>'cessRate')::numeric,(rule_row.tax->>'reverseCharge')::boolean,(rule_row.tax->>'pricesIncludeTax')::boolean,rule_row.tax->>'reviewReference')) THEN RAISE EXCEPTION 'Mapping must agree with its reviewed effective rule' USING ERRCODE='23514'; END IF;
  IF EXISTS(SELECT 1 FROM public.gst_tax_configurations c WHERE c.tenant_id=NEW.tenant_id AND c.product_id=NEW.product_id AND c.store_id IS NOT DISTINCT FROM NEW.store_id AND c.variant_id IS NOT DISTINCT FROM NEW.variant_id AND c.tax_config_id<>coalesce(NEW.tax_config_id,0) AND daterange(c.effective_from,c.effective_to,'[]')&&daterange(NEW.effective_from,NEW.effective_to,'[]')) THEN RAISE EXCEPTION 'Tax version dates overlap' USING ERRCODE='23514'; END IF;
 $branch$;
 EXECUTE replace(definition,old_branch,new_branch);
END $migration$;

-- Resolve into the original configuration row type, with variant > shop default > legacy fallback.
CREATE FUNCTION public.sf_gst_resolve_tax(product integer,variant bigint,day date) RETURNS SETOF public.gst_tax_configurations LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT t.* FROM public.gst_tax_configurations t WHERE t.tenant_id=public.sf_tenant() AND t.product_id=product
 AND t.business_profile_code=public.sf_visible_business_profile() AND (t.store_id=public.sf_store() OR t.store_id IS NULL AND NOT EXISTS(SELECT 1 FROM public.gst_tax_configurations scoped WHERE scoped.tenant_id=t.tenant_id AND scoped.product_id=product AND scoped.store_id=public.sf_store() AND (scoped.variant_id=variant OR scoped.variant_id IS NULL) AND scoped.effective_from<=day))
 AND (t.variant_id=variant OR t.variant_id IS NULL AND NOT EXISTS(SELECT 1 FROM public.gst_tax_configurations overridden WHERE overridden.tenant_id=t.tenant_id AND overridden.product_id=product AND overridden.store_id=public.sf_store() AND overridden.variant_id=variant AND overridden.effective_from<=day)) AND day BETWEEN t.effective_from AND coalesce(t.effective_to,'infinity')
 AND public.sf_can_location(public.sf_location()) AND (public.sf_gst_allowed('VIEW') OR public.sf_gst_allowed('VIEW','SALES') OR public.sf_gst_allowed('CREATE','SALES'))
 ORDER BY (t.variant_id IS NOT NULL) DESC,(t.store_id IS NOT NULL) DESC LIMIT 1
$$;
CREATE FUNCTION public.sf_gst_product_rate(id integer,variant bigint) RETURNS numeric LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE rate numeric; BEGIN
 IF NOT public.sf_gst_allowed('VIEW','SALES') OR NOT public.sf_can_location(public.sf_location()) THEN RAISE EXCEPTION 'Sales permission required' USING ERRCODE='42501'; END IF;
 IF NOT coalesce((public.sf_gst_sale_context()->>'mapped')::boolean,false) THEN RETURN NULL; END IF;
 SELECT t.tax_rate INTO rate FROM public.sf_gst_resolve_tax(id,variant,CURRENT_DATE) t;
 IF NOT FOUND THEN RAISE EXCEPTION 'Review effective GST classification for the selected item' USING ERRCODE='23514'; END IF;
 IF public.sf_gst_sale_context()->>'registrationType' IN('UNREGISTERED','COMPOSITION') THEN RETURN 0; END IF; RETURN rate;
END $$;
CREATE OR REPLACE FUNCTION public.sf_gst_product_rate(id integer) RETURNS numeric LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$ SELECT public.sf_gst_product_rate(id,NULL::bigint) $$;
DO $migration$ DECLARE definition text; old_select text; BEGIN
 definition:=replace(pg_get_functiondef('public.sf_gst_capture_sale(bigint,text)'::regprocedure),E'\r\n',E'\n');
 old_select:='SELECT * INTO tax FROM public.gst_tax_configurations t WHERE t.tenant_id=s.tenant_id AND t.product_id=item.product_id AND t.business_profile_code=s.business_profile_code
   AND s.sale_date::date BETWEEN t.effective_from AND coalesce(t.effective_to,''infinity'');';
 IF position(old_select IN definition)=0 THEN RAISE EXCEPTION 'Unexpected GST capture definition'; END IF;
 definition:=replace(definition,old_select,'SELECT * INTO tax FROM public.sf_gst_resolve_tax(item.product_id,item.variant_id,s.sale_date::date);');
 definition:=replace(definition,'''taxConfigId'',tax.tax_config_id','''taxConfigId'',tax.tax_config_id,''taxRuleId'',tax.rule_id,''variantId'',item.variant_id');
 EXECUTE definition;
END $migration$;
REVOKE ALL ON FUNCTION public.sf_gst_resolve_tax(integer,bigint,date),public.sf_gst_product_rate(integer,bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_gst_resolve_tax(integer,bigint,date),public.sf_gst_product_rate(integer,bigint) TO stockflow_runtime;
DROP POLICY gst_audit_read ON public.gst_audit_history;
CREATE POLICY gst_audit_read ON public.gst_audit_history FOR SELECT USING(tenant_id=public.sf_tenant() AND public.sf_gst_allowed('VIEW')
 AND entity IN('gst_legal_businesses','gst_registrations','gst_shop_mappings','gst_tax_configurations','gst_tax_rules')
 AND (entity NOT IN('gst_tax_configurations','gst_tax_rules') OR new_value->>'business_profile_code'=public.sf_visible_business_profile() AND (new_value->>'store_id' IS NULL OR (new_value->>'store_id')::bigint=public.sf_store())));
COMMIT;
