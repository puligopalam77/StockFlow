-- MANUAL: stop StockFlow, back up, execute once after 031 as PostgreSQL superuser, then restart.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name='purchase_receipts' AND column_name='receipt_number') THEN
  RAISE EXCEPTION 'Apply migrations through 031 first'; END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_brand_category_parent ON brands(tenant_id,brand_id);
CREATE TABLE brand_categories(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),
 brand_id integer NOT NULL,
 category_id integer NOT NULL,
 PRIMARY KEY(tenant_id,brand_id,category_id),
 FOREIGN KEY(tenant_id,brand_id) REFERENCES brands(tenant_id,brand_id),
 FOREIGN KEY(tenant_id,category_id) REFERENCES categories(tenant_id,category_id)
);
CREATE INDEX ix_brand_categories_category ON brand_categories(tenant_id,category_id,brand_id);
-- Preserve existing product assignments; do not guess relationships for unused brands.
INSERT INTO brand_categories(tenant_id,brand_id,category_id)
 SELECT DISTINCT tenant_id,brand_id,category_id FROM products WHERE brand_id IS NOT NULL;
ALTER TABLE brand_categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE brand_categories FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON brand_categories USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant());
GRANT SELECT,INSERT,UPDATE,DELETE ON brand_categories TO stockflow_runtime;
CREATE FUNCTION sf_protect_brand_category() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:' || COALESCE(NEW.tenant_id,OLD.tenant_id)::text,0));
 IF TG_OP='DELETE' THEN
  IF EXISTS(SELECT 1 FROM products WHERE tenant_id=OLD.tenant_id AND brand_id=OLD.brand_id AND category_id=OLD.category_id AND is_active) THEN
   RAISE EXCEPTION 'Reassign active products before removing this brand category' USING ERRCODE='23514'; END IF;
  RETURN OLD;
 END IF;
 IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Replace brand categories using insert/delete' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM categories WHERE tenant_id=NEW.tenant_id AND category_id=NEW.category_id AND is_active) THEN
  RAISE EXCEPTION 'Choose an active category for this brand' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER protect_brand_category BEFORE INSERT OR UPDATE OR DELETE ON brand_categories FOR EACH ROW EXECUTE FUNCTION sf_protect_brand_category();
CREATE FUNCTION sf_check_product_brand_category() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:' || NEW.tenant_id::text,0));
 IF NEW.is_active AND NEW.brand_id IS NOT NULL AND NOT EXISTS(
  SELECT 1 FROM brand_categories WHERE tenant_id=NEW.tenant_id AND brand_id=NEW.brand_id AND category_id=NEW.category_id
 ) THEN RAISE EXCEPTION 'Brand is not assigned to the selected category' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER check_product_brand_category BEFORE INSERT OR UPDATE ON products FOR EACH ROW EXECUTE FUNCTION sf_check_product_brand_category();
COMMIT;
