-- MANUAL ONLY: stop StockFlow, back up, apply ONCE after 040 as PostgreSQL superuser.
-- Email OTP defaults OFF. Existing passwords and authenticator requirements are preserved.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.notification_delivery_settings') IS NULL THEN RAISE EXCEPTION 'Apply 040 first'; END IF;
END $$;
ALTER TABLE notification_delivery_settings ADD COLUMN login_email_otp boolean NOT NULL DEFAULT false;
CREATE TABLE login_email_challenges(
 id uuid PRIMARY KEY,tenant_id bigint NOT NULL,user_id integer NOT NULL,security_version bigint NOT NULL,
 code_hash varchar(64) NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),expires_at timestamptz NOT NULL DEFAULT now()+interval '5 minutes',
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),consumed_at timestamptz,
 FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id));
CREATE INDEX login_email_user ON login_email_challenges(tenant_id,user_id,created_at DESC);
ALTER TABLE login_email_challenges ENABLE ROW LEVEL SECURITY;
ALTER TABLE login_email_challenges FORCE ROW LEVEL SECURITY;
-- No direct runtime table access. OTP hashes are keyed on the server; plaintext codes are never persisted.
CREATE FUNCTION sf_login_email_required(tid bigint) RETURNS boolean LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT COALESCE((SELECT login_email_otp FROM public.notification_delivery_settings WHERE tenant_id=tid),false)
$$;
CREATE FUNCTION sf_login_email_settings(enable_otp boolean) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NOT public.sf_transfer_permission('COMPANY_SETTINGS','EDIT') THEN RAISE EXCEPTION 'Permission denied' USING ERRCODE='42501'; END IF;
 IF enable_otp AND (NOT EXISTS(SELECT 1 FROM public.notification_delivery_settings s WHERE s.tenant_id=public.sf_tenant() AND s.email_enabled AND s.sender_verified)
  OR EXISTS(SELECT 1 FROM public.users u JOIN public.roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id WHERE u.tenant_id=public.sf_tenant() AND u.is_active AND r.is_active AND (u.email IS NULL OR btrim(u.email)='' OR u.email NOT LIKE '%_@_%._%'))) THEN
  RAISE EXCEPTION 'Verify the sender and add valid email addresses for every active staff account before enabling login OTP.' USING ERRCODE='22023';
 END IF;
 UPDATE public.notification_delivery_settings SET login_email_otp=enable_otp,updated_at=now(),updated_by=public.sf_actor() WHERE tenant_id=public.sf_tenant();
END $$;
CREATE FUNCTION sf_login_email_begin(tid bigint,uid integer,security_stamp bigint,challenge uuid,hash text)
RETURNS TABLE(recipient text,sender_email text,sender_name text) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE s public.notification_delivery_settings; addr text; quota integer;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('login-email:'||tid||':'||uid,0));
 SELECT * INTO s FROM public.notification_delivery_settings WHERE tenant_id=tid AND login_email_otp AND email_enabled AND sender_verified;
 IF s.tenant_id IS NULL THEN RETURN; END IF;
 SELECT u.email INTO addr FROM public.users u JOIN public.roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id JOIN public.tenants t ON t.tenant_id=u.tenant_id
 WHERE u.tenant_id=tid AND u.user_id=uid AND u.security_version=security_stamp AND u.is_active AND r.is_active AND t.is_active;
 IF addr IS NULL OR btrim(addr)='' OR length(hash)<>64 OR EXISTS(SELECT 1 FROM public.login_email_challenges c WHERE c.tenant_id=tid AND c.user_id=uid AND c.created_at>now()-interval '60 seconds')
 OR (SELECT count(*) FROM public.login_email_challenges c WHERE c.tenant_id=tid AND c.user_id=uid AND c.created_at>now()-interval '30 minutes')>=5 THEN RETURN; END IF;
 INSERT INTO public.notification_email_quota VALUES(tid,(now() AT TIME ZONE 'UTC')::date,0) ON CONFLICT DO NOTHING;
 UPDATE public.notification_email_quota q SET reserved=reserved+1 WHERE q.tenant_id=tid AND q.day=(now() AT TIME ZONE 'UTC')::date AND q.reserved<s.daily_limit RETURNING reserved INTO quota;
 IF quota IS NULL THEN RETURN; END IF;
 UPDATE public.login_email_challenges c SET consumed_at=now() WHERE c.tenant_id=tid AND c.user_id=uid AND c.consumed_at IS NULL;
 INSERT INTO public.login_email_challenges(id,tenant_id,user_id,security_version,code_hash) VALUES(challenge,tid,uid,security_stamp,hash);
 DELETE FROM public.login_email_challenges c WHERE c.tenant_id=tid AND c.user_id=uid AND c.created_at<now()-interval '1 day';
 RETURN QUERY SELECT btrim(addr),s.sender_email::text,s.sender_name::text;
