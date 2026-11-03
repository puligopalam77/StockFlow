-- MANUAL ONLY, after 084. Backup/stop writers; apply entire file as postgres.
-- Existing IDs/identifiers/history are retained. Multi-store legacy masters are
-- quarantined until an explicit owner mapping; no ownership inferred from usage.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as superuser'; END IF;
 IF to_regprocedure('public.sf_eway_allowed(text)') IS NULL THEN RAISE EXCEPTION 'Apply 084 first'; END IF;
END $$;
LOCK TABLE public.stores,public.tenants IN SHARE MODE;
-- Security-definer entry points need the same master boundary as RLS. Preserve
-- their validated calculation/identifier logic and prepend narrowly named guards.
DO $$ DECLARE sig text; guard text; definition text; BEGIN
 FOR sig,guard IN SELECT * FROM (VALUES
 ('public.sf_save_category_configuration(integer,jsonb)', 'IF NOT public.sf_master_access(''CATEGORY'',cat,true) THEN RAISE EXCEPTION ''Category unavailable in this shop'' USING ERRCODE=''42501''; END IF;'),
 ('public.sf_reserve_product_code(integer,uuid,text)', 'IF NOT public.sf_master_access(''CATEGORY'',cat,true) THEN RAISE EXCEPTION ''Category unavailable in this shop'' USING ERRCODE=''42501''; END IF;'),
 ('public.sf_reserve_variant_codes(text,uuid,uuid,integer,integer)', 'IF NOT (CASE WHEN pid IS NOT NULL THEN public.sf_master_access(''PRODUCT'',pid,true) ELSE EXISTS(SELECT 1 FROM public.catalog_identifier_ledger l WHERE l.tenant_id=public.sf_tenant() AND l.code=upper(btrim(parent)) AND public.sf_master_access(''CATEGORY'',l.category_id,true)) END) THEN RAISE EXCEPTION ''Product reservation unavailable in this shop'' USING ERRCODE=''42501''; END IF;'),
 ('public.sf_reserve_unit_identifiers(bigint,uuid,integer)', 'IF NOT EXISTS(SELECT 1 FROM public.product_variants v WHERE v.tenant_id=public.sf_tenant() AND v.variant_id=vid AND public.sf_master_access(''PRODUCT'',v.product_id)) THEN RAISE EXCEPTION ''Unit source unavailable in this shop'' USING ERRCODE=''42501''; END IF;'),
 ('public.sf_gst_product_rate(integer,bigint)', 'IF NOT public.sf_master_access(''PRODUCT'',id) THEN RAISE EXCEPTION ''Tax source unavailable in this shop'' USING ERRCODE=''42501''; END IF;')
 ) v LOOP
  definition:=pg_get_functiondef(sig::regprocedure);
  IF definition !~ '\mBEGIN\M' THEN RAISE EXCEPTION 'Review changed guarded function %',sig; END IF;
  EXECUTE regexp_replace(definition,'\mBEGIN\M','BEGIN '||guard,'i');
 END LOOP;
END $$;
CREATE TEMP TABLE sf085_triggers ON COMMIT DROP AS SELECT tgrelid::regclass relation,tgname,tgenabled FROM pg_trigger
 WHERE NOT tgisinternal AND tgenabled<>'D' AND tgrelid IN('public.categories'::regclass,'public.brands'::regclass,'public.products'::regclass,'public.customers'::regclass,'public.suppliers'::regclass);
DO $$ DECLARE r record; BEGIN FOR r IN SELECT * FROM sf085_triggers LOOP EXECUTE format('ALTER TABLE %s DISABLE TRIGGER %I',r.relation,r.tgname); END LOOP; END $$;
CREATE FUNCTION public.sf_shop_member(s bigint) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.stores st JOIN public.users u ON u.tenant_id=st.tenant_id
 WHERE st.tenant_id=public.sf_tenant() AND st.store_id=s AND st.is_active AND u.user_id=public.sf_actor() AND u.is_active
 AND (u.is_tenant_owner OR EXISTS(SELECT 1 FROM public.user_store_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.store_id=s AND a.is_active)
 OR EXISTS(SELECT 1 FROM public.user_location_access a JOIN public.store_locations l USING(tenant_id,location_id) WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND l.store_id=s AND l.is_active)))
