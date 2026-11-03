-- MANUAL ONLY: stop StockFlow, back up, apply once after 043 as PostgreSQL superuser.
-- Login OTP now depends ONLY on each staff account's saved checkbox.
-- Existing individual selections remain unchanged. Shop-wide and request-time opt-in are retired.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='users' AND column_name='require_login_email_otp') THEN RAISE EXCEPTION 'Apply 043 first'; END IF;
END $$;
UPDATE public.notification_delivery_settings SET login_email_otp=false;
CREATE OR REPLACE FUNCTION sf_login_email_user_required(tid bigint,uid integer) RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT COALESCE((SELECT u.require_login_email_otp FROM public.users u
 JOIN public.roles r ON r.tenant_id=u.tenant_id AND r.role_id=u.role_id
 JOIN public.tenants t ON t.tenant_id=u.tenant_id
 WHERE u.tenant_id=tid AND u.user_id=uid AND u.is_active AND r.is_active AND t.is_active),false)
$$;
CREATE OR REPLACE FUNCTION sf_login_email_begin(tid bigint,uid integer,security_stamp bigint,challenge uuid,hash text,requested boolean)
RETURNS TABLE(recipient text,sender_email text,sender_name text) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE s public.notification_delivery_settings; addr text; quota integer;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('login-email:'||tid||':'||uid,0));
 SELECT * INTO s FROM public.notification_delivery_settings WHERE tenant_id=tid AND email_enabled AND sender_verified;
 IF s.tenant_id IS NULL OR NOT public.sf_login_email_user_required(tid,uid) THEN RETURN; END IF;
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


COMMIT;
