-- MANUAL ONLY, after 075. Back up, stop writers; run the ENTIRE file as postgres.
-- No historical sends/backfill, no financial changes, no provider credentials in this file.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF to_regprocedure('public.sf_pos_manual_no_gst()') IS NULL OR to_regclass('public.notification_customer_consents') IS NULL THEN RAISE EXCEPTION 'Apply migrations through 075 first'; END IF;
END $$;

CREATE FUNCTION public.sf_wa_manage() RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_transfer_permission('COMPANY_SETTINGS','EDIT') AND public.sf_can_location(public.sf_location()) AND EXISTS(
 SELECT 1 FROM public.users WHERE tenant_id=public.sf_tenant() AND user_id=public.sf_actor() AND is_active AND is_tenant_owner)
$$;
CREATE TABLE public.notification_whatsapp_connections(
 connection_id uuid PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),store_id bigint NOT NULL DEFAULT public.sf_store(),
 business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),generation uuid NOT NULL DEFAULT gen_random_uuid(),
 waba_id varchar(30) NOT NULL CHECK(waba_id ~ '^[0-9]{5,30}$'),phone_id varchar(30) NOT NULL UNIQUE CHECK(phone_id ~ '^[0-9]{5,30}$'),
 display_number varchar(50) NOT NULL,business_name varchar(200) NOT NULL,protected_token text NOT NULL,
 enabled boolean NOT NULL DEFAULT false,billing_confirmed boolean NOT NULL DEFAULT false,invoice_enabled boolean NOT NULL DEFAULT false,payment_enabled boolean NOT NULL DEFAULT false,
 daily_limit integer NOT NULL DEFAULT 100 CHECK(daily_limit BETWEEN 1 AND 10000),connected_at timestamptz NOT NULL DEFAULT now(),disconnected_at timestamptz,
 created_by integer NOT NULL DEFAULT public.sf_actor(),updated_at timestamptz NOT NULL DEFAULT now(),template_check_after timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,connection_id),UNIQUE(tenant_id,store_id,business_profile_code),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id));
CREATE TABLE public.notification_whatsapp_signup(
 ticket uuid PRIMARY KEY,tenant_id bigint NOT NULL DEFAULT public.sf_tenant(),store_id bigint NOT NULL DEFAULT public.sf_store(),
 business_profile_code varchar(40) NOT NULL DEFAULT public.sf_visible_business_profile(),actor_id integer NOT NULL DEFAULT public.sf_actor(),
 expires_at timestamptz NOT NULL DEFAULT now()+interval '10 minutes',consumed boolean NOT NULL DEFAULT false);
CREATE TABLE public.notification_whatsapp_templates(
 tenant_id bigint NOT NULL,connection_id uuid NOT NULL,event varchar(40) NOT NULL CHECK(event IN('invoice.completed','payment.recorded')),
 template_id varchar(30) NOT NULL,template_name varchar(512) NOT NULL,language varchar(20) NOT NULL,body text NOT NULL,
 status varchar(30) NOT NULL CHECK(status IN('APPROVED','UNAVAILABLE')),synced_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,connection_id,event),FOREIGN KEY(tenant_id,connection_id) REFERENCES public.notification_whatsapp_connections(tenant_id,connection_id));
CREATE TABLE public.notification_whatsapp_deliveries(
 id bigserial PRIMARY KEY,request_key uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,tenant_id bigint NOT NULL,connection_id uuid NOT NULL,generation uuid NOT NULL,
 event varchar(40) NOT NULL CHECK(event IN('invoice.completed','payment.recorded')),source_id bigint NOT NULL,sale_id bigint NOT NULL,location_id bigint NOT NULL,customer_id integer NOT NULL,
 recipient varchar(15) NOT NULL,reference varchar(100) NOT NULL,parameters jsonb NOT NULL CHECK(jsonb_typeof(parameters)='array' AND jsonb_array_length(parameters)=3),
 template_name varchar(512) NOT NULL,language varchar(20) NOT NULL,
 status varchar(12) NOT NULL DEFAULT 'PENDING' CHECK(status IN('PENDING','RETRY','LEASED','ACCEPTED','DELIVERED','READ','FAILED','UNKNOWN','SKIPPED')),
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),next_attempt_at timestamptz NOT NULL DEFAULT now(),lease_token uuid,lease_until timestamptz,
 provider_id varchar(250),diagnostic varchar(250),created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,event,source_id),FOREIGN KEY(tenant_id,connection_id) REFERENCES public.notification_whatsapp_connections(tenant_id,connection_id),
 FOREIGN KEY(tenant_id,customer_id) REFERENCES public.customers(tenant_id,customer_id),FOREIGN KEY(tenant_id,location_id,sale_id) REFERENCES public.sales(tenant_id,location_id,sale_id));
