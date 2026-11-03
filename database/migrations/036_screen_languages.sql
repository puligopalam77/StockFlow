-- MANUAL: stop the app, back up, apply after 035 as PostgreSQL superuser, restart.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.cash_closings') IS NULL THEN RAISE EXCEPTION 'Apply migration 035 first'; END IF;
END $$;
CREATE TABLE screen_language_settings(
 tenant_id BIGINT NOT NULL DEFAULT sf_tenant(),screen VARCHAR(50) NOT NULL,setting JSONB NOT NULL,
 updated_by INT NOT NULL,updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),PRIMARY KEY(tenant_id,screen),
 FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id)
);
ALTER TABLE screen_language_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE screen_language_settings FORCE ROW LEVEL SECURITY;
CREATE POLICY screen_language_tenant ON screen_language_settings USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant());
GRANT SELECT,INSERT,UPDATE ON screen_language_settings TO stockflow_runtime;
COMMIT;