$$;
CREATE TABLE public.master_sharing_events(event_id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,entity text NOT NULL,master_id bigint NOT NULL,
 owner_store_id bigint NOT NULL,target_store_id bigint NOT NULL,action text NOT NULL,reason text NOT NULL,actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,owner_store_id) REFERENCES public.stores(tenant_id,store_id),FOREIGN KEY(tenant_id,target_store_id) REFERENCES public.stores(tenant_id,store_id),
 FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id));
CREATE TRIGGER sharing_history BEFORE UPDATE OR DELETE ON public.master_sharing_events FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_immutable();
DO $$ DECLARE tab text; col text; BEGIN
 FOREACH tab IN ARRAY ARRAY['categories','brands','products','customers','suppliers'] LOOP
  col:=CASE tab WHEN 'categories' THEN 'category_id' WHEN 'brands' THEN 'brand_id' WHEN 'products' THEN 'product_id' WHEN 'customers' THEN 'customer_id' ELSE 'supplier_id' END;
  EXECUTE format('ALTER TABLE public.%I ADD COLUMN owner_store_id bigint REFERENCES public.stores(store_id)',tab);
  EXECUTE format('ALTER TABLE public.%I ADD CONSTRAINT master_owner_tenant FOREIGN KEY(tenant_id,owner_store_id) REFERENCES public.stores(tenant_id,store_id)',tab);
  -- A tenant with exactly one store has no competing shop owner. Include inactive
  -- stores in this count; do not accidentally allocate an inactive shop's history.
  EXECUTE format('UPDATE public.%I m SET owner_store_id=s.store_id FROM (SELECT tenant_id,min(store_id) store_id FROM public.stores GROUP BY tenant_id HAVING count(*)=1) s WHERE m.tenant_id=s.tenant_id',tab);
  EXECUTE format('ALTER TABLE public.%I ALTER COLUMN owner_store_id SET DEFAULT NULLIF(public.sf_store(),0)',tab);
  EXECUTE format('CREATE INDEX ON public.%I(tenant_id,owner_store_id)',tab);
  EXECUTE format('CREATE TABLE public.%I(tenant_id bigint NOT NULL,master_id integer NOT NULL,store_id bigint NOT NULL,PRIMARY KEY(tenant_id,master_id,store_id),FOREIGN KEY(tenant_id,master_id) REFERENCES public.%I(tenant_id,%I),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id))',tab||'_shop_shares',tab,col);
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab||'_shop_shares');
  EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab||'_shop_shares');
 END LOOP;
END $$;
DO $$ DECLARE r record; BEGIN FOR r IN SELECT * FROM sf085_triggers LOOP EXECUTE format('ALTER TABLE %s %s TRIGGER %I',r.relation,CASE r.tgenabled WHEN 'A' THEN 'ENABLE ALWAYS' WHEN 'R' THEN 'ENABLE REPLICA' ELSE 'ENABLE' END,r.tgname); END LOOP; END $$;
CREATE FUNCTION public.sf_master_access(kind text,id bigint,writing boolean DEFAULT false) RETURNS boolean LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE tab text; col text; own bigint; allowed boolean;
BEGIN
 tab:=CASE kind WHEN 'CATEGORY' THEN 'categories' WHEN 'BRAND' THEN 'brands' WHEN 'PRODUCT' THEN 'products' WHEN 'CUSTOMER' THEN 'customers' WHEN 'SUPPLIER' THEN 'suppliers' END;
 col:=CASE kind WHEN 'CATEGORY' THEN 'category_id' WHEN 'BRAND' THEN 'brand_id' WHEN 'PRODUCT' THEN 'product_id' WHEN 'CUSTOMER' THEN 'customer_id' WHEN 'SUPPLIER' THEN 'supplier_id' END;
 IF tab IS NULL OR public.sf_tenant()<=0 OR public.sf_actor()<=0 THEN RETURN false; END IF;
 EXECUTE format('SELECT owner_store_id FROM public.%I WHERE tenant_id=$1 AND %I=$2',tab,col) INTO own USING public.sf_tenant(),id;
 IF own IS NULL THEN RETURN false; END IF;
 IF public.sf_store()=0 THEN RETURN NOT writing AND public.sf_owner(); END IF;
 IF NOT public.sf_shop_member(public.sf_store()) THEN RETURN false; END IF;
 IF own=public.sf_store() THEN RETURN true; END IF;
 IF writing THEN RETURN false; END IF;
 EXECUTE format('SELECT EXISTS(SELECT 1 FROM public.%I WHERE tenant_id=$1 AND master_id=$2 AND store_id=$3)',tab||'_shop_shares') INTO allowed USING public.sf_tenant(),id,public.sf_store();
 RETURN allowed;
