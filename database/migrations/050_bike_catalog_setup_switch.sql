-- MANUAL ONLY. Apply after 049 as PostgreSQL superuser with a backup and writers stopped.
-- This enables Electrical/Bike catalog switching. It does not switch any tenant,
-- reclassify products, create sample stock, change balances or modify history.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Apply 050 as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_snapshot_retail_variant()') IS NULL THEN RAISE EXCEPTION 'Apply 049 first'; END IF;
END $$;
ALTER TABLE public.tenants ADD COLUMN IF NOT EXISTS profile_catalog_switching_enabled boolean NOT NULL DEFAULT true;

CREATE OR REPLACE FUNCTION public.sf_seed_bike_catalog(tid bigint) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE category_value text; defaults jsonb; mappings jsonb; fields_value jsonb; cat record; current_doc jsonb;
BEGIN
 FOREACH category_value IN ARRAY ARRAY['Helmets','Bike Spare Parts','Side Guards','Head Lights','Mirrors','Brake Parts','Chains & Sprockets','Gloves','Bike Covers','Bike Luggage'] LOOP
  INSERT INTO public.categories(tenant_id,category_name,business_profile_code,is_active)
   VALUES(tid,category_value,'BIKE_ACCESSORIES',true) ON CONFLICT DO NOTHING;
 END LOOP;
 -- Never replace an adopted/custom schema or add required fields to existing products.
 SELECT definition INTO current_doc FROM public.catalog_attribute_schemas WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES';
 IF current_doc IS NOT NULL AND current_doc->'attributes'<>'[]'::jsonb THEN RETURN; END IF;
 IF EXISTS(SELECT 1 FROM public.products p JOIN public.categories c ON c.category_id=p.category_id AND c.tenant_id=p.tenant_id
  WHERE p.tenant_id=tid AND c.business_profile_code='BIKE_ACCESSORIES') THEN RETURN; END IF;
 defaults:='[{"minimum":null,"maximum":null,"code":"helmet_size","label":"Helmet / glove size","type":"SELECT","options":["XS","S","M","L","XL","XXL"]},{"minimum":null,"maximum":null,"code":"color","label":"Color","type":"SELECT","options":["Black","White","Red","Blue","Grey","Other"]},{"minimum":null,"maximum":null,"code":"vehicle_make","label":"Bike make","type":"TEXT","options":[]},{"minimum":null,"maximum":null,"code":"compatible_models","label":"Compatible bike models","type":"TEXT","options":[]},{"minimum":null,"maximum":null,"code":"model_years","label":"Model / year range","type":"TEXT","options":[]},{"minimum":null,"maximum":null,"code":"fitting","label":"Fitting / side","type":"SELECT","options":["Left","Right","Pair","Universal"]},{"minimum":0,"maximum":100,"code":"voltage","label":"Voltage (V)","type":"NUMBER","options":[]},{"minimum":0,"maximum":1000,"code":"power_w","label":"Power (W)","type":"NUMBER","options":[]},{"minimum":null,"maximum":null,"code":"connector","label":"Connector / socket type","type":"TEXT","options":[]},{"minimum":null,"maximum":null,"code":"material","label":"Material / finish","type":"TEXT","options":[]}]'::jsonb;
 mappings:='[]'::jsonb;
 FOR cat IN SELECT category_id,category_name FROM public.categories WHERE tenant_id=tid AND business_profile_code='BIKE_ACCESSORIES' AND is_active LOOP
  fields_value:=CASE cat.category_name
   WHEN 'Helmets' THEN '[{"code":"helmet_size","required":true,"variantAxis":true},{"code":"color","required":true,"variantAxis":true},{"code":"material","required":false,"variantAxis":false}]'::jsonb
   WHEN 'Gloves' THEN '[{"code":"helmet_size","required":true,"variantAxis":true},{"code":"color","required":true,"variantAxis":true},{"code":"material","required":false,"variantAxis":false}]'::jsonb
   WHEN 'Head Lights' THEN '[{"code":"vehicle_make","required":false,"variantAxis":false},{"code":"compatible_models","required":true,"variantAxis":false},{"code":"model_years","required":false,"variantAxis":false},{"code":"voltage","required":false,"variantAxis":false},{"code":"power_w","required":false,"variantAxis":false},{"code":"connector","required":false,"variantAxis":false}]'::jsonb
   WHEN 'Side Guards' THEN '[{"code":"vehicle_make","required":false,"variantAxis":false},{"code":"compatible_models","required":true,"variantAxis":false},{"code":"fitting","required":true,"variantAxis":true},{"code":"material","required":false,"variantAxis":false}]'::jsonb
   WHEN 'Mirrors' THEN '[{"code":"vehicle_make","required":false,"variantAxis":false},{"code":"compatible_models","required":true,"variantAxis":false},{"code":"fitting","required":true,"variantAxis":true}]'::jsonb
   ELSE '[{"code":"vehicle_make","required":false,"variantAxis":false},{"code":"compatible_models","required":false,"variantAxis":false},{"code":"model_years","required":false,"variantAxis":false},{"code":"material","required":false,"variantAxis":false}]'::jsonb END;
  mappings:=mappings||jsonb_build_array(jsonb_build_object('categoryId',cat.category_id,'fields',fields_value));
 END LOOP;
 INSERT INTO public.catalog_attribute_schemas(tenant_id,business_profile_code,version,definition)
 VALUES(tid,'BIKE_ACCESSORIES',1,jsonb_build_object('attributes',defaults,'categories',mappings))
 ON CONFLICT(tenant_id,business_profile_code) DO UPDATE SET version=catalog_attribute_schemas.version+1,definition=EXCLUDED.definition,updated_at=now();
 INSERT INTO public.audit_logs(tenant_id,user_id,location_id,module_name,action_name,table_name,record_id,old_values,new_values)
 VALUES(tid,public.sf_actor(),NULL,'Bike setup','CONFIGURE','catalog_attribute_schemas',tid,current_doc,jsonb_build_object('attributes',defaults,'categories',mappings));
