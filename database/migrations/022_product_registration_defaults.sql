-- MANUAL migration: apply after 021, with StockFlow stopped, as PostgreSQL superuser.
-- This file is never run by application startup. Back up before applying.
-- Existing products, variants, identifiers and transaction history are retained.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN
   RAISE EXCEPTION 'Run migration 022 as PostgreSQL superuser';
 END IF;
END $$;
ALTER TABLE products ADD COLUMN description varchar(2000),
 ADD COLUMN has_variants boolean NOT NULL DEFAULT false,
 ADD COLUMN warranty_provider varchar(20)
 CHECK (warranty_provider IN ('MANUFACTURER','DEALER','BOTH'));
-- Preserve the existing, already populated is_serial_tracked column as the source
-- of truth; the API exposes QUANTITY/SERIAL as TrackingType.

-- Product metadata no longer pushes prices, tax, warranty, SKU or minimum stock
-- into an existing variant. Tax/warranty are defaults for NEW variants only.
CREATE OR REPLACE FUNCTION stockflow_sync_default_variant() RETURNS trigger
LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.is_serial_tracked IS DISTINCT FROM OLD.is_serial_tracked THEN
   UPDATE product_variants SET is_serial_tracked=NEW.is_serial_tracked
   WHERE tenant_id=NEW.tenant_id AND product_id=NEW.product_id;
 END IF;
 RETURN NEW;
END $$;
-- Backfill only AFTER replacing the old synchronization function, so this
-- metadata update cannot overwrite an existing default variant's values.
UPDATE products p SET has_variants=true
 WHERE EXISTS(SELECT 1 FROM product_variants v WHERE v.tenant_id=p.tenant_id AND v.product_id=p.product_id AND NOT v.is_default);
CREATE FUNCTION sf_protect_product_tracking() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 IF NEW.is_serial_tracked IS DISTINCT FROM OLD.is_serial_tracked AND (
   EXISTS(SELECT 1 FROM public.purchase_items WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id) OR
   EXISTS(SELECT 1 FROM public.sale_items WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id) OR
   EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id) OR
   EXISTS(SELECT 1 FROM public.product_units WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id)) THEN
   RAISE EXCEPTION 'Tracking cannot change after this product has been used' USING ERRCODE='23514';
 END IF;
 IF NOT NEW.has_variants AND OLD.has_variants AND EXISTS(
   SELECT 1 FROM public.product_variants WHERE tenant_id=OLD.tenant_id AND product_id=OLD.product_id AND NOT is_default AND is_active) THEN
   RAISE EXCEPTION 'Deactivate additional variants before disabling Has Variants' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER protect_product_tracking BEFORE UPDATE ON products
 FOR EACH ROW EXECUTE FUNCTION sf_protect_product_tracking();

-- Keep the stronger tracking guard across every store, including sale history.
CREATE OR REPLACE FUNCTION stockflow_protect_variant_tracking() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 IF NEW.is_serial_tracked<>OLD.is_serial_tracked AND (
   EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id) OR
   EXISTS(SELECT 1 FROM public.product_units WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id) OR
   EXISTS(SELECT 1 FROM public.purchase_items WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id) OR
   EXISTS(SELECT 1 FROM public.sale_items WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id)) THEN
   RAISE EXCEPTION 'Tracking cannot change after the variant has been used' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;

-- New values obey the tightened limits without rewriting legacy values/history.
ALTER TABLE product_variants ADD CONSTRAINT ck_variant_registration_limits
 CHECK(length(btrim(sku)) BETWEEN 3 AND 100 AND warranty_months BETWEEN 0 AND 240
 AND minimum_stock_level BETWEEN 0 AND 999999999) NOT VALID;
-- Existing tenant-scoped SKU and barcode indexes from 020 remain in force.

CREATE TABLE variant_location_settings (
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),
 variant_id bigint NOT NULL,
 location_id bigint NOT NULL,
 minimum_stock_level integer NOT NULL CHECK(minimum_stock_level BETWEEN 0 AND 999999999),
 PRIMARY KEY(tenant_id,variant_id,location_id),
 FOREIGN KEY(tenant_id,variant_id) REFERENCES product_variants(tenant_id,variant_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)
);
ALTER TABLE variant_location_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE variant_location_settings FORCE ROW LEVEL SECURITY;
CREATE POLICY location_settings_scope ON variant_location_settings
 USING(tenant_id=sf_tenant() AND sf_can_location(location_id))
 WITH CHECK(tenant_id=sf_tenant() AND sf_can_location(location_id));
GRANT SELECT,INSERT,UPDATE,DELETE ON variant_location_settings TO stockflow_runtime;
CREATE FUNCTION sf_variant_minimum(vid bigint, fallback integer) RETURNS integer
LANGUAGE sql STABLE SECURITY INVOKER AS $$
 SELECT COALESCE((SELECT minimum_stock_level FROM variant_location_settings
 WHERE tenant_id=sf_tenant() AND variant_id=vid AND location_id=sf_location()),fallback)
$$;
GRANT EXECUTE ON FUNCTION sf_variant_minimum(bigint,integer) TO stockflow_runtime;

CREATE FUNCTION sf_audit_product_registration() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 INSERT INTO audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,new_values)
 VALUES(NEW.tenant_id,NULLIF(sf_actor(),0),'Products',CASE WHEN TG_OP='INSERT' THEN 'CREATE' ELSE 'UPDATE' END,
 'products',NEW.product_id,to_jsonb(NEW));
 RETURN NEW;
END $$;
CREATE TRIGGER audit_product_registration AFTER INSERT OR UPDATE ON products
 FOR EACH ROW EXECUTE FUNCTION sf_audit_product_registration();
COMMIT;