CREATE INDEX ix_wa_pending ON public.notification_whatsapp_deliveries(next_attempt_at,id) WHERE status IN('PENDING','RETRY','LEASED');
CREATE INDEX ix_wa_provider ON public.notification_whatsapp_deliveries(connection_id,provider_id);
CREATE TABLE public.notification_whatsapp_quota(tenant_id bigint NOT NULL,connection_id uuid NOT NULL,day date NOT NULL,reserved integer NOT NULL DEFAULT 0,
 PRIMARY KEY(tenant_id,connection_id,day),FOREIGN KEY(tenant_id,connection_id) REFERENCES public.notification_whatsapp_connections(tenant_id,connection_id));
-- Persist status callbacks even when they race the HTTP send acknowledgement.
CREATE TABLE public.notification_whatsapp_receipts(phone_id varchar(30) NOT NULL,message_id varchar(250) NOT NULL,status varchar(12) NOT NULL,
 request_key uuid,received_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(phone_id,message_id,status));
CREATE TABLE public.notification_whatsapp_suppressions(tenant_id bigint NOT NULL,recipient varchar(15) NOT NULL,received_at timestamptz NOT NULL DEFAULT now(),
 message_id varchar(250) NOT NULL,PRIMARY KEY(tenant_id,recipient));
CREATE TABLE public.notification_whatsapp_audit(id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,store_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,
 actor_id integer NOT NULL,recorded_at timestamptz NOT NULL DEFAULT now(),before_value jsonb NOT NULL,after_value jsonb NOT NULL);

DO $$ DECLARE tab text; BEGIN FOREACH tab IN ARRAY ARRAY['notification_whatsapp_connections','notification_whatsapp_signup','notification_whatsapp_templates','notification_whatsapp_deliveries','notification_whatsapp_quota','notification_whatsapp_receipts','notification_whatsapp_suppressions','notification_whatsapp_audit'] LOOP
 EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab);EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab);
 END LOOP; END $$;
-- No runtime table writes or ciphertext reads. Narrow definer functions authorize mutations.
CREATE POLICY wa_connection_read ON public.notification_whatsapp_connections FOR SELECT USING(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_wa_manage());
GRANT SELECT(connection_id,tenant_id,store_id,business_profile_code,generation,waba_id,phone_id,display_number,business_name,enabled,billing_confirmed,invoice_enabled,payment_enabled,daily_limit,connected_at,disconnected_at,updated_at) ON public.notification_whatsapp_connections TO stockflow_runtime;
CREATE POLICY wa_template_read ON public.notification_whatsapp_templates FOR SELECT USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.notification_whatsapp_connections c WHERE c.connection_id=notification_whatsapp_templates.connection_id));
CREATE POLICY wa_delivery_read ON public.notification_whatsapp_deliveries FOR SELECT USING(tenant_id=public.sf_tenant() AND public.sf_can_location(location_id) AND EXISTS(SELECT 1 FROM public.notification_whatsapp_connections c WHERE c.connection_id=notification_whatsapp_deliveries.connection_id));
GRANT SELECT ON public.notification_whatsapp_templates,public.notification_whatsapp_deliveries TO stockflow_runtime;
CREATE POLICY wa_audit_read ON public.notification_whatsapp_audit FOR SELECT USING(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_wa_manage());
GRANT SELECT ON public.notification_whatsapp_audit TO stockflow_runtime;
CREATE FUNCTION public.sf_wa_audit() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 -- Provider-maintenance updates do not create misleading owner actions.
 IF TG_OP='INSERT' OR (to_jsonb(NEW)-'template_check_after') IS DISTINCT FROM (to_jsonb(OLD)-'template_check_after') THEN
 INSERT INTO public.notification_whatsapp_audit(tenant_id,store_id,business_profile_code,actor_id,before_value,after_value)
 VALUES(NEW.tenant_id,NEW.store_id,NEW.business_profile_code,public.sf_actor(),CASE WHEN TG_OP='INSERT' THEN '{}'::jsonb ELSE to_jsonb(OLD)-'protected_token' END,to_jsonb(NEW)-'protected_token');
 END IF;RETURN NEW;END $$;
