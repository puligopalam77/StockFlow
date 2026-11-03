-- MANUAL ONLY: stop StockFlow, back up, apply ONCE after 039 as PostgreSQL superuser.
-- No historical customer messages; new options default OFF. Secrets stay in server configuration.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.notifications') IS NULL THEN RAISE EXCEPTION 'Apply 039 first'; END IF;
END $$;
CREATE TABLE notification_delivery_settings(
 tenant_id bigint PRIMARY KEY DEFAULT sf_tenant() REFERENCES tenants,
 digest_enabled boolean NOT NULL DEFAULT false,digest_hour integer NOT NULL DEFAULT 9 CHECK(digest_hour BETWEEN 0 AND 23),
 time_zone varchar(100) NOT NULL DEFAULT 'Asia/Kolkata',email_enabled boolean NOT NULL DEFAULT false,
 sender_email varchar(150) NOT NULL DEFAULT '',sender_name varchar(100) NOT NULL DEFAULT 'StockFlow',sender_verified boolean NOT NULL DEFAULT false,
 administrator_emails varchar(1000) NOT NULL DEFAULT '',daily_limit integer NOT NULL DEFAULT 250 CHECK(daily_limit BETWEEN 1 AND 300),
 invoice_emails boolean NOT NULL DEFAULT false,repair_emails boolean NOT NULL DEFAULT false,warranty_emails boolean NOT NULL DEFAULT false,failure_alerts boolean NOT NULL DEFAULT false,
 updated_by integer NOT NULL DEFAULT sf_actor(),updated_at timestamptz NOT NULL DEFAULT now(),FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id));
ALTER TABLE notification_delivery_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_delivery_settings FORCE ROW LEVEL SECURITY;
CREATE POLICY delivery_settings_read ON notification_delivery_settings FOR SELECT USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','VIEW'));
CREATE POLICY delivery_settings_write ON notification_delivery_settings FOR ALL USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','EDIT')) WITH CHECK(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','EDIT'));
GRANT SELECT,INSERT,UPDATE ON notification_delivery_settings TO stockflow_runtime;
CREATE TABLE notification_digest_runs(tenant_id bigint NOT NULL,location_id bigint NOT NULL,day date NOT NULL,low_count integer NOT NULL,out_count integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(tenant_id,location_id,day),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id));
CREATE TABLE notification_email_deliveries(
 id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,outbox_id bigint NOT NULL,customer_id integer,
 recipient varchar(150) NOT NULL,title varchar(100) NOT NULL,body text NOT NULL CHECK(length(body)<=20000),
 request_key uuid NOT NULL DEFAULT gen_random_uuid(),status varchar(12) NOT NULL DEFAULT 'PENDING' CHECK(status IN('PENDING','RETRY','LEASED','ACCEPTED','FAILED','UNKNOWN','SKIPPED')),
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),next_attempt_at timestamptz NOT NULL DEFAULT now(),
 lease_token uuid,lease_until timestamptz,provider_id varchar(250),diagnostic varchar(250),created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,outbox_id,recipient),FOREIGN KEY(tenant_id,outbox_id) REFERENCES notification_outbox(tenant_id,id),FOREIGN KEY(tenant_id,customer_id) REFERENCES customers(tenant_id,customer_id));
CREATE INDEX notification_email_pending ON notification_email_deliveries(next_attempt_at,id) WHERE status IN('PENDING','RETRY');
CREATE INDEX notification_email_expired ON notification_email_deliveries(lease_until,id) WHERE status='LEASED';
CREATE TABLE notification_email_quota(tenant_id bigint NOT NULL REFERENCES tenants,day date NOT NULL,reserved integer NOT NULL DEFAULT 0,PRIMARY KEY(tenant_id,day));
ALTER TABLE notification_email_deliveries ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_email_deliveries FORCE ROW LEVEL SECURITY;
CREATE POLICY email_history ON notification_email_deliveries FOR SELECT USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','VIEW') AND EXISTS(SELECT 1 FROM notification_outbox o WHERE o.tenant_id=notification_email_deliveries.tenant_id AND o.id=outbox_id));
GRANT SELECT ON notification_email_deliveries TO stockflow_runtime;
ALTER TABLE notification_digest_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_digest_runs FORCE ROW LEVEL SECURITY;
ALTER TABLE notification_email_quota ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_email_quota FORCE ROW LEVEL SECURITY;