END $$;
CREATE FUNCTION public.sf_master_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE doc jsonb:=to_jsonb(NEW); ref bigint; kind text;
BEGIN
 IF TG_OP='INSERT' THEN
  IF (doc->>'tenant_id')::bigint<>public.sf_tenant() OR (doc->>'owner_store_id')::bigint IS DISTINCT FROM public.sf_store() OR NOT public.sf_shop_member(public.sf_store())
   THEN RAISE EXCEPTION 'Select an authorized shop before registering master data' USING ERRCODE='42501'; END IF;
 ELSIF (doc->>'owner_store_id') IS DISTINCT FROM (to_jsonb(OLD)->>'owner_store_id') THEN
  IF (to_jsonb(OLD)->>'owner_store_id') IS NOT NULL OR NOT public.sf_owner() OR (doc->>'owner_store_id')::bigint IS DISTINCT FROM public.sf_store() OR NOT public.sf_shop_member(public.sf_store()) THEN RAISE EXCEPTION 'Ownership reassignment denied' USING ERRCODE='42501'; END IF;
 ELSIF NOT public.sf_master_access(TG_ARGV[0],(doc->>TG_ARGV[1])::bigint,true) THEN RAISE EXCEPTION 'Shared or quarantined masters cannot be edited here' USING ERRCODE='42501'; END IF;
 IF TG_TABLE_NAME='products' THEN
  IF NOT public.sf_master_access('CATEGORY',NEW.category_id,TG_OP='INSERT') OR (NEW.brand_id IS NOT NULL AND NOT public.sf_master_access('BRAND',NEW.brand_id)) THEN RAISE EXCEPTION 'Category or brand is not available in this shop' USING ERRCODE='42501'; END IF;
 ELSIF TG_TABLE_NAME='categories' THEN
  IF doc->>'parent_category_id' IS NOT NULL AND NOT public.sf_master_access('CATEGORY',(doc->>'parent_category_id')::bigint,TG_OP='INSERT') THEN RAISE EXCEPTION 'Parent category is not available' USING ERRCODE='42501'; END IF;
 END IF;
 RETURN NEW;
END $$;
DO $$ DECLARE tab text; kind text; col text; BEGIN
 FOR tab,kind,col IN SELECT * FROM (VALUES('categories','CATEGORY','category_id'),('brands','BRAND','brand_id'),('products','PRODUCT','product_id'),('customers','CUSTOMER','customer_id'),('suppliers','SUPPLIER','supplier_id')) v LOOP
  EXECUTE format('CREATE POLICY shop_master_read ON public.%I AS RESTRICTIVE FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant() AND owner_store_id IS NOT NULL AND ((owner_store_id=public.sf_store() AND public.sf_shop_member(owner_store_id)) OR (public.sf_store()=0 AND public.sf_owner()) OR public.sf_master_access(%L,%I)))',tab,kind,col);
  EXECUTE format('CREATE POLICY shop_master_update ON public.%I AS RESTRICTIVE FOR UPDATE TO stockflow_runtime USING(public.sf_master_access(%L,%I,true)) WITH CHECK(public.sf_master_access(%L,%I,true))',tab,kind,col,kind,col);
  EXECUTE format('CREATE POLICY shop_master_delete ON public.%I AS RESTRICTIVE FOR DELETE TO stockflow_runtime USING(false)',tab);
  EXECUTE format('CREATE TRIGGER zz_private_master BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_master_guard(%L,%L)',tab,kind,col);
 END LOOP;
