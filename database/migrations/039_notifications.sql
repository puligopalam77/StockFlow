-- MANUAL ONLY: stop StockFlow, back up, apply ONCE after 038 as PostgreSQL superuser.
-- No historical notifications are generated. Shop notifications start disabled.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Apply as PostgreSQL superuser'; END IF;
 IF to_regclass('public.company_settings') IS NULL OR NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='company_settings' AND column_name='profile') THEN RAISE EXCEPTION 'Apply through 038 first'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_notification_worker') THEN CREATE ROLE stockflow_notification_worker NOLOGIN; END IF;
END $$;
-- Let staff with Company settings access reach Configuration without granting catalog access.
INSERT INTO module_navigation_permissions(navigation_module_id,permission_module_id,action_code)
SELECT n.module_id,p.module_id,'VIEW' FROM modules n CROSS JOIN modules p
WHERE n.module_code='CONFIGURATION' AND p.module_code='COMPANY_SETTINGS' ON CONFLICT DO NOTHING;
CREATE TABLE notification_settings(
 tenant_id bigint PRIMARY KEY DEFAULT sf_tenant() REFERENCES tenants, enabled boolean NOT NULL DEFAULT false,
 updated_by integer NOT NULL DEFAULT sf_actor(),updated_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id));
CREATE TABLE notification_rules(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),event varchar(40) NOT NULL,
 enabled boolean NOT NULL,title varchar(100) NOT NULL CHECK(length(btrim(title))>=2),
 body varchar(500) NOT NULL CHECK(length(btrim(body))>=2),PRIMARY KEY(tenant_id,event),
 FOREIGN KEY(tenant_id) REFERENCES notification_settings,
 CHECK(event IN('invoice.completed','receipt.completed','repair.received','repair.ready','warranty.updated')));
CREATE TABLE notification_recipients(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),user_id integer NOT NULL,PRIMARY KEY(tenant_id,user_id),
 FOREIGN KEY(tenant_id) REFERENCES notification_settings,FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id));
CREATE TABLE notification_customer_consents(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),customer_id integer NOT NULL,channel varchar(10) NOT NULL CHECK(channel IN('email','whatsapp','sms')),
 allowed boolean NOT NULL,evidence varchar(250) NOT NULL CHECK(length(btrim(evidence))>=5),
 updated_by integer NOT NULL DEFAULT sf_actor(),updated_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(tenant_id,customer_id,channel),
 FOREIGN KEY(tenant_id,customer_id) REFERENCES customers(tenant_id,customer_id),FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id));
CREATE TABLE notification_consent_history(
 id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,customer_id integer NOT NULL,channel varchar(10) NOT NULL,
 allowed boolean NOT NULL,evidence varchar(250) NOT NULL,updated_by integer NOT NULL,updated_at timestamptz NOT NULL,
 FOREIGN KEY(tenant_id,customer_id) REFERENCES customers(tenant_id,customer_id),FOREIGN KEY(tenant_id,updated_by) REFERENCES users(tenant_id,user_id));
CREATE TABLE notification_outbox(
 id bigserial PRIMARY KEY,tenant_id bigint NOT NULL REFERENCES tenants,event varchar(40) NOT NULL,
 event_key varchar(160) NOT NULL,reference varchar(100) NOT NULL,status_text varchar(40) NOT NULL,
 module varchar(40) NOT NULL,location_id bigint NOT NULL,url varchar(200) NOT NULL,
 title varchar(100) NOT NULL,body varchar(500) NOT NULL,
 status varchar(12) NOT NULL DEFAULT 'PENDING' CHECK(status IN('PENDING','RETRY','DELIVERED','SKIPPED','FAILED')),
 attempts integer NOT NULL DEFAULT 0 CHECK(attempts BETWEEN 0 AND 5),delivered integer NOT NULL DEFAULT 0,
 next_attempt_at timestamptz NOT NULL DEFAULT now(),created_at timestamptz NOT NULL DEFAULT now(),last_error varchar(250),
 UNIQUE(tenant_id,event_key),UNIQUE(tenant_id,id),FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id));
CREATE INDEX notification_pending ON notification_outbox(next_attempt_at,id) WHERE status IN('PENDING','RETRY');
CREATE TABLE notifications(
 id bigserial PRIMARY KEY,tenant_id bigint NOT NULL,user_id integer NOT NULL,outbox_id bigint NOT NULL,
 module varchar(40) NOT NULL,location_id bigint NOT NULL,title varchar(100) NOT NULL,body varchar(500) NOT NULL,url varchar(200) NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),read_at timestamptz,
 FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,outbox_id) REFERENCES notification_outbox(tenant_id,id),
 FOREIGN KEY(tenant_id,location_id) REFERENCES store_locations(tenant_id,location_id),UNIQUE(tenant_id,user_id,outbox_id));
CREATE INDEX notification_feed ON notifications(tenant_id,user_id,id DESC);
CREATE INDEX notification_unread ON notifications(tenant_id,user_id,id) WHERE read_at IS NULL;