CREATE TRIGGER whatsapp_connection_audit AFTER INSERT OR UPDATE ON public.notification_whatsapp_connections FOR EACH ROW EXECUTE FUNCTION public.sf_wa_audit();

CREATE FUNCTION public.sf_wa_signup_begin() RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE ticket uuid:=gen_random_uuid(); BEGIN
 IF NOT public.sf_wa_manage() OR public.sf_store()<=0 THEN RAISE EXCEPTION 'Choose an authorized shop as tenant owner' USING ERRCODE='42501'; END IF;
 DELETE FROM public.notification_whatsapp_signup WHERE expires_at<now();
 UPDATE public.notification_whatsapp_signup SET consumed=true WHERE tenant_id=public.sf_tenant() AND actor_id=public.sf_actor();
 INSERT INTO public.notification_whatsapp_signup(ticket) VALUES(ticket); RETURN ticket;
END $$;
CREATE FUNCTION public.sf_wa_signup_consume(id uuid) RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NOT public.sf_wa_manage() THEN RETURN false; END IF;
 UPDATE public.notification_whatsapp_signup SET consumed=true WHERE ticket=id AND tenant_id=public.sf_tenant() AND actor_id=public.sf_actor() AND store_id=public.sf_store()
 AND business_profile_code=public.sf_visible_business_profile() AND NOT consumed AND expires_at>now(); RETURN FOUND;
END $$;
CREATE FUNCTION public.sf_wa_connect(id uuid,waba text,phone text,number text,name text,ciphertext text) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NOT public.sf_wa_manage() OR length(ciphertext)<20 OR length(ciphertext)>20000 THEN RAISE EXCEPTION 'Owner connection permission required' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('wa-account:'||waba,0));
 IF EXISTS(SELECT 1 FROM public.notification_whatsapp_connections WHERE waba_id=waba AND tenant_id<>public.sf_tenant()) THEN RAISE EXCEPTION 'WhatsApp account is already assigned to another business' USING ERRCODE='23514'; END IF;
 INSERT INTO public.notification_whatsapp_connections(connection_id,waba_id,phone_id,display_number,business_name,protected_token) VALUES(id,waba,phone,left(number,50),left(name,200),ciphertext)
 ON CONFLICT(tenant_id,store_id,business_profile_code) DO UPDATE SET connection_id=notification_whatsapp_connections.connection_id,
 waba_id=EXCLUDED.waba_id,phone_id=EXCLUDED.phone_id,display_number=EXCLUDED.display_number,business_name=EXCLUDED.business_name,protected_token=EXCLUDED.protected_token,
 generation=gen_random_uuid(),enabled=false,billing_confirmed=false,invoice_enabled=false,payment_enabled=false,disconnected_at=NULL,connected_at=now(),updated_at=now();
 -- Reconnect always requires refreshed approvals and deliberate re-enabling.
 DELETE FROM public.notification_whatsapp_templates t USING public.notification_whatsapp_connections c WHERE t.connection_id=c.connection_id AND c.tenant_id=public.sf_tenant() AND c.store_id=public.sf_store() AND c.business_profile_code=public.sf_visible_business_profile();