END $$;
-- Child catalog records inherit product privacy. Read-only sharing cannot change
-- a common price, tax, SKU, barcode, defaults or tracking setting in another shop.
CREATE FUNCTION public.sf_catalog_product(tab text,doc jsonb) RETURNS bigint LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE id bigint; BEGIN
 IF tab IN('product_variants','product_units','gst_tax_configurations') THEN RETURN (doc->>'product_id')::bigint; END IF;
 SELECT product_id INTO id FROM public.product_variants WHERE tenant_id=public.sf_tenant() AND variant_id=(doc->>'variant_id')::bigint;
 RETURN id;
END $$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['product_variants','variant_barcodes','variant_location_settings'] LOOP
  EXECUTE format('CREATE POLICY shop_catalog_read ON public.%I AS RESTRICTIVE FOR SELECT TO stockflow_runtime USING(public.sf_master_access(''PRODUCT'',public.sf_catalog_product(%L,to_jsonb(%I))))',tab,tab,tab);
  EXECUTE format('CREATE POLICY shop_catalog_write ON public.%I AS RESTRICTIVE FOR UPDATE TO stockflow_runtime USING(public.sf_master_access(''PRODUCT'',public.sf_catalog_product(%L,to_jsonb(%I)),true)) WITH CHECK(public.sf_master_access(''PRODUCT'',public.sf_catalog_product(%L,to_jsonb(%I)),true))',tab,tab,tab,tab,tab);
  EXECUTE format('CREATE POLICY shop_catalog_insert ON public.%I AS RESTRICTIVE FOR INSERT TO stockflow_runtime WITH CHECK(public.sf_master_access(''PRODUCT'',public.sf_catalog_product(%L,to_jsonb(%I)),true))',tab,tab,tab);
  EXECUTE format('CREATE POLICY shop_catalog_delete ON public.%I AS RESTRICTIVE FOR DELETE TO stockflow_runtime USING(public.sf_master_access(''PRODUCT'',public.sf_catalog_product(%L,to_jsonb(%I)),true))',tab,tab,tab);
 END LOOP;
END $$;
-- Definer writers must also enforce shop authority; FK checks alone bypass RLS.
CREATE FUNCTION public.sf_shop_reference_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE doc jsonb:=to_jsonb(NEW); product bigint; BEGIN
 IF TG_OP='UPDATE' AND TG_TABLE_NAME IN('sale_items','purchase_items') THEN RETURN NEW; END IF;
 IF doc->>'product_id' IS NOT NULL AND NOT public.sf_master_access('PRODUCT',(doc->>'product_id')::bigint) THEN RAISE EXCEPTION 'Product unavailable in this shop' USING ERRCODE='42501'; END IF;
 IF doc->>'customer_id' IS NOT NULL AND NOT public.sf_master_access('CUSTOMER',(doc->>'customer_id')::bigint) THEN RAISE EXCEPTION 'Customer unavailable in this shop' USING ERRCODE='42501'; END IF;
 IF doc->>'supplier_id' IS NOT NULL AND NOT public.sf_master_access('SUPPLIER',(doc->>'supplier_id')::bigint) THEN RAISE EXCEPTION 'Supplier unavailable in this shop' USING ERRCODE='42501'; END IF;
 IF doc->>'variant_id' IS NOT NULL AND NOT public.sf_master_access('PRODUCT',public.sf_catalog_product('variant_barcodes',doc)) THEN RAISE EXCEPTION 'Variant unavailable in this shop' USING ERRCODE='42501'; END IF;
 IF TG_TABLE_NAME='supplier_product_catalog' AND NOT public.sf_master_access('SUPPLIER',(doc->>'supplier_id')::bigint,true) THEN RAISE EXCEPTION 'Shared supplier catalog is read only' USING ERRCODE='42501'; END IF;
 RETURN NEW;
END $$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['sales','sale_items','purchases','purchase_items','stock_intake_items','service_requests','supplier_product_catalog','inventory_transactions','product_units','stock_transfer_items','stock_count_items','service_parts'] LOOP
  EXECUTE format('CREATE TRIGGER zz_shop_reference BEFORE INSERT ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_shop_reference_guard()',tab);
 END LOOP;
