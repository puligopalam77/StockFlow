-- MANUAL ONLY. Back up and stop application/workers; apply after 063 as PostgreSQL superuser.
-- Preserves existing product/variant IDs, SKU/barcode identities, stock and history.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply 064 as PostgreSQL superuser'; END IF;
 IF to_regclass('public.purchase_changes') IS NULL THEN RAISE EXCEPTION 'Apply 063 first'; END IF;
END $$;
ALTER TABLE public.products ADD COLUMN parent_product_code varchar(40);
ALTER TABLE public.products ADD COLUMN detailed_description varchar(6000);
CREATE UNIQUE INDEX ux_product_parent_code ON public.products(tenant_id,upper(btrim(parent_product_code))) WHERE parent_product_code IS NOT NULL;
ALTER TABLE public.products ADD CHECK(parent_product_code IS NULL OR parent_product_code ~ '^[A-Za-z0-9][A-Za-z0-9_-]{2,39}$');
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
        IF (current_profile<>'ELECTRICAL' AND NEW.is_serial_tracked) OR (current_profile NOT IN('ELECTRICAL','BIKE_ACCESSORIES') AND NEW.warranty_months>0) THEN
            RAISE EXCEPTION 'Serial tracking and warranty are not enabled for this shop profile' USING ERRCODE='23514';
        END IF;
    ELSE
        IF (current_profile<>'ELECTRICAL' AND NEW.is_serial_tracked) OR (current_profile NOT IN('ELECTRICAL','BIKE_ACCESSORIES') AND NEW.warranty_months>0) THEN
            RAISE EXCEPTION 'Serial tracking and warranty are not enabled for this shop profile' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;

REVOKE ALL ON FUNCTION public.sf_profile_catalog_guard() FROM PUBLIC;
COMMIT;