END $$;
REVOKE ALL ON FUNCTION public.sf_seed_bike_catalog(bigint) FROM PUBLIC;

CREATE OR REPLACE FUNCTION public.sf_change_business_profile(target_code text, expected_version bigint, change_reason text, create_starters boolean)
RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tenant bigint:=public.sf_tenant(); actor integer:=public.sf_actor(); previous_code text;
    previous_version bigint; category_name_value text; starter_names text[];
BEGIN
    IF tenant<=0 OR actor<=0 OR NOT EXISTS(
        SELECT 1 FROM public.users u JOIN public.roles r ON r.role_id=u.role_id AND r.tenant_id=u.tenant_id
        JOIN public.tenants t ON t.tenant_id=u.tenant_id
        WHERE u.user_id=actor AND u.tenant_id=tenant AND u.is_active AND r.is_active AND t.is_active
          AND (u.is_tenant_owner OR lower(btrim(r.role_name))='admin')
    ) OR (SELECT count(DISTINCT ma.action_code) FROM public.user_action_permissions p
        JOIN public.module_actions ma ON ma.module_action_id=p.module_action_id
        JOIN public.modules m ON m.module_id=ma.module_id
        WHERE p.user_id=actor AND p.tenant_id=tenant AND p.is_allowed AND ma.is_active AND m.is_active
          AND m.module_code='COMPANY_SETTINGS' AND ma.action_code IN('VIEW','EDIT'))<>2 THEN
        RAISE EXCEPTION 'Business profile change requires an authorized owner or administrator' USING ERRCODE='42501';
    END IF;
    IF target_code IS NULL OR target_code NOT IN('ELECTRICAL','BIKE_ACCESSORIES','CLOTHING','FOOTWEAR')
       OR change_reason IS NULL OR length(btrim(change_reason)) NOT BETWEEN 5 AND 500 THEN
        RAISE EXCEPTION 'Choose a valid profile and enter a change reason' USING ERRCODE='23514';
    END IF;
    PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||tenant::text,0));
    SELECT business_profile_code,business_profile_version INTO previous_code,previous_version
        FROM public.tenants WHERE tenant_id=tenant FOR UPDATE;
    IF previous_version IS DISTINCT FROM expected_version THEN
        RAISE EXCEPTION 'Business profile changed; reload before saving' USING ERRCODE='23514';
    END IF;
    IF previous_code=target_code AND target_code<>'BIKE_ACCESSORIES' THEN RETURN previous_version; END IF;
    -- Definer ownership sees all locations and inactive rows, not just the active
    -- location's RLS window. Historical records cannot be hidden by reclassification.
    IF NOT (previous_code IN('ELECTRICAL','BIKE_ACCESSORIES') AND target_code IN('ELECTRICAL','BIKE_ACCESSORIES')
       AND (SELECT profile_catalog_switching_enabled FROM public.tenants WHERE tenant_id=tenant)) AND
       (EXISTS(SELECT 1 FROM public.products WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.purchases WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.sales WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.service_requests WHERE tenant_id=tenant)) THEN
        RAISE EXCEPTION 'Profile change blocked: this business has products or transactions' USING ERRCODE='23514';
    END IF;
    UPDATE public.tenants SET business_profile_code=target_code,business_profile_version=previous_version+1 WHERE tenant_id=tenant;
    IF target_code='BIKE_ACCESSORIES' THEN PERFORM public.sf_seed_bike_catalog(tenant);
    ELSIF create_starters THEN
        starter_names:=CASE target_code
            WHEN 'BIKE_ACCESSORIES' THEN ARRAY['Helmets','Gloves','Bike Covers','Mirrors','Bike Lights','Bike Luggage']
            WHEN 'CLOTHING' THEN ARRAY['Shirts','T-Shirts','Trousers','Dresses','Sarees']
            WHEN 'FOOTWEAR' THEN ARRAY['Slippers','Sandals','Shoes'] ELSE ARRAY[]::text[] END;
        FOREACH category_name_value IN ARRAY starter_names LOOP
            INSERT INTO public.categories(tenant_id,category_name,business_profile_code,is_active)
                VALUES(tenant,category_name_value,target_code,true) ON CONFLICT DO NOTHING;
        END LOOP;
    END IF;
    INSERT INTO public.audit_logs(tenant_id,user_id,location_id,module_name,action_name,table_name,record_id,reason,old_values,new_values)
        VALUES(tenant,actor,NULL,'Business profile','UPDATE','tenants',tenant,btrim(change_reason),
            jsonb_build_object('businessProfileCode',previous_code,'version',previous_version),
            jsonb_build_object('businessProfileCode',target_code,'version',previous_version+1,'starterCategoriesRequested',create_starters));
    RETURN previous_version+1;
