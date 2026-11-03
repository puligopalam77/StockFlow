-- MANUAL migration after 019. Stop the application; back up first.
-- REVIEW: all existing stock is assigned to DEFAULT / MAIN / Main stock.
-- Rename these three records below to match the physical legacy stock location.
-- Run as a database administrator able to create/grant roles. PostgreSQL 15+.
BEGIN;
DO $$ BEGIN IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN
 RAISE EXCEPTION 'Run this migration as a PostgreSQL superuser; security helper ownership requires it';
END IF; END $$;
CREATE TABLE tenants(tenant_id BIGSERIAL PRIMARY KEY,tenant_code VARCHAR(50) NOT NULL UNIQUE,
 business_name VARCHAR(200) NOT NULL,contact_person VARCHAR(150),mobile_number VARCHAR(20),email VARCHAR(150),
 address TEXT,gst_number VARCHAR(30),is_active BOOLEAN NOT NULL DEFAULT TRUE,created_at TIMESTAMPTZ NOT NULL DEFAULT now());
CREATE UNIQUE INDEX ON tenants(lower(tenant_code));
INSERT INTO tenants(tenant_id,tenant_code,business_name) VALUES(1,'DEFAULT','Existing business');
SELECT setval(pg_get_serial_sequence('tenants','tenant_id'),1);
CREATE FUNCTION sf_tenant() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT COALESCE(NULLIF(current_setting('app.tenant_id',true),''),'0')::bigint $$;
CREATE FUNCTION sf_actor() RETURNS integer LANGUAGE sql STABLE AS $$ SELECT COALESCE(NULLIF(current_setting('app.user_id',true),''),'0')::integer $$;
CREATE FUNCTION sf_location() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT COALESCE(NULLIF(current_setting('app.location_id',true),''),'0')::bigint $$;
CREATE FUNCTION sf_store() RETURNS bigint LANGUAGE sql STABLE AS $$ SELECT COALESCE(NULLIF(current_setting('app.store_id',true),''),'0')::bigint $$;
CREATE TABLE stores(store_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant() REFERENCES tenants,
 store_code VARCHAR(50) NOT NULL,store_name VARCHAR(150) NOT NULL,address TEXT,city VARCHAR(100),state VARCHAR(100),mobile_number VARCHAR(20),gst_number VARCHAR(30),
 is_head_office BOOLEAN NOT NULL DEFAULT FALSE,is_active BOOLEAN NOT NULL DEFAULT TRUE,created_at TIMESTAMPTZ NOT NULL DEFAULT now(),UNIQUE(tenant_id,store_id));
CREATE UNIQUE INDEX ON stores(tenant_id,lower(store_code));
INSERT INTO stores(store_id,tenant_id,store_code,store_name,is_head_office) VALUES(1,1,'MAIN','Main store',TRUE);
SELECT setval(pg_get_serial_sequence('stores','store_id'),1);
CREATE TABLE store_locations(location_id BIGSERIAL PRIMARY KEY,tenant_id BIGINT NOT NULL DEFAULT sf_tenant() REFERENCES tenants,
 store_id BIGINT,location_name VARCHAR(150) NOT NULL,location_type VARCHAR(30) NOT NULL DEFAULT 'STOCKROOM'
 CHECK(location_type IN('SALES_FLOOR','BACKROOM','STOCKROOM','WAREHOUSE','SERVICE_CENTER','TRANSIT')),
 is_active BOOLEAN NOT NULL DEFAULT TRUE,UNIQUE(tenant_id,location_id),FOREIGN KEY(tenant_id,store_id) REFERENCES stores(tenant_id,store_id));
