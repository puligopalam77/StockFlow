-- MANUAL ONLY. Apply after 046 as PostgreSQL superuser, in a maintenance window.
-- Back up first. No existing tenant profile, product, stock or history is converted.
-- Application startup does not run this file. Review verification queries afterward.
BEGIN;
DO $$ BEGIN
    IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN
        RAISE EXCEPTION 'Apply migration 047 as a PostgreSQL superuser';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='tenants' AND column_name='business_profile_code') THEN
        RAISE EXCEPTION 'Apply migration 046 first';
    END IF;
END $$;

ALTER TABLE public.tenants DROP CONSTRAINT IF EXISTS ck_tenants_phase1_business_profile;
ALTER TABLE public.tenants ADD COLUMN IF NOT EXISTS business_profile_version bigint NOT NULL DEFAULT 1;
ALTER TABLE public.tenants DROP CONSTRAINT IF EXISTS ck_tenants_business_profile_version;
ALTER TABLE public.tenants ADD CONSTRAINT ck_tenants_business_profile_version CHECK(business_profile_version>=1);
ALTER TABLE public.tenants DROP CONSTRAINT IF EXISTS ck_tenants_business_profile;
ALTER TABLE public.tenants ADD CONSTRAINT ck_tenants_business_profile
    CHECK(business_profile_code IN('ELECTRICAL','BIKE_ACCESSORIES','CLOTHING','FOOTWEAR'));

CREATE OR REPLACE FUNCTION public.sf_current_business_profile() RETURNS text
LANGUAGE sql STABLE SET search_path=pg_catalog,public,pg_temp AS $$
    SELECT business_profile_code FROM public.tenants WHERE tenant_id=public.sf_tenant()
$$;

-- Existing category identities and relationships remain intact. They belong to
-- the original electrical profile; profile changes do not relabel or delete them.
ALTER TABLE public.categories ADD COLUMN IF NOT EXISTS business_profile_code varchar(40) NOT NULL DEFAULT 'ELECTRICAL';
ALTER TABLE public.categories ALTER COLUMN business_profile_code SET DEFAULT public.sf_current_business_profile();
ALTER TABLE public.categories DROP CONSTRAINT IF EXISTS ck_category_business_profile;
ALTER TABLE public.categories ADD CONSTRAINT ck_category_business_profile
    CHECK(business_profile_code IN('ELECTRICAL','BIKE_ACCESSORIES','CLOTHING','FOOTWEAR'));
DROP INDEX IF EXISTS public.ux_category_root_name;
DROP INDEX IF EXISTS public.ux_category_child_name;
CREATE UNIQUE INDEX ux_category_root_name ON public.categories(tenant_id,business_profile_code,lower(btrim(category_name))) WHERE parent_category_id IS NULL;
CREATE UNIQUE INDEX ux_category_child_name ON public.categories(tenant_id,business_profile_code,parent_category_id,lower(btrim(category_name))) WHERE parent_category_id IS NOT NULL;

CREATE OR REPLACE FUNCTION public.sf_profile_catalog_guard() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE current_profile text; category_profile text;
BEGIN
    -- Share the existing catalog lock: switching a profile cannot race a category
    -- or product insert. The function below acquires this lock in the same order.
    PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||NEW.tenant_id::text,0));
    SELECT business_profile_code INTO current_profile FROM public.tenants WHERE tenant_id=NEW.tenant_id;
    IF current_profile IS NULL THEN RAISE EXCEPTION 'Business profile not found' USING ERRCODE='23514'; END IF;
    IF TG_TABLE_NAME='categories' THEN
        IF NEW.business_profile_code IS NULL THEN NEW.business_profile_code:=current_profile; END IF;
        IF NEW.business_profile_code<>current_profile THEN
            RAISE EXCEPTION 'Category does not belong to the current shop profile' USING ERRCODE='23514';
        END IF;
        IF NEW.parent_category_id IS NOT NULL AND NOT EXISTS(
            SELECT 1 FROM public.categories WHERE tenant_id=NEW.tenant_id
                AND category_id=NEW.parent_category_id AND business_profile_code=current_profile
        ) THEN RAISE EXCEPTION 'Parent category belongs to a different profile' USING ERRCODE='23514'; END IF;
    ELSIF TG_TABLE_NAME='products' THEN
        SELECT business_profile_code INTO category_profile FROM public.categories
            WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id;
        IF category_profile IS DISTINCT FROM current_profile THEN
            RAISE EXCEPTION 'Product category does not belong to the current shop profile' USING ERRCODE='23514';
        END IF;
        IF current_profile<>'ELECTRICAL' AND (NEW.is_serial_tracked OR NEW.warranty_months>0) THEN
            RAISE EXCEPTION 'Serial tracking and warranty are not enabled for this shop profile' USING ERRCODE='23514';
        END IF;
    ELSE
        IF current_profile<>'ELECTRICAL' AND (NEW.is_serial_tracked OR NEW.warranty_months>0) THEN
            RAISE EXCEPTION 'Serial tracking and warranty are not enabled for this shop profile' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS aa_business_profile_guard ON public.categories;