-- A separate queue: in-app delivery success never hides email failures.
CREATE FUNCTION sf_notification_email_capture() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE s public.notification_delivery_settings; cid integer; addr text; details text; resource bigint; company text;
BEGIN
 SELECT * INTO s FROM public.notification_delivery_settings WHERE tenant_id=NEW.tenant_id AND email_enabled AND sender_verified;
 IF s.tenant_id IS NULL THEN RETURN NEW; END IF;
 details:=NEW.body;
 IF NEW.event IN('stock.digest','operations.failed') THEN
  IF NEW.event='operations.failed' AND NOT s.failure_alerts THEN RETURN NEW; END IF;
  FOR addr IN SELECT btrim(x) FROM unnest(string_to_array(s.administrator_emails,',')) x WHERE btrim(x)<>'' LOOP
   INSERT INTO public.notification_email_deliveries(tenant_id,outbox_id,recipient,title,body) VALUES(NEW.tenant_id,NEW.id,addr,NEW.title,details) ON CONFLICT DO NOTHING;
  END LOOP;
  RETURN NEW;
 END IF;
 resource:=split_part(NEW.event_key,':',2)::bigint;
 IF NEW.event='invoice.completed' AND s.invoice_emails THEN
  SELECT sale.customer_id,details||E'\n\nInvoice: '||sale.invoice_number||E'\n'||COALESCE((SELECT string_agg(i.label,E'\n') FROM
   (SELECT COALESCE(si.product_name_snapshot,p.product_name)||' x '||si.quantity||' : INR '||si.line_total AS label FROM public.sale_items si JOIN public.products p ON p.tenant_id=si.tenant_id AND p.product_id=si.product_id WHERE si.tenant_id=NEW.tenant_id AND si.sale_id=resource ORDER BY si.sale_item_id LIMIT 100) i),'')||E'\nInvoice total INR '||sale.total_amount INTO cid,details
   FROM public.sales sale WHERE sale.tenant_id=NEW.tenant_id AND sale.sale_id=resource AND sale.status='COMPLETED';
 ELSIF NEW.event IN('repair.received','repair.ready') AND s.repair_emails THEN
  SELECT customer_id INTO cid FROM public.service_requests WHERE tenant_id=NEW.tenant_id AND service_request_id=resource;
 ELSIF NEW.event='warranty.updated' AND s.warranty_emails THEN
  SELECT sale.customer_id INTO cid FROM public.warranty_claims wc JOIN public.warranties w ON w.tenant_id=wc.tenant_id AND w.warranty_id=wc.warranty_id JOIN public.sale_items si ON si.tenant_id=w.tenant_id AND si.sale_item_id=w.sale_item_id JOIN public.sales sale ON sale.tenant_id=si.tenant_id AND sale.sale_id=si.sale_id WHERE wc.tenant_id=NEW.tenant_id AND wc.warranty_claim_id=resource;
 ELSE RETURN NEW; END IF;
 SELECT c.email INTO addr FROM public.customers c JOIN public.notification_customer_consents consent ON consent.tenant_id=c.tenant_id AND consent.customer_id=c.customer_id AND consent.channel='email' AND consent.allowed WHERE c.tenant_id=NEW.tenant_id AND c.customer_id=cid AND c.is_active;
 IF addr IS NULL OR btrim(addr)='' THEN RETURN NEW; END IF;
 SELECT company_name INTO company FROM public.company_settings WHERE tenant_id=NEW.tenant_id AND company_settings_id=1;
 INSERT INTO public.notification_email_deliveries(tenant_id,outbox_id,customer_id,recipient,title,body) VALUES(NEW.tenant_id,NEW.id,cid,btrim(addr),NEW.title,left(COALESCE(company,'StockFlow')||E'\n\n'||COALESCE(details,NEW.body),20000)) ON CONFLICT DO NOTHING;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER notification_email_capture AFTER INSERT ON notification_outbox DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sf_notification_email_capture();