INSERT INTO store_locations(location_id,tenant_id,store_id,location_name,location_type) VALUES(1,1,1,'Main stock','SALES_FLOOR');
SELECT setval(pg_get_serial_sequence('store_locations','location_id'),1);
-- Every existing business table (modules are the shared, immutable permission vocabulary).
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['roles','users','categories','brands','suppliers','customers','products','product_variants','variant_barcodes',
 'product_units','purchases','purchase_items','purchase_receipts','purchase_receipt_items','sales','sale_items','payments','inventory_transactions',
 'warranties','warranty_claims','warranty_events','service_requests','service_parts','service_history','service_operations','audit_logs','company_settings',
 'user_module_permissions','role_module_permissions'] LOOP
  EXECUTE format('ALTER TABLE %I ADD COLUMN tenant_id bigint NOT NULL DEFAULT 1 REFERENCES tenants(tenant_id)',tab);
  EXECUTE format('ALTER TABLE %I ALTER COLUMN tenant_id SET DEFAULT sf_tenant()',tab);
  EXECUTE format('CREATE INDEX ON %I(tenant_id)',tab);
 END LOOP;
END $$;
ALTER TABLE users ADD COLUMN is_tenant_owner BOOLEAN NOT NULL DEFAULT FALSE;
UPDATE users u SET is_tenant_owner=TRUE FROM roles r WHERE r.role_id=u.role_id AND r.role_name='Admin';
ALTER TABLE users ADD UNIQUE(tenant_id,user_id);
CREATE TABLE user_store_access(tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),user_id INT NOT NULL,store_id BIGINT NOT NULL,
 is_default BOOLEAN NOT NULL DEFAULT FALSE,is_active BOOLEAN NOT NULL DEFAULT TRUE,PRIMARY KEY(user_id,store_id),
 FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,store_id) REFERENCES stores(tenant_id,store_id));
CREATE UNIQUE INDEX ON user_store_access(user_id) WHERE is_default AND is_active;
INSERT INTO user_store_access(tenant_id,user_id,store_id,is_default) SELECT 1,user_id,1,TRUE FROM users;
CREATE TABLE user_location_access(tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),user_id INT NOT NULL,location_id BIGINT NOT NULL,
 PRIMARY KEY(user_id,location_id),FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id));
-- Per-location operational records. Children carry and validate the parent's location.
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['product_units','purchases','purchase_items','purchase_receipts','purchase_receipt_items','sales','sale_items','payments',
 'inventory_transactions','warranties','warranty_claims','warranty_events','service_requests','service_parts','service_history','service_operations','audit_logs'] LOOP
  EXECUTE format('ALTER TABLE %I ADD COLUMN location_id bigint NOT NULL DEFAULT 1',tab);
  EXECUTE format('ALTER TABLE %I ALTER COLUMN location_id SET DEFAULT NULLIF(sf_location(),0)',tab);
  EXECUTE format('ALTER TABLE %I ADD FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id)',tab);
  EXECUTE format('CREATE INDEX ON %I(tenant_id,location_id)',tab);
 END LOOP;
END $$;
-- Administrative auditing may occur in consolidated context.
ALTER TABLE audit_logs ALTER COLUMN location_id DROP NOT NULL;
ALTER TABLE sales ADD COLUMN store_id BIGINT;
UPDATE sales SET store_id=1;
ALTER TABLE sales ADD FOREIGN KEY(tenant_id,store_id) REFERENCES stores(tenant_id,store_id);
CREATE FUNCTION sf_sale_store() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 SELECT store_id INTO NEW.store_id FROM store_locations WHERE location_id=NEW.location_id AND tenant_id=NEW.tenant_id AND location_type<>'TRANSIT';
 IF NEW.store_id IS NULL THEN RAISE EXCEPTION 'Billing requires a retail/service store location' USING ERRCODE='23514'; END IF;
 RETURN NEW; END $$;
