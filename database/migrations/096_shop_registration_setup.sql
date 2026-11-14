-- MANUAL ONLY after 095. Back up, stop writers, run the ENTIRE file as postgres.
-- No backfill or financial/stock updates. Existing shops keep their legacy policy.
-- New shops use existing management_details and existing GST mapping/audit tables.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_expense_gst(jsonb,numeric,date,jsonb)') IS NULL
  OR to_regprocedure('public.sf_store_unused(bigint)') IS NULL THEN RAISE EXCEPTION 'Apply existing migrations through 095 first'; END IF;
END $$;

-- State names match the existing GstRules dropdown. No inferred PIN geography.
CREATE FUNCTION public.sf_shop_state_code(value text) RETURNS text
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public AS $$
 SELECT code FROM (VALUES
 ('01','Jammu and Kashmir'),('02','Himachal Pradesh'),('03','Punjab'),('04','Chandigarh'),
 ('05','Uttarakhand'),('06','Haryana'),('07','Delhi'),('08','Rajasthan'),('09','Uttar Pradesh'),
 ('10','Bihar'),('11','Sikkim'),('12','Arunachal Pradesh'),('13','Nagaland'),('14','Manipur'),
 ('15','Mizoram'),('16','Tripura'),('17','Meghalaya'),('18','Assam'),('19','West Bengal'),
 ('20','Jharkhand'),('21','Odisha'),('22','Chhattisgarh'),('23','Madhya Pradesh'),('24','Gujarat'),
 ('26','Dadra and Nagar Haveli and Daman and Diu'),('27','Maharashtra'),('29','Karnataka'),
 ('30','Goa'),('31','Lakshadweep'),('32','Kerala'),('33','Tamil Nadu'),('34','Puducherry'),
 ('35','Andaman and Nicobar Islands'),('36','Telangana'),('37','Andhra Pradesh'),('38','Ladakh')
 ) s(code,name) WHERE lower(btrim(value))=lower(name)
$$;

CREATE FUNCTION public.sf_shop_setup_status(id bigint,profile text,day date) RETURNS text
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE s public.stores; registration public.gst_registrations; n integer;
BEGIN
 SELECT * INTO s FROM public.stores WHERE tenant_id=public.sf_tenant() AND store_id=id;
 IF NOT FOUND OR NOT (public.sf_owner() OR EXISTS(SELECT 1 FROM public.store_locations l
  WHERE l.tenant_id=s.tenant_id AND l.store_id=id AND public.sf_can_location(l.location_id))) THEN
  RAISE EXCEPTION 'Shop is outside authorized access' USING ERRCODE='42501'; END IF;
 IF NOT coalesce((s.management_details->>'ShopSetupRequired')::boolean,false) THEN RETURN 'Legacy setup - review in GST Settings'; END IF;
 IF NOT s.is_active THEN RETURN 'Inactive'; END IF;
 IF nullif(btrim(s.management_details->>'AddressLine1'),'') IS NULL OR nullif(btrim(s.city),'') IS NULL
  OR public.sf_shop_state_code(s.state) IS NULL OR coalesce(s.management_details->>'PinCode','') !~ '^[1-9][0-9]{5}$' THEN RETURN 'Address incomplete'; END IF;
 IF nullif(s.management_details->>'LegalBusinessId','') IS NULL THEN RETURN 'Legal business not selected'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.store_locations WHERE tenant_id=s.tenant_id AND store_id=id AND is_active AND location_type='SALES_FLOOR') THEN RETURN 'Sales location missing'; END IF;
 SELECT count(*) INTO n FROM public.gst_shop_mappings m JOIN public.gst_registrations r USING(tenant_id,registration_id)
 WHERE m.tenant_id=s.tenant_id AND m.store_id=id AND m.business_profile_code=profile AND m.status='ACTIVE' AND r.status='ACTIVE'
 AND day BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') AND day BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity');
 IF n<>1 THEN RETURN 'GST setup pending'; END IF;
 SELECT r.* INTO registration FROM public.gst_shop_mappings m JOIN public.gst_registrations r USING(tenant_id,registration_id)
 WHERE m.tenant_id=s.tenant_id AND m.store_id=id AND m.business_profile_code=profile AND m.status='ACTIVE' AND r.status='ACTIVE'
 AND day BETWEEN m.effective_from AND coalesce(m.effective_to,'infinity') AND day BETWEEN r.effective_from AND coalesce(r.effective_to,'infinity');
 IF registration.business_id<>(s.management_details->>'LegalBusinessId')::bigint OR registration.state_code IS DISTINCT FROM public.sf_shop_state_code(s.state) THEN RETURN 'GST identity/state mismatch'; END IF;
 IF registration.registration_type='OTHER' THEN RETURN 'Unsupported GST scheme'; END IF;
 IF registration.registration_type='UNREGISTERED' THEN RETURN 'Non-GST shop setup complete'; END IF;
 RETURN 'Shop setup complete';