END $$;
CREATE FUNCTION public.sf_wa_secret() RETURNS TABLE(connection_id uuid,waba_id text,phone_id text,protected_token text) LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT c.connection_id,c.waba_id::text,c.phone_id::text,c.protected_token FROM public.notification_whatsapp_connections c WHERE public.sf_wa_manage() AND c.tenant_id=public.sf_tenant() AND c.store_id=public.sf_store() AND c.business_profile_code=public.sf_visible_business_profile() AND c.disconnected_at IS NULL FOR SHARE
$$;
CREATE FUNCTION public.sf_wa_settings(active boolean,billing boolean,invoices boolean,payments boolean,quota integer) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NOT public.sf_wa_manage() THEN RAISE EXCEPTION 'Owner permission required' USING ERRCODE='42501'; END IF;
 IF active AND (NOT billing OR NOT (invoices OR payments)) THEN RAISE EXCEPTION 'Confirm Meta billing and choose a message type' USING ERRCODE='23514'; END IF;
 IF active AND EXISTS(SELECT 1 FROM (VALUES('invoice.completed',invoices),('payment.recorded',payments)) x(event,enabled)
 WHERE x.enabled AND NOT EXISTS(SELECT 1 FROM public.notification_whatsapp_templates t JOIN public.notification_whatsapp_connections c USING(tenant_id,connection_id)
 WHERE c.tenant_id=public.sf_tenant() AND c.store_id=public.sf_store() AND c.business_profile_code=public.sf_visible_business_profile() AND t.event=x.event AND t.status='APPROVED' AND t.synced_at>now()-interval '24 hours')) THEN
 RAISE EXCEPTION 'Sync an approved template for each selected message type' USING ERRCODE='23514'; END IF;
 UPDATE public.notification_whatsapp_connections SET enabled=active,billing_confirmed=billing,invoice_enabled=invoices,payment_enabled=payments,daily_limit=quota,updated_at=now()
 WHERE tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND disconnected_at IS NULL;
 IF NOT FOUND THEN RAISE EXCEPTION 'Connect WhatsApp first' USING ERRCODE='23514'; END IF;
END $$;
CREATE FUNCTION public.sf_wa_disconnect() RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NOT public.sf_wa_manage() THEN RAISE EXCEPTION 'Owner permission required' USING ERRCODE='42501'; END IF;
 UPDATE public.notification_whatsapp_connections SET protected_token='',enabled=false,billing_confirmed=false,generation=gen_random_uuid(),disconnected_at=now(),updated_at=now()
 WHERE tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile();
END $$;
CREATE FUNCTION public.sf_wa_template(event_name text,id text,name text,lang text,content text,approved boolean) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE cid uuid; BEGIN
 IF NOT public.sf_wa_manage() THEN RAISE EXCEPTION 'Owner permission required' USING ERRCODE='42501'; END IF;
 SELECT connection_id INTO STRICT cid FROM public.sf_wa_secret();
 INSERT INTO public.notification_whatsapp_templates(tenant_id,connection_id,event,template_id,template_name,language,body,status)
 VALUES(public.sf_tenant(),cid,event_name,id,name,lang,content,CASE WHEN approved THEN 'APPROVED' ELSE 'UNAVAILABLE' END)
 ON CONFLICT(tenant_id,connection_id,event) DO UPDATE SET template_id=EXCLUDED.template_id,template_name=EXCLUDED.template_name,language=EXCLUDED.language,body=EXCLUDED.body,status=EXCLUDED.status,synced_at=now();
 IF approved THEN UPDATE public.notification_whatsapp_deliveries d SET next_attempt_at=now(),diagnostic=NULL WHERE d.connection_id=cid AND d.event=event_name AND d.template_name=name AND d.language=lang
 AND d.status IN('PENDING','RETRY') AND d.diagnostic='Refresh template approval in WhatsApp settings.'; END IF;
END $$;
-- Initial release: Indian ten-digit mobiles, or explicit international numbers (8-15 digits).
CREATE FUNCTION public.sf_wa_phone(mobile text) RETURNS text LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,public AS $$
 SELECT CASE WHEN mobile ~ '^\s*[0-9]{10}\s*$' THEN '91'||btrim(mobile)
 WHEN btrim(mobile) ~ '^\+[1-9][0-9]{7,14}$' THEN substring(btrim(mobile) FROM 2) ELSE NULL END