CREATE TRIGGER sale_store BEFORE INSERT ON sales FOR EACH ROW EXECUTE FUNCTION sf_sale_store();
-- Tenant-aware natural keys; globally unique surrogate IDs remain unchanged.
ALTER TABLE categories DROP CONSTRAINT IF EXISTS categories_category_name_key;
ALTER TABLE brands DROP CONSTRAINT IF EXISTS brands_brand_name_key;
ALTER TABLE users DROP CONSTRAINT IF EXISTS users_username_key;
ALTER TABLE roles DROP CONSTRAINT IF EXISTS roles_role_name_key;
DROP INDEX IF EXISTS ux_categories_name,ux_brands_name,ux_users_username_ci,ux_users_email_ci,ux_products_sku,ux_products_barcode,ux_products_barcode_ci,
 ux_variants_sku,ux_variant_barcode,ux_product_units_serial_number,ux_product_units_serial_ci,ux_product_units_barcode_ci,ux_unit_internal_barcode;
CREATE UNIQUE INDEX ux_categories_name ON categories(tenant_id,lower(btrim(category_name)));
CREATE UNIQUE INDEX ux_brands_name ON brands(tenant_id,lower(btrim(brand_name)));
CREATE UNIQUE INDEX ux_users_username_ci ON users(tenant_id,lower(username));
CREATE UNIQUE INDEX ux_users_email_ci ON users(tenant_id,lower(email)) WHERE NULLIF(btrim(email),'') IS NOT NULL;
CREATE UNIQUE INDEX ON roles(tenant_id,role_name);
CREATE UNIQUE INDEX ux_products_sku ON products(tenant_id,lower(btrim(sku))) WHERE sku IS NOT NULL;
CREATE UNIQUE INDEX ux_products_barcode_ci ON products(tenant_id,lower(btrim(barcode))) WHERE barcode IS NOT NULL;
CREATE UNIQUE INDEX ux_variants_sku ON product_variants(tenant_id,lower(btrim(sku)));
CREATE UNIQUE INDEX ux_variant_barcode ON variant_barcodes(tenant_id,lower(btrim(barcode)));
CREATE UNIQUE INDEX ux_product_units_serial_ci ON product_units(tenant_id,lower(btrim(serial_number))) WHERE serial_number IS NOT NULL;
CREATE UNIQUE INDEX ux_product_units_barcode_ci ON product_units(tenant_id,lower(btrim(barcode))) WHERE barcode IS NOT NULL;
CREATE UNIQUE INDEX ux_unit_internal_barcode ON product_units(tenant_id,lower(btrim(internal_barcode))) WHERE internal_barcode IS NOT NULL;
ALTER TABLE company_settings DROP CONSTRAINT company_settings_pkey;
ALTER TABLE company_settings ADD PRIMARY KEY(tenant_id,company_settings_id);
-- Tracking is a shared catalog property: inspect all locations in this tenant.
CREATE OR REPLACE FUNCTION stockflow_protect_variant_tracking() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ BEGIN
 IF NEW.is_serial_tracked<>OLD.is_serial_tracked AND
 (EXISTS(SELECT 1 FROM public.inventory_transactions WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id)
 OR EXISTS(SELECT 1 FROM public.product_units WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id)
 OR EXISTS(SELECT 1 FROM public.purchase_items WHERE tenant_id=OLD.tenant_id AND variant_id=OLD.variant_id)) THEN
 RAISE EXCEPTION 'Tracking cannot change after the variant has been used' USING ERRCODE='23514'; END IF;
 RETURN NEW; END $$;
