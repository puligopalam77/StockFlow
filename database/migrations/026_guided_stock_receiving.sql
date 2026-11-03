-- MANUAL: run once after migration 025 as PostgreSQL superuser.
-- Stop StockFlow and back up the application database first.
BEGIN;
DO $$ BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN
  RAISE EXCEPTION 'Run migration 026 as PostgreSQL superuser';
 END IF;
 IF to_regclass('public.module_actions') IS NULL THEN
  RAISE EXCEPTION 'Apply migrations through 025 before migration 026';
 END IF;
END $$;
CREATE TABLE stock_intakes (
 intake_id BIGSERIAL PRIMARY KEY,
 tenant_id BIGINT NOT NULL DEFAULT sf_tenant() REFERENCES tenants,
 location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 kind VARCHAR(20) NOT NULL CHECK(kind IN ('RECEIPT','OPENING_STOCK')),
 received_date DATE NOT NULL,
 request_id UUID NOT NULL UNIQUE,
 request_payload JSONB NOT NULL,
 purchase_id BIGINT,
 created_by INT NOT NULL,
 created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,location_id,intake_id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,created_by) REFERENCES users(tenant_id,user_id),
 FOREIGN KEY(tenant_id,location_id,purchase_id) REFERENCES purchases(tenant_id,location_id,purchase_id),
 CHECK(kind<>'OPENING_STOCK' OR purchase_id IS NULL)
);
CREATE TABLE stock_intake_items (
 intake_item_id BIGSERIAL PRIMARY KEY,
 tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),
 location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 intake_id BIGINT NOT NULL,
 product_id INT NOT NULL,
 variant_id BIGINT NOT NULL,
 product_name_snapshot VARCHAR(150) NOT NULL,
 variant_name_snapshot VARCHAR(150) NOT NULL,
 quantity INT NOT NULL CHECK(quantity BETWEEN 1 AND 10000),
 purchase_price NUMERIC(12,2) NOT NULL CHECK(purchase_price>=0),
 tax_percentage NUMERIC(5,2) NOT NULL CHECK(tax_percentage BETWEEN 0 AND 100),
 serial_numbers TEXT[] NOT NULL DEFAULT '{}',
 FOREIGN KEY(tenant_id,location_id,intake_id) REFERENCES stock_intakes(tenant_id,location_id,intake_id),
 FOREIGN KEY(tenant_id,product_id) REFERENCES products(tenant_id,product_id),
 FOREIGN KEY(tenant_id,variant_id) REFERENCES product_variants(tenant_id,variant_id)
);
CREATE INDEX ON stock_intakes(tenant_id,location_id,received_date);
CREATE INDEX ON stock_intake_items(intake_id);
ALTER TABLE stock_intakes ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_intakes FORCE ROW LEVEL SECURITY;
CREATE POLICY intake_tenant ON stock_intakes USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant());
CREATE POLICY intake_location ON stock_intakes AS RESTRICTIVE USING(sf_visible_location(location_id)) WITH CHECK(sf_location()>0 AND sf_visible_location(location_id));
ALTER TABLE stock_intake_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_intake_items FORCE ROW LEVEL SECURITY;
CREATE POLICY intake_item_tenant ON stock_intake_items USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant());
CREATE POLICY intake_item_location ON stock_intake_items AS RESTRICTIVE USING(sf_visible_location(location_id)) WITH CHECK(sf_location()>0 AND sf_visible_location(location_id));
GRANT SELECT,INSERT,UPDATE ON stock_intakes TO stockflow_runtime;
GRANT SELECT,INSERT ON stock_intake_items TO stockflow_runtime;
GRANT USAGE,SELECT ON SEQUENCE stock_intakes_intake_id_seq,stock_intake_items_intake_item_id_seq TO stockflow_runtime;
COMMIT;
