-- MANUAL ONLY: stop StockFlow, back up, apply once after 044 as PostgreSQL superuser.
-- Retire email login OTP without removing notification/email delivery data.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_login_email_user_required(bigint,integer)') IS NULL THEN RAISE EXCEPTION 'Apply through 044 first'; END IF;
END $$;
UPDATE public.users SET require_login_email_otp=false WHERE require_login_email_otp;
UPDATE public.notification_delivery_settings SET login_email_otp=false WHERE login_email_otp;
UPDATE public.login_email_challenges SET consumed_at=now() WHERE consumed_at IS NULL;
CREATE OR REPLACE FUNCTION sf_login_email_required(tid bigint) RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ SELECT false $$;
CREATE OR REPLACE FUNCTION sf_login_email_user_required(tid bigint,uid integer) RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$ SELECT false $$;
-- Remove worker access to retired sign-in functions; preserve sf_notification_test_reserve.
DO $$ DECLARE f record; BEGIN
 FOR f IN SELECT p.oid::regprocedure AS signature FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
 WHERE n.nspname='public' AND p.proname IN('sf_login_email_begin','sf_login_email_verify','sf_login_email_cancel') LOOP
  EXECUTE format('REVOKE ALL ON FUNCTION %s FROM stockflow_notification_worker',f.signature);
 END LOOP;
END $$;
COMMIT;