-- One digest per location/local day, even across worker restarts or concurrent instances.
CREATE FUNCTION sf_notification_digest() RETURNS integer LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE loc record; digest_day date; low integer; empty integer; body_text text; done integer:=0;
BEGIN
 FOR loc IN SELECT l.*,s.time_zone,s.digest_hour FROM public.store_locations l JOIN public.notification_delivery_settings s USING(tenant_id) JOIN public.notification_settings n USING(tenant_id) JOIN public.tenants t USING(tenant_id)
 WHERE l.is_active AND l.location_type<>'TRANSIT' AND s.digest_enabled AND n.enabled AND t.is_active AND extract(hour FROM now() AT TIME ZONE s.time_zone)>=s.digest_hour
 AND NOT EXISTS(SELECT 1 FROM public.notification_digest_runs r WHERE r.tenant_id=l.tenant_id AND r.location_id=l.location_id AND r.day=(now() AT TIME ZONE s.time_zone)::date) ORDER BY l.tenant_id,l.location_id LIMIT 25 LOOP
  digest_day:=(now() AT TIME ZONE loc.time_zone)::date;
  PERFORM pg_advisory_xact_lock(hashtextextended('stock-digest:'||loc.tenant_id||':'||loc.location_id||':'||digest_day,0));
  IF EXISTS(SELECT 1 FROM public.notification_digest_runs r WHERE r.tenant_id=loc.tenant_id AND r.location_id=loc.location_id AND r.day=digest_day) THEN CONTINUE; END IF;
  PERFORM set_config('app.tenant_id',loc.tenant_id::text,true);PERFORM set_config('app.location_id',loc.location_id::text,true);
  WITH balances AS (
   SELECT p.product_name||' / '||v.variant_name AS name,
    CASE WHEN v.is_serial_tracked THEN (SELECT count(*) FROM public.product_units u WHERE u.tenant_id=v.tenant_id AND u.variant_id=v.variant_id AND u.location_id=loc.location_id AND u.unit_status='IN_STOCK') ELSE stock.current_stock END AS qty,
    public.sf_variant_minimum(v.variant_id,v.minimum_stock_level) AS minimum
   FROM public.products p JOIN public.product_variants v ON v.tenant_id=p.tenant_id AND v.product_id=p.product_id
   JOIN public.vw_location_current_stock stock ON stock.tenant_id=v.tenant_id AND stock.variant_id=v.variant_id AND stock.location_id=loc.location_id WHERE p.tenant_id=loc.tenant_id AND p.is_active AND v.is_active
  ), alerts AS(SELECT * FROM balances WHERE qty<=minimum)
  SELECT count(*)::integer,count(*) FILTER(WHERE qty<=0)::integer,
   (SELECT string_agg(name||': '||qty||' (limit '||minimum||')','; ') FROM(SELECT * FROM alerts ORDER BY qty,name LIMIT 5) preview)
  INTO low,empty,body_text FROM alerts;
  INSERT INTO public.notification_digest_runs(tenant_id,location_id,day,low_count,out_count) VALUES(loc.tenant_id,loc.location_id,digest_day,low,empty) ON CONFLICT DO NOTHING;
  IF low>0 THEN
   INSERT INTO public.notification_outbox(tenant_id,event,event_key,reference,status_text,module,location_id,url,title,body)
   VALUES(loc.tenant_id,'stock.digest','stock.digest:'||loc.location_id||':'||digest_day,digest_day::text,'LOW_STOCK','INVENTORY',loc.location_id,'/Inventory/LowStock','Daily stock alert',left(loc.location_name||': '||low||' items at/below limit, including '||empty||' out of stock. '||COALESCE(body_text,''),500)) ON CONFLICT DO NOTHING;
   done:=done+1;
  END IF;
 END LOOP;
 RETURN done;