END $$;


REVOKE ALL ON FUNCTION public.sf_change_business_profile(text,bigint,text,boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_change_business_profile(text,bigint,text,boolean) TO stockflow_runtime;

-- Guard final catalog identity at posting time, including stale browser tabs.
-- Financial/history reads remain intact. Switch back before posting against old items.
CREATE OR REPLACE FUNCTION public.sf_active_catalog_write_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE selected_profile text; product_profile text;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
 SELECT t.business_profile_code,c.business_profile_code INTO selected_profile,product_profile
 FROM public.tenants t JOIN public.products p ON p.tenant_id=t.tenant_id
 JOIN public.categories c ON c.tenant_id=p.tenant_id AND c.category_id=p.category_id
 WHERE p.tenant_id=NEW.tenant_id AND p.product_id=NEW.product_id;
 IF selected_profile IS NULL OR product_profile IS DISTINCT FROM selected_profile THEN
  RAISE EXCEPTION 'This item belongs to another shop setup. Reload, or select its original shop profile before posting.' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS z_active_catalog_write ON public.sale_items;
CREATE TRIGGER z_active_catalog_write BEFORE INSERT ON public.sale_items FOR EACH ROW EXECUTE FUNCTION public.sf_active_catalog_write_guard();
DROP TRIGGER IF EXISTS z_active_catalog_write ON public.purchase_items;
CREATE TRIGGER z_active_catalog_write BEFORE INSERT ON public.purchase_items FOR EACH ROW EXECUTE FUNCTION public.sf_active_catalog_write_guard();
DROP TRIGGER IF EXISTS z_active_catalog_write ON public.inventory_transactions;
CREATE TRIGGER z_active_catalog_write BEFORE INSERT ON public.inventory_transactions FOR EACH ROW EXECUTE FUNCTION public.sf_active_catalog_write_guard();
REVOKE ALL ON FUNCTION public.sf_active_catalog_write_guard() FROM PUBLIC;
COMMENT ON COLUMN public.tenants.profile_catalog_switching_enabled IS '050: Electrical/Bike active catalog selection with original product identities and history retained.';
COMMIT;

