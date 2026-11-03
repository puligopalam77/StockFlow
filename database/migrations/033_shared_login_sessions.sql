-- MANUAL: stop StockFlow, back up, execute once after 032 as PostgreSQL superuser, then restart all instances.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.brand_categories') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 032 first'; END IF;
END $$;
CREATE TABLE auth_sessions(
 session_id uuid PRIMARY KEY,
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),user_id integer NOT NULL,
 security_version bigint NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),last_activity_at timestamptz NOT NULL DEFAULT now(),
 idle_expires_at timestamptz NOT NULL,absolute_expires_at timestamptz NOT NULL,
 revoked_at timestamptz,revocation_reason varchar(40),
 ip_address varchar(64) NOT NULL,user_agent varchar(512) NOT NULL,audit_reference varchar(64) NOT NULL,
 FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id),
 CHECK(idle_expires_at<=absolute_expires_at),CHECK(absolute_expires_at>created_at)
);
CREATE INDEX ix_auth_sessions_owner ON auth_sessions(tenant_id,user_id,created_at DESC);
CREATE INDEX ix_auth_sessions_expiry ON auth_sessions(idle_expires_at,absolute_expires_at) WHERE revoked_at IS NULL;
ALTER TABLE auth_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE auth_sessions FORCE ROW LEVEL SECURITY;
CREATE POLICY session_owner ON auth_sessions USING(tenant_id=sf_tenant() AND user_id=COALESCE(NULLIF(current_setting('app.user_id',true),''),'0')::integer)
 WITH CHECK(tenant_id=sf_tenant() AND user_id=COALESCE(NULLIF(current_setting('app.user_id',true),''),'0')::integer);
GRANT SELECT,INSERT,UPDATE ON auth_sessions TO stockflow_runtime;
-- Application-wide keys are never exposed through HTTP. Protect database backups.
CREATE TABLE auth_protection_keys(key_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,friendly_name text NOT NULL UNIQUE,xml text NOT NULL,created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE auth_login_limits(bucket varchar(64) PRIMARY KEY,window_started_at timestamptz NOT NULL,attempts integer NOT NULL);
ALTER TABLE auth_protection_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE auth_protection_keys FORCE ROW LEVEL SECURITY;
ALTER TABLE auth_login_limits ENABLE ROW LEVEL SECURITY;
ALTER TABLE auth_login_limits FORCE ROW LEVEL SECURITY;
CREATE FUNCTION sf_read_protection_keys() RETURNS SETOF text LANGUAGE sql SECURITY DEFINER SET search_path=public,pg_temp AS $$ SELECT xml FROM auth_protection_keys ORDER BY key_id $$;
CREATE FUNCTION sf_write_protection_key(p_name text,p_xml text) RETURNS void LANGUAGE sql SECURITY DEFINER SET search_path=public,pg_temp AS $$ INSERT INTO auth_protection_keys(friendly_name,xml) VALUES(p_name,p_xml) ON CONFLICT(friendly_name) DO NOTHING $$;
CREATE FUNCTION sf_take_login_attempt(p_bucket text,p_limit integer,p_seconds integer) RETURNS integer LANGUAGE plpgsql SECURITY DEFINER SET search_path=public,pg_temp AS $$
DECLARE entry auth_login_limits; stamp timestamptz:=clock_timestamp();
BEGIN
 IF length(p_bucket)<>64 OR p_limit<1 OR p_limit>10000 OR p_seconds<1 OR p_seconds>86400 THEN RAISE EXCEPTION 'Invalid limiter parameters'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('login-limit:'||p_bucket,0));
 INSERT INTO auth_login_limits VALUES(p_bucket,stamp,0) ON CONFLICT DO NOTHING;
 SELECT * INTO entry FROM auth_login_limits WHERE bucket=p_bucket FOR UPDATE;
 IF entry.window_started_at+make_interval(secs=>p_seconds)<=stamp THEN
  UPDATE auth_login_limits SET window_started_at=stamp,attempts=1 WHERE bucket=p_bucket; RETURN 0;
 END IF;
 IF entry.attempts>=p_limit THEN RETURN GREATEST(1,ceil(extract(epoch FROM entry.window_started_at+make_interval(secs=>p_seconds)-stamp))::integer); END IF;
 UPDATE auth_login_limits SET attempts=attempts+1 WHERE bucket=p_bucket; RETURN 0;
END $$;
CREATE FUNCTION sf_expire_auth_sessions() RETURNS integer LANGUAGE plpgsql SECURITY DEFINER SET search_path=public,pg_temp AS $$
DECLARE total integer;
BEGIN
 WITH candidates AS (
  SELECT s.session_id FROM auth_sessions s JOIN users u USING(tenant_id,user_id) JOIN roles r ON r.role_id=u.role_id
  WHERE s.revoked_at IS NULL AND (s.idle_expires_at<=now() OR s.absolute_expires_at<=now() OR s.security_version<>u.security_version OR NOT u.is_active OR NOT r.is_active)
  ORDER BY s.idle_expires_at LIMIT 1000 FOR UPDATE OF s SKIP LOCKED
 ), ended AS (
  UPDATE auth_sessions s SET revoked_at=now(),revocation_reason=CASE
   WHEN s.absolute_expires_at<=now() THEN 'ABSOLUTE_TIMEOUT'
   WHEN s.idle_expires_at<=now() THEN 'IDLE_TIMEOUT' ELSE 'SECURITY_CHANGED' END
  FROM candidates c WHERE s.session_id=c.session_id RETURNING s.*
 )
 INSERT INTO audit_logs(tenant_id,user_id,module_name,action_name,table_name,record_id,reason,new_values)
 SELECT tenant_id,user_id,'Security','SESSION_ENDED','auth_sessions',user_id,revocation_reason,
  jsonb_build_object('sessionReference',audit_reference,'ipAddress',ip_address,'device',user_agent,'reason',revocation_reason) FROM ended;
 GET DIAGNOSTICS total=ROW_COUNT;
 DELETE FROM auth_login_limits WHERE window_started_at<now()-interval '1 day';
 DELETE FROM auth_sessions WHERE revoked_at<now()-interval '30 days';
 RETURN total;
END $$;
REVOKE ALL ON FUNCTION sf_read_protection_keys(),sf_write_protection_key(text,text),sf_take_login_attempt(text,integer,integer),sf_expire_auth_sessions() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_read_protection_keys(),sf_write_protection_key(text,text),sf_take_login_attempt(text,integer,integer),sf_expire_auth_sessions() TO stockflow_runtime;
COMMIT;