$$;
CREATE FUNCTION public.sf_wa_capture() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE sale public.sales;c public.notification_whatsapp_connections;t public.notification_whatsapp_templates;recipient text;kind text;ref text;amount numeric;source bigint;params jsonb; BEGIN
 IF TG_TABLE_NAME='sales' THEN
  SELECT * INTO sale FROM public.sales s WHERE s.tenant_id=NEW.tenant_id AND s.sale_id=NEW.sale_id;
  IF sale.status<>'COMPLETED' OR (TG_OP='UPDATE' AND OLD.status='COMPLETED') THEN RETURN NEW; END IF;
  kind:='invoice.completed';source:=sale.sale_id;ref:=sale.invoice_number;amount:=sale.total_amount;
 ELSE
  SELECT * INTO sale FROM public.sales s WHERE s.tenant_id=NEW.tenant_id AND s.sale_id=NEW.sale_id;
  IF sale.status<>'COMPLETED' OR NEW.amount<=0 THEN RETURN NEW; END IF;
  kind:='payment.recorded';source:=NEW.payment_id;ref:=NEW.payment_id::text;amount:=NEW.amount;
 END IF;
 IF sale.sale_id IS NULL OR sale.customer_id IS NULL THEN RETURN NEW; END IF;
 SELECT * INTO c FROM public.notification_whatsapp_connections x WHERE x.tenant_id=sale.tenant_id AND x.store_id=sale.store_id AND x.business_profile_code=sale.business_profile_code AND x.enabled AND x.billing_confirmed AND x.disconnected_at IS NULL;
 IF c.connection_id IS NULL OR (kind='invoice.completed' AND NOT c.invoice_enabled) OR (kind='payment.recorded' AND NOT c.payment_enabled) THEN RETURN NEW; END IF;
 SELECT * INTO t FROM public.notification_whatsapp_templates x WHERE x.tenant_id=c.tenant_id AND x.connection_id=c.connection_id AND x.event=kind AND x.status='APPROVED';
 IF t.template_id IS NULL THEN RETURN NEW; END IF;
 SELECT public.sf_wa_phone(cust.mobile_number) INTO recipient FROM public.customers cust JOIN public.notification_customer_consents consent USING(tenant_id,customer_id)
 WHERE cust.tenant_id=sale.tenant_id AND cust.customer_id=sale.customer_id AND cust.is_active AND consent.channel='whatsapp' AND consent.allowed
 AND NOT EXISTS(SELECT 1 FROM public.notification_whatsapp_suppressions s WHERE s.tenant_id=cust.tenant_id AND s.recipient=public.sf_wa_phone(cust.mobile_number) AND s.received_at>=consent.updated_at);
 IF recipient IS NULL THEN RETURN NEW; END IF;
 IF kind='invoice.completed' THEN params:=jsonb_build_array(ref,to_char(amount,'FM999999999999990.00'),c.business_name);
 ELSE params:=jsonb_build_array(ref,to_char(amount,'FM999999999999990.00'),sale.invoice_number); END IF;
 INSERT INTO public.notification_whatsapp_deliveries(tenant_id,connection_id,generation,event,source_id,sale_id,location_id,customer_id,recipient,reference,parameters,template_name,language)
 VALUES(sale.tenant_id,c.connection_id,c.generation,kind,source,sale.sale_id,sale.location_id,sale.customer_id,recipient,ref,params,t.template_name,t.language) ON CONFLICT DO NOTHING;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER whatsapp_invoice_capture AFTER INSERT OR UPDATE ON public.sales DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.sf_wa_capture();
CREATE CONSTRAINT TRIGGER whatsapp_payment_capture AFTER INSERT ON public.payments DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.sf_wa_capture();