END $$;
CREATE FUNCTION public.sf_share_master(kind text,id bigint,target bigint,enabled boolean,reason text) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE tab text; col text; own bigint; p public.products; parent bigint;
BEGIN
 IF NOT public.sf_owner() OR NOT public.sf_transfer_permission('COMPANY_SETTINGS','EDIT') OR NOT public.sf_shop_member(target) OR target=public.sf_store() OR length(btrim(coalesce(reason,''))) NOT BETWEEN 10 AND 1000 OR NOT public.sf_master_access(kind,id,true) THEN RAISE EXCEPTION 'Owner authorization, source shop and a sharing reason are required' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 tab:=CASE kind WHEN 'CATEGORY' THEN 'categories' WHEN 'BRAND' THEN 'brands' WHEN 'PRODUCT' THEN 'products' WHEN 'CUSTOMER' THEN 'customers' WHEN 'SUPPLIER' THEN 'suppliers' END;
 col:=CASE kind WHEN 'CATEGORY' THEN 'category_id' WHEN 'BRAND' THEN 'brand_id' WHEN 'PRODUCT' THEN 'product_id' WHEN 'CUSTOMER' THEN 'customer_id' WHEN 'SUPPLIER' THEN 'supplier_id' END;
 IF NOT enabled AND kind IN('CATEGORY','BRAND') AND EXISTS(SELECT 1 FROM public.products dependency WHERE dependency.tenant_id=public.sf_tenant() AND (CASE kind WHEN 'CATEGORY' THEN dependency.category_id ELSE dependency.brand_id END)=id AND (dependency.owner_store_id=target OR EXISTS(SELECT 1 FROM public.products_shop_shares s WHERE s.tenant_id=dependency.tenant_id AND s.master_id=dependency.product_id AND s.store_id=target))) THEN RAISE EXCEPTION 'Dependent products still need this category or brand; review dependencies before revoking' USING ERRCODE='23514'; END IF;
 IF enabled AND kind='PRODUCT' THEN
  SELECT * INTO p FROM public.products WHERE tenant_id=public.sf_tenant() AND product_id=id;
  IF NOT EXISTS(SELECT 1 FROM public.categories c WHERE c.tenant_id=p.tenant_id AND c.category_id=p.category_id AND c.owner_store_id=target) AND NOT EXISTS(SELECT 1 FROM public.categories_shop_shares WHERE tenant_id=p.tenant_id AND master_id=p.category_id AND store_id=target)
  THEN RAISE EXCEPTION 'Explicitly share the category first' USING ERRCODE='23514'; END IF;
  IF p.brand_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.brands b WHERE b.tenant_id=p.tenant_id AND b.brand_id=p.brand_id AND b.owner_store_id=target) AND NOT EXISTS(SELECT 1 FROM public.brands_shop_shares WHERE tenant_id=p.tenant_id AND master_id=p.brand_id AND store_id=target)
  THEN RAISE EXCEPTION 'Explicitly share the brand first' USING ERRCODE='23514'; END IF;
 END IF;
 IF enabled THEN EXECUTE format('INSERT INTO public.%I VALUES($1,$2,$3) ON CONFLICT DO NOTHING',tab||'_shop_shares') USING public.sf_tenant(),id,target;
 ELSE EXECUTE format('DELETE FROM public.%I WHERE tenant_id=$1 AND master_id=$2 AND store_id=$3',tab||'_shop_shares') USING public.sf_tenant(),id,target; END IF;
 INSERT INTO public.master_sharing_events(tenant_id,entity,master_id,owner_store_id,target_store_id,action,reason,actor_id) VALUES(public.sf_tenant(),kind,id,public.sf_store(),target,CASE WHEN enabled THEN 'SHARE' ELSE 'REVOKE' END,btrim(reason),public.sf_actor());