-- Add tenant-consistent counterparts to EVERY existing FK between business tables.
-- A scan identifier must remain unambiguous across every location of its business.
CREATE FUNCTION sf_protect_scan_identifiers() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE codes text[]; code text; own_unit bigint; own_mapping bigint;
BEGIN
 IF TG_TABLE_NAME='product_units' THEN
  codes:=ARRAY[NEW.serial_number,NEW.barcode,NEW.internal_barcode,NEW.manufacturer_barcode];own_unit:=NEW.product_unit_id;
 ELSE codes:=ARRAY[NEW.barcode];own_mapping:=NEW.variant_barcode_id; END IF;
 FOR code IN SELECT DISTINCT lower(btrim(x)) FROM unnest(codes) x WHERE NULLIF(btrim(x),'') IS NOT NULL ORDER BY 1 LOOP
  PERFORM pg_advisory_xact_lock(hashtextextended(NEW.tenant_id||':scan:'||code,0));
  IF EXISTS(SELECT 1 FROM public.variant_barcodes b WHERE b.tenant_id=NEW.tenant_id AND b.variant_barcode_id IS DISTINCT FROM own_mapping AND lower(btrim(b.barcode))=code)
  OR EXISTS(SELECT 1 FROM public.product_units u WHERE u.tenant_id=NEW.tenant_id AND u.product_unit_id IS DISTINCT FROM own_unit AND
   (lower(btrim(u.serial_number))=code OR lower(btrim(u.barcode))=code OR lower(btrim(u.internal_barcode))=code OR lower(btrim(u.manufacturer_barcode))=code)) THEN
   RAISE EXCEPTION 'Scan identifier already belongs to another item in this business' USING ERRCODE='23505';
  END IF;
 END LOOP;
 RETURN NEW;
END $$;
CREATE TRIGGER protect_scan_identifier BEFORE INSERT OR UPDATE OF serial_number,barcode,internal_barcode,manufacturer_barcode ON product_units FOR EACH ROW EXECUTE FUNCTION sf_protect_scan_identifiers();
CREATE TRIGGER protect_scan_identifier BEFORE INSERT OR UPDATE OF barcode ON variant_barcodes FOR EACH ROW EXECUTE FUNCTION sf_protect_scan_identifiers();
-- Add tenant-consistent counterparts to EVERY existing FK between business tables.
-- Foreign keys bypass RLS; these constraints prevent a forged cross-tenant parent ID.
DO $$ DECLARE f record; childcols text; parentcols text; BEGIN
 FOR f IN SELECT con.oid,con.conrelid,con.confrelid,con.conkey,con.confkey FROM pg_constraint con
 WHERE con.contype='f' AND con.connamespace='public'::regnamespace
 AND EXISTS(SELECT 1 FROM pg_attribute WHERE attrelid=con.conrelid AND attname='tenant_id')
 AND EXISTS(SELECT 1 FROM pg_attribute WHERE attrelid=con.confrelid AND attname='tenant_id')
 AND NOT EXISTS(SELECT 1 FROM pg_attribute WHERE attrelid=con.conrelid AND attnum=ANY(con.conkey) AND attname='tenant_id') LOOP
  SELECT string_agg(quote_ident(a.attname),',' ORDER BY k.n) INTO childcols FROM unnest(f.conkey) WITH ORDINALITY k(attnum,n) JOIN pg_attribute a ON a.attrelid=f.conrelid AND a.attnum=k.attnum;
  SELECT string_agg(quote_ident(a.attname),',' ORDER BY k.n) INTO parentcols FROM unnest(f.confkey) WITH ORDINALITY k(attnum,n) JOIN pg_attribute a ON a.attrelid=f.confrelid AND a.attnum=k.attnum;
  EXECUTE format('CREATE UNIQUE INDEX %I ON %s(tenant_id,%s)','sf_parent_'||f.oid,f.confrelid::regclass,parentcols);
  EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I FOREIGN KEY(tenant_id,%s) REFERENCES %s(tenant_id,%s)',f.conrelid::regclass,'sf_fk_'||f.oid,childcols,f.confrelid::regclass,parentcols);
 END LOOP;