CREATE TRIGGER aa_business_profile_guard BEFORE INSERT OR UPDATE ON public.categories FOR EACH ROW EXECUTE FUNCTION public.sf_profile_catalog_guard();
DROP TRIGGER IF EXISTS aa_business_profile_guard ON public.products;
CREATE TRIGGER aa_business_profile_guard BEFORE INSERT OR UPDATE ON public.products FOR EACH ROW EXECUTE FUNCTION public.sf_profile_catalog_guard();
DROP TRIGGER IF EXISTS aa_business_profile_guard ON public.product_variants;
CREATE TRIGGER aa_business_profile_guard BEFORE INSERT OR UPDATE ON public.product_variants FOR EACH ROW EXECUTE FUNCTION public.sf_profile_catalog_guard();

-- Impact is tenant-wide, including other branches and inactive catalog rows.
-- Location-limited queries must never suggest that a populated business is empty.
CREATE OR REPLACE FUNCTION public.sf_business_profile_impact()
RETURNS TABLE("ProductCount" integer,"CategoryCount" integer,"TransactionCount" integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tenant bigint:=public.sf_tenant(); actor integer:=public.sf_actor();
BEGIN
    IF tenant<=0 OR actor<=0 OR NOT EXISTS(
        SELECT 1 FROM public.users u JOIN public.roles r ON r.role_id=u.role_id AND r.tenant_id=u.tenant_id
        JOIN public.tenants t ON t.tenant_id=u.tenant_id
        WHERE u.tenant_id=tenant AND u.user_id=actor AND u.is_active AND r.is_active AND t.is_active
            AND (u.is_tenant_owner OR lower(btrim(r.role_name))='admin')
    ) OR NOT EXISTS(
        SELECT 1 FROM public.user_action_permissions p JOIN public.module_actions a ON a.module_action_id=p.module_action_id
        JOIN public.modules m ON m.module_id=a.module_id
        WHERE p.tenant_id=tenant AND p.user_id=actor AND p.is_allowed AND a.is_active AND m.is_active
            AND m.module_code='COMPANY_SETTINGS' AND a.action_code='VIEW'
    ) THEN RAISE EXCEPTION 'Profile impact requires an authorized administrator' USING ERRCODE='42501'; END IF;
    RETURN QUERY SELECT (SELECT count(*)::integer FROM public.products WHERE tenant_id=tenant),
        (SELECT count(*)::integer FROM public.categories WHERE tenant_id=tenant),
        ((SELECT count(*) FROM public.purchases WHERE tenant_id=tenant)+(SELECT count(*) FROM public.sales WHERE tenant_id=tenant)
         +(SELECT count(*) FROM public.inventory_transactions WHERE tenant_id=tenant)+(SELECT count(*) FROM public.service_requests WHERE tenant_id=tenant))::integer;
END $$;

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
    IF previous_code=target_code THEN RETURN previous_version; END IF;
    -- Definer ownership sees all locations and inactive rows, not just the active
    -- location's RLS window. Historical records cannot be hidden by reclassification.
    IF EXISTS(SELECT 1 FROM public.products WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.purchases WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.sales WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=tenant)
       OR EXISTS(SELECT 1 FROM public.service_requests WHERE tenant_id=tenant) THEN
        RAISE EXCEPTION 'Profile change blocked: this business has products or transactions' USING ERRCODE='23514';
    END IF;
    UPDATE public.tenants SET business_profile_code=target_code,business_profile_version=previous_version+1 WHERE tenant_id=tenant;
    IF create_starters THEN
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
REVOKE ALL ON FUNCTION public.sf_business_profile_impact() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_business_profile_impact() TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_profile_catalog_guard() FROM PUBLIC;
COMMENT ON COLUMN public.tenants.business_profile_version IS 'Optimistic concurrency version for administrator-controlled profile changes.';
COMMIT;