CREATE FUNCTION public.sf_wa_refresh_claim() RETURNS TABLE(connection_id uuid,tenant_id bigint,generation uuid,waba_id text,phone_id text,protected_token text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE c public.notification_whatsapp_connections; BEGIN
 SELECT * INTO c FROM public.notification_whatsapp_connections x WHERE x.enabled AND x.disconnected_at IS NULL AND x.template_check_after<=now() FOR UPDATE SKIP LOCKED LIMIT 1;
 IF NOT FOUND THEN RETURN; END IF;
 UPDATE public.notification_whatsapp_connections x SET template_check_after=now()+interval '10 minutes' WHERE x.connection_id=c.connection_id;
 RETURN QUERY SELECT c.connection_id,c.tenant_id,c.generation,c.waba_id::text,c.phone_id::text,c.protected_token;
END $$;
CREATE FUNCTION public.sf_wa_refresh_finish(id uuid,version uuid,templates jsonb) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 PERFORM 1 FROM public.notification_whatsapp_connections c WHERE c.connection_id=id AND c.generation=version AND c.disconnected_at IS NULL FOR UPDATE;
 IF NOT FOUND THEN RETURN; END IF;
 UPDATE public.notification_whatsapp_templates SET status='UNAVAILABLE',synced_at=now() WHERE connection_id=id;
 UPDATE public.notification_whatsapp_templates t SET status=CASE WHEN x->>'Status'='APPROVED' THEN 'APPROVED' ELSE 'UNAVAILABLE' END,synced_at=now()
 FROM jsonb_array_elements(templates) x WHERE t.connection_id=id AND t.template_id=x->>'Id' AND t.template_name=x->>'Name' AND t.language=x->>'Language' AND t.body=x->>'Body';
 UPDATE public.notification_whatsapp_deliveries d SET next_attempt_at=now(),diagnostic=NULL WHERE d.connection_id=sf_wa_refresh_finish.id AND d.status IN('PENDING','RETRY') AND d.diagnostic='Refresh template approval in WhatsApp settings.'
 AND EXISTS(SELECT 1 FROM public.notification_whatsapp_templates t WHERE t.connection_id=sf_wa_refresh_finish.id AND t.event=d.event AND t.template_name=d.template_name AND t.language=d.language AND t.status='APPROVED');
 UPDATE public.notification_whatsapp_connections c SET template_check_after=now()+interval '1 hour' WHERE c.connection_id=id;
END $$;
CREATE FUNCTION public.sf_wa_claim() RETURNS TABLE(id bigint,tenant_id bigint,connection_id uuid,token uuid,request_key uuid,phone_id text,protected_token text,recipient text,template_name text,language text,parameters text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE j public.notification_whatsapp_deliveries;c public.notification_whatsapp_connections;lease uuid;reserved integer; BEGIN
 UPDATE public.notification_whatsapp_deliveries SET status='UNKNOWN',diagnostic='Expired send lease; do not resend without provider reconciliation.',updated_at=now() WHERE status='LEASED' AND lease_until<now();
 FOR j IN SELECT d.* FROM public.notification_whatsapp_deliveries d WHERE d.status IN('PENDING','RETRY') AND d.next_attempt_at<=now() AND d.attempts<5 ORDER BY d.id FOR UPDATE SKIP LOCKED LIMIT 25 LOOP
  SELECT * INTO c FROM public.notification_whatsapp_connections x WHERE x.connection_id=j.connection_id;
  IF NOT c.enabled OR NOT c.billing_confirmed OR c.disconnected_at IS NOT NULL OR c.generation<>j.generation
   OR (j.event='invoice.completed' AND NOT c.invoice_enabled) OR (j.event='payment.recorded' AND NOT c.payment_enabled)
   OR NOT EXISTS(SELECT 1 FROM public.tenants WHERE tenants.tenant_id=j.tenant_id AND is_active)
   OR NOT EXISTS(SELECT 1 FROM public.stores s JOIN public.store_locations l USING(tenant_id,store_id) WHERE s.tenant_id=j.tenant_id AND s.store_id=c.store_id AND s.is_active AND l.location_id=j.location_id AND l.is_active)
   OR NOT EXISTS(SELECT 1 FROM public.sales s WHERE s.tenant_id=j.tenant_id AND s.sale_id=j.sale_id AND s.status='COMPLETED')
   OR NOT EXISTS(SELECT 1 FROM public.customers cust JOIN public.notification_customer_consents consent USING(tenant_id,customer_id) WHERE cust.tenant_id=j.tenant_id AND cust.customer_id=j.customer_id AND cust.is_active AND public.sf_wa_phone(cust.mobile_number)=j.recipient AND consent.channel='whatsapp' AND consent.allowed AND NOT EXISTS(SELECT 1 FROM public.notification_whatsapp_suppressions s WHERE s.tenant_id=j.tenant_id AND s.recipient=j.recipient AND s.received_at>=consent.updated_at))
   THEN UPDATE public.notification_whatsapp_deliveries d SET status='SKIPPED',diagnostic='Connection, source, recipient or consent changed.',updated_at=now() WHERE d.id=j.id;CONTINUE; END IF;
  -- Stale approvals pause queued jobs until the owner syncs; never substitute another template.
  IF NOT EXISTS(SELECT 1 FROM public.notification_whatsapp_templates t WHERE t.connection_id=c.connection_id AND t.event=j.event AND t.template_name=j.template_name AND t.language=j.language AND t.status='APPROVED' AND t.synced_at>now()-interval '24 hours') THEN
   UPDATE public.notification_whatsapp_deliveries d SET next_attempt_at=now()+interval '15 minutes',diagnostic='Refresh template approval in WhatsApp settings.' WHERE d.id=j.id;CONTINUE; END IF;
  INSERT INTO public.notification_whatsapp_quota VALUES(j.tenant_id,j.connection_id,(now() AT TIME ZONE 'UTC')::date,0) ON CONFLICT DO NOTHING;
  UPDATE public.notification_whatsapp_quota q SET reserved=q.reserved+1 WHERE q.tenant_id=j.tenant_id AND q.connection_id=j.connection_id AND q.day=(now() AT TIME ZONE 'UTC')::date AND q.reserved<c.daily_limit RETURNING q.reserved INTO reserved;
  IF reserved IS NULL THEN UPDATE public.notification_whatsapp_deliveries d SET next_attempt_at=date_trunc('day',now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'+interval '1 day',diagnostic='Daily send limit reached.' WHERE d.id=j.id;CONTINUE; END IF;
  lease:=gen_random_uuid();UPDATE public.notification_whatsapp_deliveries d SET status='LEASED',attempts=d.attempts+1,lease_token=lease,lease_until=now()+interval '2 minutes',updated_at=now(),diagnostic=NULL WHERE d.id=j.id;
  RETURN QUERY SELECT j.id,j.tenant_id,j.connection_id,lease,j.request_key,c.phone_id::text,c.protected_token,j.recipient::text,j.template_name::text,j.language::text,j.parameters::text;RETURN;
 END LOOP;
END $$;
CREATE FUNCTION public.sf_wa_finish(job bigint,lease uuid,result text,provider text,detail text,delay_seconds integer DEFAULT 300) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF result NOT IN('ACCEPTED','RETRY','UNKNOWN','FAILED','SKIPPED') THEN RAISE EXCEPTION 'Invalid WhatsApp delivery result'; END IF;
 UPDATE public.notification_whatsapp_deliveries d SET status=CASE WHEN result='RETRY' AND attempts>=5 THEN 'FAILED' ELSE result END,provider_id=left(provider,250),diagnostic=left(detail,250),lease_until=NULL,updated_at=now(),next_attempt_at=now()+make_interval(secs=>greatest(60,least(delay_seconds,86400)))
 WHERE d.id=job AND d.lease_token=lease AND d.status='LEASED';
 -- Reconcile receipts received before the send response (also fenced by sender).
 UPDATE public.notification_whatsapp_deliveries d SET status=r.status,diagnostic=NULL,updated_at=now() FROM public.notification_whatsapp_connections c,
 LATERAL(SELECT x.status FROM public.notification_whatsapp_receipts x WHERE x.phone_id=c.phone_id AND (x.message_id=provider OR x.request_key=(SELECT request_key FROM public.notification_whatsapp_deliveries WHERE id=job)) ORDER BY CASE x.status WHEN 'READ' THEN 4 WHEN 'DELIVERED' THEN 3 WHEN 'FAILED' THEN 2 ELSE 1 END DESC LIMIT 1) r
 WHERE d.id=job AND d.connection_id=c.connection_id AND d.lease_token=lease AND d.status IN('ACCEPTED','UNKNOWN');
END $$;
CREATE FUNCTION public.sf_wa_status(phone text,message text,state text,correlation uuid) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF state NOT IN('ACCEPTED','DELIVERED','READ','FAILED') OR length(message)>250 THEN RETURN; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.notification_whatsapp_connections WHERE phone_id=phone AND disconnected_at IS NULL) THEN RETURN; END IF;
 INSERT INTO public.notification_whatsapp_receipts(phone_id,message_id,status,request_key) VALUES(phone,message,state,correlation) ON CONFLICT DO NOTHING;
 UPDATE public.notification_whatsapp_deliveries d SET status=state,provider_id=message,updated_at=now(),diagnostic=CASE WHEN state='FAILED' THEN 'Meta reported delivery failure; review WhatsApp Manager.' ELSE NULL END
 FROM public.notification_whatsapp_connections c WHERE d.connection_id=c.connection_id AND c.phone_id=phone AND (d.provider_id=message OR d.request_key=correlation)
 AND d.status IN('LEASED','UNKNOWN','ACCEPTED','DELIVERED','FAILED') AND (d.status<>'FAILED' OR state IN('DELIVERED','READ')) AND (state<>'ACCEPTED' OR d.status IN('LEASED','UNKNOWN')) AND (state<>'FAILED' OR d.status<>'DELIVERED');
END $$;
CREATE FUNCTION public.sf_wa_optout(phone text,sender text,message text) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF sender !~ '^[1-9][0-9]{7,14}$' OR length(message)>250 THEN RETURN; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.notification_whatsapp_connections WHERE phone_id=phone AND disconnected_at IS NULL) THEN RETURN; END IF;
 INSERT INTO public.notification_whatsapp_receipts(phone_id,message_id,status) VALUES(phone,message,'OPTOUT') ON CONFLICT DO NOTHING;
 IF NOT FOUND THEN RETURN; END IF;
 INSERT INTO public.notification_whatsapp_suppressions(tenant_id,recipient,message_id)
 SELECT tenant_id,sender,message FROM public.notification_whatsapp_connections WHERE phone_id=phone AND disconnected_at IS NULL
 ON CONFLICT(tenant_id,recipient) DO UPDATE SET received_at=now(),message_id=EXCLUDED.message_id WHERE notification_whatsapp_suppressions.message_id<>EXCLUDED.message_id;
END $$;
REVOKE ALL ON FUNCTION public.sf_wa_manage(),public.sf_wa_signup_begin(),public.sf_wa_signup_consume(uuid),public.sf_wa_connect(uuid,text,text,text,text,text),public.sf_wa_secret(),public.sf_wa_settings(boolean,boolean,boolean,boolean,integer),public.sf_wa_disconnect(),public.sf_wa_template(text,text,text,text,text,boolean),public.sf_wa_phone(text),public.sf_wa_capture(),public.sf_wa_claim(),public.sf_wa_finish(bigint,uuid,text,text,text,integer),public.sf_wa_status(text,text,text,uuid),public.sf_wa_optout(text,text,text) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.sf_wa_audit(),public.sf_wa_refresh_claim(),public.sf_wa_refresh_finish(uuid,uuid,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_wa_manage(),public.sf_wa_signup_begin(),public.sf_wa_signup_consume(uuid),public.sf_wa_connect(uuid,text,text,text,text,text),public.sf_wa_secret(),public.sf_wa_settings(boolean,boolean,boolean,boolean,integer),public.sf_wa_disconnect(),public.sf_wa_template(text,text,text,text,text,boolean) TO stockflow_runtime;
GRANT EXECUTE ON FUNCTION public.sf_wa_claim(),public.sf_wa_finish(bigint,uuid,text,text,text,integer),public.sf_wa_status(text,text,text,uuid),public.sf_wa_optout(text,text,text) TO stockflow_notification_worker;
GRANT EXECUTE ON FUNCTION public.sf_wa_refresh_claim(),public.sf_wa_refresh_finish(uuid,uuid,jsonb) TO stockflow_notification_worker;
COMMIT;