END $$;

CREATE FUNCTION sf_notification_failure(tid bigint,run_key uuid,job_name text) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE loc bigint;
BEGIN
 IF job_name NOT IN('backup','notification-delivery','email-delivery') THEN RAISE EXCEPTION 'Unknown job'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.notification_delivery_settings s JOIN public.notification_settings n USING(tenant_id) WHERE s.tenant_id=tid AND s.failure_alerts AND n.enabled) THEN RETURN; END IF;
 SELECT location_id INTO loc FROM public.store_locations WHERE tenant_id=tid AND is_active AND location_type<>'TRANSIT' ORDER BY location_id LIMIT 1;
 IF loc IS NULL THEN RETURN; END IF;
 INSERT INTO public.notification_outbox(tenant_id,event,event_key,reference,status_text,module,location_id,url,title,body)
 VALUES(tid,'operations.failed','operations.failed:'||job_name||':'||run_key,job_name,'FAILED','COMPANY_SETTINGS',loc,'/Configuration/Notifications','Operational job failed',job_name||' failed. Review the server/job log for details.') ON CONFLICT DO NOTHING;
END $$;
CREATE FUNCTION sf_notification_failed_job() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NEW.status='FAILED' AND OLD.status<>'FAILED' AND NEW.event<>'operations.failed' THEN
  PERFORM public.sf_notification_failure(NEW.tenant_id,md5('inapp:'||NEW.tenant_id||':'||NEW.id)::uuid,'notification-delivery');
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER notification_failed_job AFTER UPDATE ON notification_outbox FOR EACH ROW EXECUTE FUNCTION sf_notification_failed_job();

