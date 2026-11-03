-- MANUAL: stop StockFlow, take a backup, apply after 034 as a PostgreSQL superuser, restart.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.billing_drafts') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 034 first'; END IF;
END $$;
CREATE TABLE cash_closings(
 closing_id BIGSERIAL PRIMARY KEY,
 tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),
 location_id BIGINT NOT NULL DEFAULT NULLIF(sf_location(),0),
 business_date DATE NOT NULL,version INT NOT NULL CHECK(version>0),
 request_id UUID NOT NULL,request_payload JSONB NOT NULL,snapshot JSONB NOT NULL,
 closed_by INT NOT NULL,closed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,request_id),UNIQUE(tenant_id,location_id,business_date,version),
 FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,closed_by) REFERENCES users(tenant_id,user_id)
);
ALTER TABLE cash_closings ENABLE ROW LEVEL SECURITY;
ALTER TABLE cash_closings FORCE ROW LEVEL SECURITY;
CREATE POLICY cash_tenant ON cash_closings USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant());
CREATE POLICY cash_location ON cash_closings AS RESTRICTIVE USING(location_id=sf_location()) WITH CHECK(location_id=sf_location());
GRANT SELECT,INSERT ON cash_closings TO stockflow_runtime;
GRANT USAGE,SELECT ON SEQUENCE cash_closings_closing_id_seq TO stockflow_runtime;
-- Records are append-only: corrections create a new version with an explanation.
COMMIT;