END $$;
-- Owner-reviewed quarantine resolution is additive, never clones or renumbers IDs.
CREATE FUNCTION public.sf_resolve_master(kind text,id bigint,target bigint,reason text) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE tab text; col text; own bigint; affected integer; BEGIN
 IF NOT public.sf_owner() OR NOT public.sf_transfer_permission('COMPANY_SETTINGS','EDIT') OR NOT public.sf_shop_member(target) OR target<>public.sf_store() OR length(btrim(coalesce(reason,''))) NOT BETWEEN 10 AND 1000 THEN RAISE EXCEPTION 'Owner mapping authorization required' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 tab:=CASE kind WHEN 'CATEGORY' THEN 'categories' WHEN 'BRAND' THEN 'brands' WHEN 'PRODUCT' THEN 'products' WHEN 'CUSTOMER' THEN 'customers' WHEN 'SUPPLIER' THEN 'suppliers' END;
 col:=CASE kind WHEN 'CATEGORY' THEN 'category_id' WHEN 'BRAND' THEN 'brand_id' WHEN 'PRODUCT' THEN 'product_id' WHEN 'CUSTOMER' THEN 'customer_id' WHEN 'SUPPLIER' THEN 'supplier_id' END;
 IF tab IS NULL THEN RAISE EXCEPTION 'Invalid master kind'; END IF;
 EXECUTE format('SELECT owner_store_id FROM public.%I WHERE tenant_id=$1 AND %I=$2 FOR UPDATE',tab,col) INTO own USING public.sf_tenant(),id;
 IF own IS NOT NULL THEN RAISE EXCEPTION 'Already allocated; reassignment requires a separate reviewed migration' USING ERRCODE='23514'; END IF;
 -- Runtime has no RLS UPDATE access to quarantined rows. The trigger permits
 -- this owner-only null-to-owner transition without disabling any protection.
 EXECUTE format('UPDATE public.%I SET owner_store_id=$1 WHERE tenant_id=$2 AND %I=$3 AND owner_store_id IS NULL',tab,col) USING target,public.sf_tenant(),id;
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>1 THEN RAISE EXCEPTION 'Quarantined record not found' USING ERRCODE='23514'; END IF;
 INSERT INTO public.master_sharing_events(tenant_id,entity,master_id,owner_store_id,target_store_id,action,reason,actor_id) VALUES(public.sf_tenant(),kind,id,target,target,'RESOLVE',btrim(reason),public.sf_actor());