-- Claims persist BEFORE contacting the provider. An expired lease is UNKNOWN, never blindly replayed.
CREATE FUNCTION sf_notification_email_claim() RETURNS TABLE(id bigint,tenant_id bigint,token uuid,request_key uuid,recipient text,title text,body text,sender_email text,sender_name text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE job public.notification_email_deliveries; s public.notification_delivery_settings; o public.notification_outbox; used integer; today date:=(now() AT TIME ZONE 'UTC')::date; lease uuid;
BEGIN
 UPDATE public.notification_email_deliveries e SET status='UNKNOWN',diagnostic='Worker stopped during sending; confirm provider status before any resend.' WHERE e.status='LEASED' AND e.lease_until<now();
 FOR job IN SELECT e.* FROM public.notification_email_deliveries e WHERE e.status IN('PENDING','RETRY') AND e.next_attempt_at<=now() AND e.attempts<5 ORDER BY e.id FOR UPDATE SKIP LOCKED LIMIT 25 LOOP
  SELECT * INTO s FROM public.notification_delivery_settings WHERE notification_delivery_settings.tenant_id=job.tenant_id;
  SELECT * INTO o FROM public.notification_outbox WHERE notification_outbox.id=job.outbox_id;
  IF NOT s.email_enabled OR NOT s.sender_verified OR NOT EXISTS(SELECT 1 FROM public.notification_settings n JOIN public.tenants t USING(tenant_id) WHERE n.tenant_id=job.tenant_id AND n.enabled AND t.is_active)
   OR (job.customer_id IS NOT NULL AND (NOT EXISTS(SELECT 1 FROM public.notification_rules r WHERE r.tenant_id=job.tenant_id AND r.event=o.event AND r.enabled) OR NOT EXISTS(SELECT 1 FROM public.notification_customer_consents c JOIN public.customers cust USING(tenant_id,customer_id) WHERE c.tenant_id=job.tenant_id AND c.customer_id=job.customer_id AND c.channel='email' AND c.allowed AND cust.is_active AND btrim(cust.email)=job.recipient)
    OR (o.event='invoice.completed' AND NOT s.invoice_emails) OR (o.event IN('repair.received','repair.ready') AND NOT s.repair_emails) OR (o.event='warranty.updated' AND NOT s.warranty_emails)))
   OR (job.customer_id IS NULL AND (NOT job.recipient=ANY(string_to_array(s.administrator_emails,',')) OR (o.event='stock.digest' AND NOT s.digest_enabled) OR (o.event='operations.failed' AND NOT s.failure_alerts))) THEN
   UPDATE public.notification_email_deliveries e SET status='SKIPPED',diagnostic='Settings, recipient or consent changed.' WHERE e.id=job.id; CONTINUE;
  END IF;
  INSERT INTO public.notification_email_quota VALUES(job.tenant_id,today,0) ON CONFLICT DO NOTHING;
  UPDATE public.notification_email_quota q SET reserved=reserved+1 WHERE q.tenant_id=job.tenant_id AND q.day=today AND q.reserved<s.daily_limit RETURNING reserved INTO used;
  IF used IS NULL THEN
   UPDATE public.notification_email_deliveries e SET next_attempt_at=(today+1)::timestamp AT TIME ZONE 'UTC',diagnostic='Daily email limit reached; deferred.' WHERE e.id=job.id;CONTINUE;
  END IF;
  lease:=gen_random_uuid();
  UPDATE public.notification_email_deliveries e SET status='LEASED',attempts=attempts+1,lease_token=lease,lease_until=now()+interval '2 minutes',diagnostic=NULL WHERE e.id=job.id;
  RETURN QUERY SELECT job.id,job.tenant_id,lease,job.request_key,job.recipient::text,job.title::text,job.body,s.sender_email::text,s.sender_name::text;
  RETURN;
 END LOOP;
END $$;
CREATE FUNCTION sf_notification_email_finish(job_id bigint,token uuid,result text,provider text,diagnostic text,delay_seconds integer DEFAULT 60) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE job public.notification_email_deliveries; event text;
BEGIN
 IF result NOT IN('ACCEPTED','RETRY','FAILED','UNKNOWN','SKIPPED') THEN RAISE EXCEPTION 'Invalid delivery result'; END IF;
 UPDATE public.notification_email_deliveries e SET status=CASE WHEN result='RETRY' AND e.attempts>=5 THEN 'FAILED' ELSE result END,
 provider_id=left(provider,250),diagnostic=left(sf_notification_email_finish.diagnostic,250),next_attempt_at=now()+make_interval(secs=>greatest(60,least(delay_seconds,86400))),lease_until=NULL
 WHERE e.id=job_id AND e.lease_token=token AND e.status='LEASED' RETURNING e.* INTO job;
 IF job.status IN('FAILED','UNKNOWN') THEN
  SELECT o.event INTO event FROM public.notification_outbox o WHERE o.id=job.outbox_id;
  IF event<>'operations.failed' THEN PERFORM public.sf_notification_failure(job.tenant_id,job.request_key,'email-delivery'); END IF;
 END IF;
END $$;
CREATE FUNCTION sf_notification_email_failure() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 IF NEW.status IN('FAILED','UNKNOWN') AND OLD.status NOT IN('FAILED','UNKNOWN') AND EXISTS(SELECT 1 FROM public.notification_outbox o WHERE o.id=NEW.outbox_id AND o.event<>'operations.failed') THEN
  PERFORM public.sf_notification_failure(NEW.tenant_id,NEW.request_key,'email-delivery');
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER notification_email_failure AFTER UPDATE ON notification_email_deliveries FOR EACH ROW EXECUTE FUNCTION sf_notification_email_failure();
REVOKE ALL ON FUNCTION sf_notification_email_failure() FROM PUBLIC;
REVOKE ALL ON FUNCTION sf_notification_email_capture(),sf_notification_digest(),sf_notification_failure(bigint,uuid,text),sf_notification_failed_job(),sf_notification_email_claim(),sf_notification_email_finish(bigint,uuid,text,text,text,integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_notification_digest(),sf_notification_failure(bigint,uuid,text),sf_notification_email_claim(),sf_notification_email_finish(bigint,uuid,text,text,text,integer) TO stockflow_notification_worker;
CREATE OR REPLACE FUNCTION sf_notification_process(batch_size integer DEFAULT 25) RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE job public.notification_outbox; recipient record; completed integer:=0; n integer; msg text;
BEGIN
 FOR job IN SELECT * FROM public.notification_outbox WHERE status IN('PENDING','RETRY') AND next_attempt_at<=now() AND attempts<5 ORDER BY id FOR UPDATE SKIP LOCKED LIMIT greatest(1,least(batch_size,100)) LOOP
  BEGIN
   n:=0;
   IF EXISTS(SELECT 1 FROM public.notification_settings s LEFT JOIN public.notification_rules r USING(tenant_id) JOIN public.tenants t USING(tenant_id) WHERE s.tenant_id=job.tenant_id AND s.enabled AND t.is_active AND ((r.event=job.event AND r.enabled) OR (job.event='stock.digest' AND EXISTS(SELECT 1 FROM public.notification_delivery_settings ds WHERE ds.tenant_id=job.tenant_id AND ds.digest_enabled)) OR (job.event='operations.failed' AND EXISTS(SELECT 1 FROM public.notification_delivery_settings ds WHERE ds.tenant_id=job.tenant_id AND ds.failure_alerts)))) THEN
    FOR recipient IN SELECT u.user_id FROM public.notification_recipients nr JOIN public.users u ON u.tenant_id=nr.tenant_id AND u.user_id=nr.user_id
     JOIN public.roles role ON role.tenant_id=u.tenant_id AND role.role_id=u.role_id
     JOIN public.store_locations l ON l.tenant_id=u.tenant_id AND l.location_id=job.location_id AND l.is_active
     WHERE nr.tenant_id=job.tenant_id AND u.is_active AND role.is_active
     AND EXISTS(SELECT 1 FROM public.user_action_permissions p JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
      WHERE p.tenant_id=u.tenant_id AND p.user_id=u.user_id AND p.is_allowed AND a.action_code='VIEW' AND a.is_active AND m.is_active AND m.module_code=job.module)
     AND (u.is_tenant_owner OR EXISTS(SELECT 1 FROM public.user_store_access a JOIN public.stores s ON s.tenant_id=a.tenant_id AND s.store_id=a.store_id AND s.is_active WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.store_id=l.store_id AND a.is_active)
      OR EXISTS(SELECT 1 FROM public.user_location_access a WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.location_id=l.location_id))
    LOOP
     INSERT INTO public.notifications(tenant_id,user_id,outbox_id,module,location_id,title,body,url) VALUES(job.tenant_id,recipient.user_id,job.id,job.module,job.location_id,job.title,job.body,job.url) ON CONFLICT DO NOTHING;
     n:=n+1;
    END LOOP;
   END IF;
   UPDATE public.notification_outbox SET status=CASE WHEN n>0 THEN 'DELIVERED' ELSE 'SKIPPED' END,attempts=attempts+1,delivered=n,last_error=NULL WHERE id=job.id;
   completed:=completed+1;
  EXCEPTION WHEN OTHERS THEN
   -- Subtransaction rolls back partial delivery. Avoid recording customer data/SQL in diagnostics.
   msg:='Database delivery error ('||SQLSTATE||')';
   UPDATE public.notification_outbox SET attempts=attempts+1,status=CASE WHEN attempts+1>=5 THEN 'FAILED' ELSE 'RETRY' END,
    next_attempt_at=now()+make_interval(secs=>least(3600,30*power(2,attempts)::integer)),last_error=msg WHERE id=job.id;
  END;
 END LOOP;
 RETURN completed;
END $$;
COMMIT;