-- Settings use existing Company settings permissions. Consent also requires customer access.
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['notification_settings','notification_rules','notification_recipients'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);
  EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('CREATE POLICY settings_read ON %I FOR SELECT USING(tenant_id=sf_tenant() AND sf_transfer_permission(''COMPANY_SETTINGS'',''VIEW''))',tab);
  EXECUTE format('CREATE POLICY settings_write ON %I FOR ALL USING(tenant_id=sf_tenant() AND sf_transfer_permission(''COMPANY_SETTINGS'',''EDIT'')) WITH CHECK(tenant_id=sf_tenant() AND sf_transfer_permission(''COMPANY_SETTINGS'',''EDIT''))',tab);
  EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON %I TO stockflow_runtime',tab);
 END LOOP;
END $$;
ALTER TABLE notification_customer_consents ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_customer_consents FORCE ROW LEVEL SECURITY;
CREATE POLICY consent_read ON notification_customer_consents FOR SELECT USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','VIEW') AND sf_transfer_permission('CUSTOMERS','VIEW'));
CREATE POLICY consent_write ON notification_customer_consents FOR ALL USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','EDIT') AND sf_transfer_permission('CUSTOMERS','VIEW')) WITH CHECK(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','EDIT') AND sf_transfer_permission('CUSTOMERS','VIEW'));
GRANT SELECT,INSERT,UPDATE ON notification_customer_consents TO stockflow_runtime;
ALTER TABLE notification_consent_history ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_consent_history FORCE ROW LEVEL SECURITY;
CREATE POLICY history_read ON notification_consent_history FOR SELECT USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','VIEW') AND sf_transfer_permission('CUSTOMERS','VIEW'));
GRANT SELECT ON notification_consent_history TO stockflow_runtime;
ALTER TABLE notification_outbox ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_outbox FORCE ROW LEVEL SECURITY;
CREATE POLICY jobs_read ON notification_outbox FOR SELECT USING(tenant_id=sf_tenant() AND sf_transfer_permission('COMPANY_SETTINGS','VIEW') AND sf_can_location(location_id) AND sf_transfer_permission(module,'VIEW'));
GRANT SELECT ON notification_outbox TO stockflow_runtime;
ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
ALTER TABLE notifications FORCE ROW LEVEL SECURITY;
CREATE POLICY inbox_read ON notifications FOR SELECT USING(tenant_id=sf_tenant() AND user_id=sf_actor() AND sf_visible_location(location_id) AND sf_transfer_permission(module,'VIEW'));
CREATE POLICY inbox_update ON notifications FOR UPDATE USING(tenant_id=sf_tenant() AND user_id=sf_actor() AND sf_visible_location(location_id) AND sf_transfer_permission(module,'VIEW')) WITH CHECK(tenant_id=sf_tenant() AND user_id=sf_actor());
GRANT SELECT,UPDATE(read_at) ON notifications TO stockflow_runtime;

CREATE FUNCTION sf_notification_consent_stamp() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 NEW.updated_by:=public.sf_actor(); NEW.updated_at:=now();
 RETURN NEW;
END $$;
CREATE TRIGGER notification_consent_stamp BEFORE INSERT OR UPDATE ON notification_customer_consents FOR EACH ROW EXECUTE FUNCTION sf_notification_consent_stamp();
CREATE FUNCTION sf_notification_consent_audit() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
BEGIN
 INSERT INTO public.notification_consent_history(tenant_id,customer_id,channel,allowed,evidence,updated_by,updated_at)
 VALUES(NEW.tenant_id,NEW.customer_id,NEW.channel,NEW.allowed,NEW.evidence,NEW.updated_by,NEW.updated_at);
 RETURN NEW;
END $$;
CREATE TRIGGER notification_consent_audit AFTER INSERT OR UPDATE ON notification_customer_consents FOR EACH ROW EXECUTE FUNCTION sf_notification_consent_audit();