END $$;
-- Link children to the same location as their transaction header.
DO $$ DECLARE spec text[]; BEGIN
 FOREACH spec SLICE 1 IN ARRAY ARRAY[
  ['purchase_items','purchases','purchase_id'],['purchase_receipts','purchases','purchase_id'],['purchase_receipt_items','purchase_receipts','receipt_id'],
  ['sale_items','sales','sale_id'],['payments','sales','sale_id'],['warranties','sale_items','sale_item_id'],['warranty_claims','warranties','warranty_id'],
  ['warranty_events','warranties','warranty_id'],['service_parts','service_requests','service_request_id'],['service_history','service_requests','service_request_id']] LOOP
  EXECUTE format('CREATE UNIQUE INDEX ON %I(tenant_id,location_id,%I)',spec[2],spec[3]);
  EXECUTE format('ALTER TABLE %I ADD FOREIGN KEY(tenant_id,location_id,%I) REFERENCES %I(tenant_id,location_id,%I)',spec[1],spec[3],spec[2],spec[3]);
 END LOOP;
END $$;
CREATE FUNCTION sf_login_tenant(code text) RETURNS bigint LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT tenant_id FROM public.tenants WHERE lower(tenant_code)=lower(btrim(code)) AND is_active $$;
CREATE FUNCTION sf_owner() RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.users u JOIN public.tenants t USING(tenant_id) WHERE u.user_id=public.sf_actor() AND u.tenant_id=public.sf_tenant() AND u.is_active AND u.is_tenant_owner AND t.is_active) $$;
CREATE FUNCTION sf_can_location(loc bigint) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT EXISTS(SELECT 1 FROM public.store_locations l JOIN public.tenants t USING(tenant_id) JOIN public.users u ON u.tenant_id=l.tenant_id
 WHERE l.location_id=loc AND l.tenant_id=public.sf_tenant() AND u.user_id=public.sf_actor() AND u.is_active AND t.is_active
 AND (u.is_tenant_owner OR (l.location_type<>'TRANSIT' AND
 (EXISTS(SELECT 1 FROM public.user_store_access a JOIN public.stores s ON s.store_id=a.store_id AND s.tenant_id=a.tenant_id WHERE a.user_id=u.user_id AND a.store_id=l.store_id AND a.tenant_id=l.tenant_id AND a.is_active AND s.is_active)
 OR EXISTS(SELECT 1 FROM public.user_location_access a WHERE a.user_id=u.user_id AND a.location_id=l.location_id AND a.tenant_id=l.tenant_id))))) $$;
CREATE FUNCTION sf_visible_location(loc bigint) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT sf_can_location(loc) AND (sf_location()=0 OR sf_location()=loc)
 AND (sf_store()=0 OR EXISTS(SELECT 1 FROM store_locations l WHERE l.location_id=loc AND l.store_id=sf_store())) $$;
-- Use invoker views so underlying tenant/location policies remain effective.
CREATE VIEW vw_location_current_stock WITH(security_invoker=true) AS
 SELECT v.tenant_id,l.store_id,l.location_id,v.variant_id,v.product_id,COALESCE(SUM(CASE
 WHEN it.transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN it.quantity::bigint
 WHEN it.transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -it.quantity::bigint ELSE 0 END),0)::bigint current_stock
 FROM product_variants v JOIN store_locations l ON l.tenant_id=v.tenant_id
 LEFT JOIN inventory_transactions it ON it.variant_id=v.variant_id AND it.location_id=l.location_id
 GROUP BY v.tenant_id,l.store_id,l.location_id,v.variant_id,v.product_id;
CREATE OR REPLACE VIEW vw_variant_current_stock WITH(security_invoker=true) AS
 SELECT v.variant_id,v.product_id,COALESCE(SUM(CASE
 WHEN it.transaction_type IN('PURCHASE','CUSTOMER_RETURN','ADJUSTMENT_IN','SERVICE_IN','TRANSFER_IN') THEN it.quantity::bigint
 WHEN it.transaction_type IN('SALE','SUPPLIER_RETURN','DAMAGED','ADJUSTMENT_OUT','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS') THEN -it.quantity::bigint ELSE 0 END),0)::bigint current_stock
 FROM product_variants v LEFT JOIN inventory_transactions it ON it.variant_id=v.variant_id GROUP BY v.variant_id,v.product_id;