END $$;

CREATE FUNCTION public.sf_shop_registration_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE business bigint; code text;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('gst-config:'||NEW.tenant_id::text,0));
 -- Sticky marker: older clients cannot accidentally disable the new-shop policy.
 IF TG_OP='INSERT' THEN NEW.management_details:=NEW.management_details||'{"ShopSetupRequired":true}'::jsonb;
 ELSIF coalesce((OLD.management_details->>'ShopSetupRequired')::boolean,false) THEN
  NEW.management_details:=NEW.management_details||'{"ShopSetupRequired":true}'::jsonb;
 END IF;
 IF NOT coalesce((NEW.management_details->>'ShopSetupRequired')::boolean,false) THEN RETURN NEW; END IF;
 business:=nullif(NEW.management_details->>'LegalBusinessId','')::bigint; code:=public.sf_shop_state_code(NEW.state);
 IF business IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.gst_legal_businesses WHERE tenant_id=NEW.tenant_id AND business_id=business) THEN
  RAISE EXCEPTION 'Choose a legal business in this tenant from GST Settings' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM public.gst_shop_mappings m JOIN public.gst_registrations r USING(tenant_id,registration_id)
  WHERE m.tenant_id=NEW.tenant_id AND m.store_id=NEW.store_id AND m.status='ACTIVE'
  AND (r.business_id IS DISTINCT FROM business OR r.state_code IS DISTINCT FROM code)) THEN
  RAISE EXCEPTION 'Shop legal business/state must agree with its active GST mappings; review effective mappings in GST Settings' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER zz_shop_registration_guard BEFORE INSERT OR UPDATE ON public.stores
 FOR EACH ROW EXECUTE FUNCTION public.sf_shop_registration_guard();

CREATE FUNCTION public.sf_shop_gst_mapping_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE s public.stores; r public.gst_registrations;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('gst-config:'||NEW.tenant_id::text,0));
 IF NEW.status<>'ACTIVE' THEN RETURN NEW; END IF;
 SELECT * INTO s FROM public.stores WHERE tenant_id=NEW.tenant_id AND store_id=NEW.store_id;
 IF NOT coalesce((s.management_details->>'ShopSetupRequired')::boolean,false) THEN RETURN NEW; END IF;
 SELECT * INTO STRICT r FROM public.gst_registrations WHERE tenant_id=NEW.tenant_id AND registration_id=NEW.registration_id;
 IF nullif(s.management_details->>'LegalBusinessId','')::bigint IS DISTINCT FROM r.business_id
  OR public.sf_shop_state_code(s.state) IS DISTINCT FROM r.state_code THEN
  RAISE EXCEPTION 'Select the shop legal business and matching state before assigning this GST registration' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER zz_shop_gst_mapping_guard BEFORE INSERT OR UPDATE ON public.gst_shop_mappings
 FOR EACH ROW EXECUTE FUNCTION public.sf_shop_gst_mapping_guard();

-- Backstop at sale finalization, including manual POS lines. Unregistered mappings
-- use the existing non-GST calculator; pending setup never falls back to legacy tax.
CREATE FUNCTION public.sf_shop_sale_setup_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE issue text;
BEGIN
 IF NEW.status<>'COMPLETED' THEN RETURN NEW; END IF;
 IF TG_OP='UPDATE' THEN IF OLD.status='COMPLETED' THEN RETURN NEW; END IF; END IF;
 PERFORM pg_advisory_xact_lock_shared(hashtextextended('gst-config:'||NEW.tenant_id::text,0));
 PERFORM 1 FROM public.stores WHERE tenant_id=NEW.tenant_id AND store_id=NEW.store_id FOR SHARE;
 IF NEW.tenant_id<>public.sf_tenant() THEN RAISE EXCEPTION 'Shop is outside this tenant' USING ERRCODE='42501'; END IF;
 issue:=public.sf_shop_setup_status(NEW.store_id,NEW.business_profile_code,NEW.sale_date::date);
 IF issue NOT IN('Shop setup complete','Non-GST shop setup complete','Legacy setup - review in GST Settings') THEN
  RAISE EXCEPTION 'Complete shop setup before billing: %',issue USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER zz_shop_sale_setup_guard BEFORE INSERT OR UPDATE OF status ON public.sales
 FOR EACH ROW EXECUTE FUNCTION public.sf_shop_sale_setup_guard();
REVOKE ALL ON FUNCTION public.sf_shop_state_code(text),public.sf_shop_setup_status(bigint,text,date),public.sf_shop_registration_guard(),public.sf_shop_gst_mapping_guard(),public.sf_shop_sale_setup_guard() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_shop_state_code(text),public.sf_shop_setup_status(bigint,text,date) TO stockflow_runtime;
COMMIT;
-- Keep these additive guards on rollback. Review forward fixes; do not remove
-- setup enforcement after new shops start using it. No history is reconstructed.