END $$;
CREATE FUNCTION sf_login_email_verify(challenge uuid,tid bigint,uid integer,security_stamp bigint,hash text) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE c public.login_email_challenges; valid boolean;
BEGIN
 SELECT * INTO c FROM public.login_email_challenges WHERE id=challenge AND tenant_id=tid AND user_id=uid FOR UPDATE;
 IF c.id IS NULL OR c.consumed_at IS NOT NULL OR c.expires_at<=now() OR c.attempts>=5 THEN RETURN false; END IF;
 valid:=c.code_hash=hash AND c.security_version=security_stamp AND EXISTS(SELECT 1 FROM public.users u JOIN public.roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id JOIN public.tenants t ON t.tenant_id=u.tenant_id WHERE u.tenant_id=tid AND u.user_id=uid AND u.security_version=security_stamp AND u.is_active AND r.is_active AND t.is_active);
 UPDATE public.login_email_challenges SET attempts=attempts+1,consumed_at=CASE WHEN valid OR attempts+1>=5 THEN now() ELSE NULL END WHERE id=challenge;
 RETURN valid;
END $$;
CREATE TABLE notification_test_runs(tenant_id bigint PRIMARY KEY REFERENCES tenants,last_attempt_at timestamptz NOT NULL);
ALTER TABLE notification_test_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_test_runs FORCE ROW LEVEL SECURITY;
CREATE FUNCTION sf_notification_test_reserve(tid bigint) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE s public.notification_delivery_settings; used integer;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('notification-test:'||tid,0));
 SELECT * INTO s FROM public.notification_delivery_settings WHERE tenant_id=tid AND email_enabled AND sender_verified;
 IF s.tenant_id IS NULL OR EXISTS(SELECT 1 FROM public.notification_test_runs r WHERE r.tenant_id=tid AND r.last_attempt_at>now()-interval '60 seconds') THEN RETURN false; END IF;
 INSERT INTO public.notification_email_quota VALUES(tid,(now() AT TIME ZONE 'UTC')::date,0) ON CONFLICT DO NOTHING;
 UPDATE public.notification_email_quota q SET reserved=reserved+1 WHERE q.tenant_id=tid AND q.day=(now() AT TIME ZONE 'UTC')::date AND q.reserved<s.daily_limit RETURNING reserved INTO used;
 IF used IS NULL THEN RETURN false; END IF;
 INSERT INTO public.notification_test_runs VALUES(tid,now()) ON CONFLICT(tenant_id) DO UPDATE SET last_attempt_at=now();
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION sf_notification_test_reserve(bigint),sf_login_email_required(bigint),sf_login_email_settings(boolean),sf_login_email_begin(bigint,integer,bigint,uuid,text),sf_login_email_verify(uuid,bigint,integer,bigint,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_login_email_settings(boolean) TO stockflow_runtime;
GRANT EXECUTE ON FUNCTION sf_notification_test_reserve(bigint),sf_login_email_required(bigint),sf_login_email_begin(bigint,integer,bigint,uuid,text),sf_login_email_verify(uuid,bigint,integer,bigint,text) TO stockflow_notification_worker;
COMMIT;