ALTER VIEW vw_current_stock SET(security_invoker=true);
DO $$ DECLARE tab text; BEGIN
 FOR tab IN SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r'
 AND c.relname<>'tenants' AND EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='tenant_id') LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);
  EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('CREATE POLICY tenant_isolation ON %I USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant())',tab);
 END LOOP;
 FOREACH tab IN ARRAY ARRAY['product_units','purchases','purchase_items','purchase_receipts','purchase_receipt_items','sales','sale_items','payments',
 'inventory_transactions','warranties','warranty_claims','warranty_events','service_requests','service_parts','service_history','service_operations'] LOOP
  EXECUTE format('CREATE POLICY location_isolation ON %I AS RESTRICTIVE USING(sf_visible_location(location_id)) WITH CHECK(sf_location()>0 AND sf_visible_location(location_id))',tab);
 END LOOP;
END $$;
ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_self ON tenants USING(tenant_id=sf_tenant());
CREATE POLICY assigned_locations ON store_locations AS RESTRICTIVE USING(sf_can_location(location_id)) WITH CHECK(tenant_id=sf_tenant() AND sf_owner());
CREATE POLICY assigned_stores ON stores AS RESTRICTIVE USING(sf_owner() OR EXISTS(SELECT 1 FROM user_store_access a WHERE a.store_id=stores.store_id AND a.user_id=sf_actor() AND a.is_active)) WITH CHECK(sf_owner());
CREATE POLICY audit_locations ON audit_logs AS RESTRICTIVE USING(sf_owner() OR location_id IS NULL OR sf_visible_location(location_id)) WITH CHECK(tenant_id=sf_tenant());
-- Runtime always switches to this non-owner role, even if an old connection string uses postgres.
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime') THEN CREATE ROLE stockflow_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS; END IF;
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime' AND (rolsuper OR rolbypassrls)) THEN RAISE EXCEPTION 'stockflow_runtime must not bypass row security'; END IF;
 EXECUTE format('GRANT stockflow_runtime TO %I',session_user);
END $$;
GRANT USAGE ON SCHEMA public TO stockflow_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO stockflow_runtime;
REVOKE INSERT,UPDATE,DELETE ON tenants,modules FROM stockflow_runtime;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO stockflow_runtime;
REVOKE ALL ON FUNCTION sf_login_tenant(text),sf_owner(),sf_can_location(bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_login_tenant(text),sf_owner(),sf_can_location(bigint) TO stockflow_runtime;
-- Store administration and detailed transfer responsibilities use explicit modules.
INSERT INTO modules(module_code,module_name,route,sort_order) VALUES
 ('STORES','Store management','/Admin/Stores',83),('STOCK_TRANSFER','Stock transfers','/Transfers',24),
 ('TRANSFER_DISPATCH','Dispatch transfers',NULL,25),('TRANSFER_RECEIVE','Receive transfers',NULL,26),('TRANSFER_CANCEL','Cancel transfers',NULL,27);
INSERT INTO role_module_permissions(role_id,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export,tenant_id)
 SELECT r.role_id,m.module_id,TRUE,TRUE,TRUE,TRUE,TRUE,TRUE,r.tenant_id FROM roles r CROSS JOIN modules m
 WHERE r.role_name='Admin' AND m.module_code IN('STORES','STOCK_TRANSFER','TRANSFER_DISPATCH','TRANSFER_RECEIVE','TRANSFER_CANCEL');
INSERT INTO user_module_permissions(user_id,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export,tenant_id)
 SELECT u.user_id,m.module_id,TRUE,TRUE,TRUE,TRUE,TRUE,TRUE,u.tenant_id FROM users u CROSS JOIN modules m
 WHERE u.is_tenant_owner AND m.module_code IN('STORES','STOCK_TRANSFER','TRANSFER_DISPATCH','TRANSFER_RECEIVE','TRANSFER_CANCEL');
COMMIT;