-- Capture only successful business transitions, in the SAME transaction. Rollbacks remove their events.
-- Templates are snapshotted at capture time. No remote provider is contacted inside a business transaction.
CREATE FUNCTION sf_notification_capture() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE e text; ref text; state text; mod text; link text; key text; title_text text; body_text text; loc bigint; data jsonb:=to_jsonb(NEW); old_data jsonb;
BEGIN
 IF TG_OP='UPDATE' THEN old_data:=to_jsonb(OLD); END IF;
 IF NOT EXISTS(SELECT 1 FROM public.notification_settings s WHERE s.tenant_id=NEW.tenant_id AND s.enabled) THEN RETURN NEW; END IF;
 IF TG_TABLE_NAME='sales' THEN
  IF NEW.status<>'COMPLETED' OR (TG_OP='UPDATE' AND old_data->>'status'='COMPLETED') THEN RETURN NEW; END IF;
  e:='invoice.completed';ref:=NEW.invoice_number;state:=NEW.status;mod:='SALES';link:='/Sales/Details/'||NEW.sale_id;key:=e||':'||NEW.sale_id;
 ELSIF TG_TABLE_NAME='purchase_receipts' THEN
  IF NEW.status<>'COMPLETED' OR (TG_OP='UPDATE' AND old_data->>'status'='COMPLETED') THEN RETURN NEW; END IF;
  e:='receipt.completed';ref:=NEW.receipt_number;state:=NEW.status;mod:='STOCK_INFLOW';link:='/StockInflow/Details/PURCHASE/'||NEW.receipt_id;key:=e||':'||NEW.receipt_id;
 ELSIF TG_TABLE_NAME='service_requests' THEN
  IF TG_OP='INSERT' THEN e:='repair.received';
  ELSIF NEW.status='READY' AND old_data->>'status'<>NEW.status THEN e:='repair.ready';
  ELSE RETURN NEW; END IF;
  ref:=NEW.service_number;state:=NEW.status;mod:='SERVICE';link:='/Service/Details/'||NEW.service_request_id;
  key:=e||':'||NEW.service_request_id||':'||COALESCE(data->>'version','1');
 ELSIF TG_TABLE_NAME='warranty_claims' THEN
  IF TG_OP<>'UPDATE' OR old_data->>'claim_status'=NEW.claim_status THEN RETURN NEW; END IF;
  e:='warranty.updated';ref:=NEW.claim_number;state:=NEW.claim_status;mod:='WARRANTY_CLAIMS';link:='/Warranty/Claims';
  key:=e||':'||NEW.warranty_claim_id||':'||state||':'||COALESCE(data->>'updated_at',now()::text);
 END IF;
 loc:=NULLIF(data->>'location_id','')::bigint;
 IF loc IS NULL THEN
  -- Warranty claims retain their original warranty/sale location.
  SELECT s.location_id INTO loc FROM public.warranties w JOIN public.sale_items si ON si.sale_item_id=w.sale_item_id AND si.tenant_id=w.tenant_id
  JOIN public.sales s ON s.sale_id=si.sale_id AND s.tenant_id=si.tenant_id WHERE w.warranty_id=(data->>'warranty_id')::bigint AND w.tenant_id=NEW.tenant_id;
 END IF;
 SELECT r.title,r.body INTO title_text,body_text FROM public.notification_rules r WHERE r.tenant_id=NEW.tenant_id AND r.event=e AND r.enabled;
 IF title_text IS NULL OR loc IS NULL THEN RETURN NEW; END IF;
 INSERT INTO public.notification_outbox(tenant_id,event,event_key,reference,status_text,module,location_id,url,title,body)
 VALUES(NEW.tenant_id,e,key,ref,state,mod,loc,link,
 left(replace(replace(title_text,'{reference}',ref),'{status}',state),100),left(replace(replace(body_text,'{reference}',ref),'{status}',state),500)) ON CONFLICT(tenant_id,event_key) DO NOTHING;
 RETURN NEW;
END $$;
CREATE TRIGGER notification_sale AFTER INSERT OR UPDATE ON sales FOR EACH ROW EXECUTE FUNCTION sf_notification_capture();
CREATE TRIGGER notification_receipt AFTER INSERT OR UPDATE ON purchase_receipts FOR EACH ROW EXECUTE FUNCTION sf_notification_capture();
CREATE TRIGGER notification_repair AFTER INSERT OR UPDATE ON service_requests FOR EACH ROW EXECUTE FUNCTION sf_notification_capture();
CREATE TRIGGER notification_claim AFTER UPDATE ON warranty_claims FOR EACH ROW EXECUTE FUNCTION sf_notification_capture();

-- Worker has NO direct table grants and NO arbitrary tenant/SQL parameters.
-- Row locks + delivery uniqueness support concurrent app instances. Delivery and status commit atomically.
CREATE FUNCTION sf_notification_process(batch_size integer DEFAULT 25) RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE job public.notification_outbox; recipient record; completed integer:=0; n integer; msg text;
BEGIN
 FOR job IN SELECT * FROM public.notification_outbox WHERE status IN('PENDING','RETRY') AND next_attempt_at<=now() AND attempts<5 ORDER BY id FOR UPDATE SKIP LOCKED LIMIT greatest(1,least(batch_size,100)) LOOP
  BEGIN
   n:=0;
   IF EXISTS(SELECT 1 FROM public.notification_settings s JOIN public.notification_rules r USING(tenant_id) JOIN public.tenants t USING(tenant_id) WHERE s.tenant_id=job.tenant_id AND s.enabled AND r.event=job.event AND r.enabled AND t.is_active) THEN
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
REVOKE ALL ON FUNCTION sf_notification_capture(),sf_notification_consent_stamp(),sf_notification_consent_audit(),sf_notification_process(integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sf_notification_process(integer) TO stockflow_notification_worker;
COMMIT;
-- For a non-superuser backend login, the DBA must separately run:
-- GRANT stockflow_notification_worker TO your_backend_login;
-- Then set Notifications:WorkerEnabled=true and restart. Never grant this role to stockflow_runtime.
