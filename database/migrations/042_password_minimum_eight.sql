-- MANUAL ONLY: stop StockFlow, back up, apply once after 041 as PostgreSQL superuser.
-- Existing password hashes remain unchanged. Higher custom minimums are retained.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN
  RAISE EXCEPTION 'Apply as PostgreSQL superuser';
 END IF;
 IF to_regclass('public.login_email_challenges') IS NULL THEN
  RAISE EXCEPTION 'Apply through 041 first';
 END IF;
END $$;
ALTER TABLE public.tenant_security_settings
 DROP CONSTRAINT tenant_security_settings_minimum_password_length_check;
ALTER TABLE public.tenant_security_settings
 ALTER COLUMN minimum_password_length SET DEFAULT 8,
 ADD CONSTRAINT tenant_security_settings_minimum_password_length_check
 CHECK(minimum_password_length BETWEEN 8 AND 128);
UPDATE public.tenant_security_settings SET minimum_password_length=8
 WHERE minimum_password_length=12;
COMMIT;