END $$;
ALTER TABLE public.master_sharing_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.master_sharing_events FORCE ROW LEVEL SECURITY;
CREATE POLICY sharing_audit ON public.master_sharing_events FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant() AND public.sf_owner() AND (public.sf_store()=0 OR owner_store_id=public.sf_store() OR target_store_id=public.sf_store()));
GRANT SELECT ON public.master_sharing_events TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_shop_member(bigint),public.sf_master_access(text,bigint,boolean),public.sf_catalog_product(text,jsonb),public.sf_master_guard(),public.sf_shop_reference_guard(),public.sf_share_master(text,bigint,bigint,boolean,text),public.sf_resolve_master(text,bigint,bigint,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_shop_member(bigint),public.sf_master_access(text,bigint,boolean),public.sf_catalog_product(text,jsonb),public.sf_share_master(text,bigint,bigint,boolean,text),public.sf_resolve_master(text,bigint,bigint,text) TO stockflow_runtime;
DROP INDEX public.ux_brands_name;
CREATE UNIQUE INDEX ux_brands_name ON public.brands(tenant_id,owner_store_id,business_profile_code,lower(btrim(brand_name)));
DROP INDEX public.ux_category_root_name;
DROP INDEX public.ux_category_child_name;
CREATE UNIQUE INDEX ux_category_root_name ON public.categories(tenant_id,owner_store_id,business_profile_code,public.sf_category_name_key(category_name)) WHERE parent_category_id IS NULL;
CREATE UNIQUE INDEX ux_category_child_name ON public.categories(tenant_id,owner_store_id,business_profile_code,parent_category_id,public.sf_category_name_key(category_name)) WHERE parent_category_id IS NOT NULL;
DO $$ DECLARE sig text; definition text; BEGIN
 FOREACH sig IN ARRAY ARRAY['public.sf_brand_alias_guard()','public.sf_brand_registration_guard()','public.sf_duplicate_product_guard()'] LOOP
  definition:=pg_get_functiondef(sig::regprocedure);
  definition:=replace(definition,'b.tenant_id=NEW.tenant_id AND','b.tenant_id=NEW.tenant_id AND b.owner_store_id IS NOT DISTINCT FROM NEW.owner_store_id AND');
  definition:=replace(definition,'p.tenant_id=NEW.tenant_id AND p.product_id<>','p.tenant_id=NEW.tenant_id AND p.owner_store_id IS NOT DISTINCT FROM NEW.owner_store_id AND p.product_id<>');
  EXECUTE definition;
 END LOOP;
END $$;
CREATE FUNCTION public.sf_shop_historical_profile(tab text,doc jsonb) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT (doc->>'tenant_id')::bigint=public.sf_tenant() AND public.sf_record_business_profile(tab,doc)=public.sf_visible_business_profile()
$$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['inventory_transactions','product_units','service_requests'] LOOP
  EXECUTE format('DROP POLICY selected_business_profile ON public.%I',tab);
  EXECUTE format('CREATE POLICY selected_business_profile ON public.%I AS RESTRICTIVE FOR ALL TO stockflow_runtime USING(public.sf_shop_historical_profile(%L,to_jsonb(%I))) WITH CHECK(public.sf_shop_historical_profile(%L,to_jsonb(%I)))',tab,tab,tab,tab,tab);
 END LOOP;
END $$;
CREATE FUNCTION public.sf_catalog_shop_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE doc jsonb:=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END; BEGIN
 IF TG_TABLE_NAME='brand_categories' THEN
  IF NOT public.sf_master_access('BRAND',(doc->>'brand_id')::bigint,true) OR NOT public.sf_master_access('CATEGORY',(doc->>'category_id')::bigint) THEN RAISE EXCEPTION 'Brand/category mapping unavailable' USING ERRCODE='42501'; END IF;
 ELSIF NOT public.sf_master_access('PRODUCT',public.sf_catalog_product(TG_TABLE_NAME,doc),true) THEN RAISE EXCEPTION 'Shared product settings are read only' USING ERRCODE='42501'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
END $$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['product_variants','variant_barcodes','variant_location_settings','brand_categories','gst_tax_configurations'] LOOP
  EXECUTE format('CREATE TRIGGER zz_catalog_shop BEFORE INSERT OR UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_catalog_shop_guard()',tab);
 END LOOP;
END $$;
CREATE POLICY supplier_catalog_shop ON public.supplier_product_catalog AS RESTRICTIVE FOR ALL TO stockflow_runtime USING(public.sf_master_access('SUPPLIER',supplier_id) AND (category_id IS NULL OR public.sf_master_access('CATEGORY',category_id)) AND (brand_id IS NULL OR public.sf_master_access('BRAND',brand_id))) WITH CHECK(public.sf_master_access('SUPPLIER',supplier_id,true) AND (category_id IS NULL OR public.sf_master_access('CATEGORY',category_id)) AND (brand_id IS NULL OR public.sf_master_access('BRAND',brand_id)));
CREATE POLICY brand_mapping_shop ON public.brand_categories AS RESTRICTIVE FOR SELECT TO stockflow_runtime USING(public.sf_master_access('BRAND',brand_id) AND public.sf_master_access('CATEGORY',category_id));
CREATE POLICY tax_master_shop ON public.gst_tax_configurations AS RESTRICTIVE FOR SELECT TO stockflow_runtime USING(public.sf_master_access('PRODUCT',product_id));
CREATE FUNCTION public.sf_contact_shop_guard() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE doc jsonb:=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END; BEGIN
 IF NOT public.sf_master_access('CUSTOMER',(doc->>'customer_id')::bigint,true) THEN RAISE EXCEPTION 'Shared contact configuration is read only' USING ERRCODE='42501'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;RETURN NEW;
END $$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['customer_credit_limits','notification_customer_consents'] LOOP
  EXECUTE format('CREATE TRIGGER zz_contact_shop BEFORE INSERT OR UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_contact_shop_guard()',tab);
 END LOOP;
END $$;
REVOKE ALL ON FUNCTION public.sf_contact_shop_guard() FROM PUBLIC;
CREATE POLICY identifier_shop ON public.catalog_identifier_ledger AS RESTRICTIVE FOR SELECT TO stockflow_runtime USING(public.sf_master_access('PRODUCT',product_id) OR (product_id IS NULL AND public.sf_master_access('CATEGORY',category_id,true)));
CREATE POLICY prefix_shop ON public.catalog_prefix_registry AS RESTRICTIVE FOR SELECT TO stockflow_runtime USING(public.sf_master_access('CATEGORY',category_id));
CREATE FUNCTION public.sf_shop_master_review() RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE result jsonb; BEGIN
 IF NOT public.sf_owner() OR NOT public.sf_shop_member(public.sf_store()) OR NOT public.sf_transfer_permission('COMPANY_SETTINGS','EDIT') THEN RAISE EXCEPTION 'Owner review permission required' USING ERRCODE='42501'; END IF;
 WITH masters AS(
  SELECT tenant_id,'CATEGORY' kind,category_id id,category_name name,owner_store_id,business_profile_code profile FROM public.categories
  UNION ALL SELECT tenant_id,'BRAND',brand_id,brand_name,owner_store_id,business_profile_code FROM public.brands
  UNION ALL SELECT tenant_id,'PRODUCT',product_id,product_name,owner_store_id,identifier_profile FROM public.products
  UNION ALL SELECT tenant_id,'CUSTOMER',customer_id,customer_name,owner_store_id,business_profile_code FROM public.customers
  UNION ALL SELECT tenant_id,'SUPPLIER',supplier_id,supplier_name,owner_store_id,business_profile_code FROM public.suppliers
 ) SELECT jsonb_build_object('masters',(SELECT coalesce(jsonb_agg(to_jsonb(m) ORDER BY kind,id),'[]') FROM masters m WHERE tenant_id=public.sf_tenant() AND (owner_store_id IS NULL OR owner_store_id=public.sf_store())),
 'stores',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',store_id,'name',store_name)),'[]') FROM public.stores WHERE tenant_id=public.sf_tenant() AND is_active),
 'events',(SELECT coalesce(jsonb_agg(to_jsonb(x) ORDER BY event_id DESC),'[]') FROM (SELECT * FROM public.master_sharing_events WHERE tenant_id=public.sf_tenant() AND (owner_store_id=public.sf_store() OR target_store_id=public.sf_store()) ORDER BY event_id DESC LIMIT 100) x),
 'shares',(SELECT coalesce(jsonb_agg(to_jsonb(s)),'[]') FROM(SELECT 'CATEGORY' kind,master_id,store_id FROM public.categories_shop_shares WHERE tenant_id=public.sf_tenant() UNION ALL SELECT 'BRAND',master_id,store_id FROM public.brands_shop_shares WHERE tenant_id=public.sf_tenant() UNION ALL SELECT 'PRODUCT',master_id,store_id FROM public.products_shop_shares WHERE tenant_id=public.sf_tenant() UNION ALL SELECT 'CUSTOMER',master_id,store_id FROM public.customers_shop_shares WHERE tenant_id=public.sf_tenant() UNION ALL SELECT 'SUPPLIER',master_id,store_id FROM public.suppliers_shop_shares WHERE tenant_id=public.sf_tenant()) s JOIN masters m ON m.kind=s.kind AND m.id=s.master_id WHERE m.tenant_id=public.sf_tenant() AND m.owner_store_id=public.sf_store())) INTO result;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION public.sf_shop_historical_profile(text,jsonb),public.sf_catalog_shop_guard(),public.sf_shop_master_review() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_shop_historical_profile(text,jsonb),public.sf_shop_master_review() TO stockflow_runtime;
DO $$ DECLARE definition text; BEGIN
 definition:=pg_get_functiondef('public.sf_gst_resolve_tax(integer,bigint,date)'::regprocedure);
 IF strpos(definition,'t.tenant_id=public.sf_tenant()')=0 THEN RAISE EXCEPTION 'Review changed GST resolver before applying'; END IF;
 EXECUTE replace(definition,'t.tenant_id=public.sf_tenant()','t.tenant_id=public.sf_tenant() AND public.sf_master_access(''PRODUCT'',product)');
 definition:=pg_get_functiondef('public.sf_catalog_locked_categories()'::regprocedure);
 IF strpos(definition,'SELECT DISTINCT category_id FROM locked')=0 THEN RAISE EXCEPTION 'Review changed category lock reader'; END IF;
 EXECUTE replace(definition,'SELECT DISTINCT category_id FROM locked','SELECT DISTINCT category_id FROM locked WHERE public.sf_master_access(''CATEGORY'',category_id)');
END $$;
COMMIT;
