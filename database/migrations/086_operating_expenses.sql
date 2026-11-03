-- MANUAL ONLY after 085. Backup/stop writers; run entire file as postgres.
-- Operating expenses are not inventory purchases or eligible GST ITC.
BEGIN;
SET LOCAL lock_timeout='15s';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper) THEN RAISE EXCEPTION 'Run as superuser'; END IF;
 IF to_regprocedure('public.sf_master_access(text,bigint,boolean)') IS NULL THEN RAISE EXCEPTION 'Apply 085 first'; END IF;
END $$;
INSERT INTO public.modules(module_code,module_name,route,sort_order,show_in_navigation,navigation_icon) VALUES('EXPENSES','Operating Expenses','/Expenses',37,true,'document');
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT module_id,a.code,a.label,a.code NOT IN('VIEW','EXPORT') FROM public.modules CROSS JOIN (VALUES
 ('VIEW','View expenses'),('CREATE','Register expenses'),('EDIT','Correct expenses and maintain categories/payees'),('POST','Post expense'),('PAY','Record expense payments'),('REVERSE','Refund or reverse expense payments'),('CANCEL','Cancel unpaid expense'),('ATTACH','Attach expense evidence'),('EXPORT','Export expense reports')) a(code,label) WHERE module_code='EXPENSES';
INSERT INTO public.module_navigation_permissions SELECT module_id,module_id,'VIEW' FROM public.modules WHERE module_code='EXPENSES';
INSERT INTO public.user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT u.tenant_id,u.user_id,a.module_action_id,true FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id) WHERE u.is_tenant_owner AND m.module_code='EXPENSES' ON CONFLICT DO NOTHING;
INSERT INTO public.role_action_permissions(tenant_id,role_id,module_action_id,is_allowed) SELECT DISTINCT u.tenant_id,u.role_id,a.module_action_id,false FROM public.users u CROSS JOIN public.module_actions a JOIN public.modules m USING(module_id) WHERE m.module_code='EXPENSES' ON CONFLICT DO NOTHING;
CREATE FUNCTION public.sf_expense_allowed(act text) RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path=pg_catalog,public AS $$
 SELECT public.sf_store()>0 AND public.sf_location()>0 AND public.sf_shop_member(public.sf_store()) AND public.sf_can_location(public.sf_location())
 AND EXISTS(SELECT 1 FROM public.store_locations WHERE tenant_id=public.sf_tenant() AND location_id=public.sf_location() AND store_id=public.sf_store() AND is_active)
 AND public.sf_visible_business_profile() IS NOT NULL AND public.sf_transfer_permission('EXPENSES',act)
$$;
CREATE TABLE public.expense_categories(tenant_id bigint NOT NULL,store_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,category_id uuid NOT NULL,name varchar(150) NOT NULL,
 PRIMARY KEY(tenant_id,category_id),UNIQUE(tenant_id,store_id,business_profile_code,name),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id));
CREATE TABLE public.expense_payees(tenant_id bigint NOT NULL,store_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,payee_id uuid NOT NULL,name varchar(150) NOT NULL,
 PRIMARY KEY(tenant_id,payee_id),UNIQUE(tenant_id,store_id,business_profile_code,name),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id));
CREATE TABLE public.expenses(tenant_id bigint NOT NULL,store_id bigint NOT NULL,location_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,
 expense_id uuid NOT NULL,category_id uuid NOT NULL,payee_id uuid NOT NULL,incurred_date date NOT NULL,amount numeric(14,2) NOT NULL CHECK(amount>0),description varchar(2000) NOT NULL,
 status text NOT NULL CHECK(status IN('DRAFT','POSTED','CANCELLED')),version bigint NOT NULL CHECK(version>0),created_by integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,expense_id),FOREIGN KEY(tenant_id,store_id) REFERENCES public.stores(tenant_id,store_id),FOREIGN KEY(tenant_id,location_id) REFERENCES public.store_locations(tenant_id,location_id),
 FOREIGN KEY(tenant_id,category_id) REFERENCES public.expense_categories(tenant_id,category_id),FOREIGN KEY(tenant_id,payee_id) REFERENCES public.expense_payees(tenant_id,payee_id),FOREIGN KEY(tenant_id,created_by) REFERENCES public.users(tenant_id,user_id));
CREATE TABLE public.expense_payments(tenant_id bigint NOT NULL,payment_id uuid NOT NULL,expense_id uuid NOT NULL,location_id bigint NOT NULL,business_profile_code varchar(40) NOT NULL,
 paid_date date NOT NULL,amount numeric(14,2) NOT NULL CHECK(amount<>0),payment_mode text NOT NULL CHECK(payment_mode IN('CASH','UPI','DEBIT_CARD','CREDIT_CARD','BANK_TRANSFER')),
 kind text NOT NULL CHECK(kind IN('PAYMENT','REFUND','REVERSAL')),reverses_id uuid,reference varchar(150) NOT NULL,actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),movement_id bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
 PRIMARY KEY(tenant_id,payment_id),FOREIGN KEY(tenant_id,expense_id) REFERENCES public.expenses(tenant_id,expense_id),FOREIGN KEY(tenant_id,reverses_id) REFERENCES public.expense_payments(tenant_id,payment_id),
 UNIQUE(tenant_id,reverses_id),FOREIGN KEY(tenant_id,actor_id) REFERENCES public.users(tenant_id,user_id));
CREATE INDEX expense_payment_day ON public.expense_payments(tenant_id,location_id,paid_date,payment_mode);
CREATE INDEX expense_search ON public.expenses(tenant_id,store_id,location_id,business_profile_code,incurred_date DESC);
CREATE TABLE public.expense_events(tenant_id bigint NOT NULL,operation_id uuid NOT NULL,expense_id uuid NOT NULL,actor_id integer NOT NULL,action text NOT NULL,input jsonb NOT NULL,snapshot jsonb NOT NULL,version bigint NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,operation_id),FOREIGN KEY(tenant_id,expense_id) REFERENCES public.expenses(tenant_id,expense_id));
CREATE TABLE public.expense_attachments(tenant_id bigint NOT NULL,attachment_id uuid NOT NULL,expense_id uuid NOT NULL,name varchar(180) NOT NULL,content_type text NOT NULL CHECK(content_type IN('application/pdf','image/png','image/jpeg')),data bytea NOT NULL CHECK(octet_length(data) BETWEEN 1 AND 5242880),created_by integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(tenant_id,attachment_id),FOREIGN KEY(tenant_id,expense_id) REFERENCES public.expenses(tenant_id,expense_id));
CREATE TABLE public.expense_master_events(tenant_id bigint NOT NULL,entity text NOT NULL,master_id uuid NOT NULL,name text NOT NULL,actor_id integer NOT NULL,created_at timestamptz NOT NULL DEFAULT now());
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['expense_categories','expense_payees','expenses','expense_payments','expense_events','expense_attachments','expense_master_events'] LOOP
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',tab); EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('GRANT SELECT ON public.%I TO stockflow_runtime',tab);
 END LOOP;
 FOREACH tab IN ARRAY ARRAY['expense_payments','expense_events','expense_attachments','expense_master_events'] LOOP
  EXECUTE format('CREATE TRIGGER expense_immutable BEFORE UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.sf_quotation_immutable()',tab);
 END LOOP;
 FOREACH tab IN ARRAY ARRAY['expense_categories','expense_payees'] LOOP
  EXECUTE format('CREATE POLICY expense_master_read ON public.%I FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_expense_allowed(''VIEW''))',tab);
 END LOOP;
 FOREACH tab IN ARRAY ARRAY['expense_payments','expense_events','expense_attachments'] LOOP
  EXECUTE format('CREATE POLICY expense_child_read ON public.%I FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant() AND EXISTS(SELECT 1 FROM public.expenses e WHERE e.tenant_id=%I.tenant_id AND e.expense_id=%I.expense_id))',tab,tab,tab);
 END LOOP;
END $$;
CREATE POLICY expense_read ON public.expenses FOR SELECT TO stockflow_runtime USING(tenant_id=public.sf_tenant() AND store_id=public.sf_store() AND location_id=public.sf_location() AND business_profile_code=public.sf_visible_business_profile() AND public.sf_expense_allowed('VIEW'));
CREATE FUNCTION public.sf_expense_master(kind text,id uuid,label text) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE result uuid; BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 IF NOT public.sf_expense_allowed('VIEW') OR NOT public.sf_expense_allowed('EDIT') OR id IS NULL OR id='00000000-0000-0000-0000-000000000000'::uuid OR length(btrim(coalesce(label,''))) NOT BETWEEN 1 AND 150 OR kind IS NULL OR kind NOT IN('CATEGORY','PAYEE') THEN RAISE EXCEPTION 'Expense master action denied' USING ERRCODE='42501'; END IF;
 IF kind='CATEGORY' THEN
 INSERT INTO public.expense_categories VALUES(public.sf_tenant(),public.sf_store(),public.sf_visible_business_profile(),id,btrim(label)) ON CONFLICT(tenant_id,store_id,business_profile_code,name) DO UPDATE SET name=excluded.name RETURNING category_id INTO result;
 ELSE INSERT INTO public.expense_payees VALUES(public.sf_tenant(),public.sf_store(),public.sf_visible_business_profile(),id,btrim(label)) ON CONFLICT(tenant_id,store_id,business_profile_code,name) DO UPDATE SET name=excluded.name RETURNING payee_id INTO result; END IF;
 INSERT INTO public.expense_master_events VALUES(public.sf_tenant(),kind,result,btrim(label),public.sf_actor(),now()); RETURN result;
END $$;
CREATE FUNCTION public.sf_expense_write(id uuid,op uuid,expected bigint,act text,body jsonb) RETURNS bigint LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE e public.expenses; prior public.expense_events; original public.expense_payments; paid numeric; value numeric; n bigint; method text; day date; reason text; cat uuid; payee uuid; original_input jsonb; today date:=(now() AT TIME ZONE 'Asia/Kolkata')::date;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtextextended('catalog-registration:'||public.sf_tenant()::text,0));
 IF act IS NULL OR act NOT IN('SAVE','POST','CORRECT','PAY','REFUND','REVERSE','CANCEL','ATTACH') OR NOT public.sf_expense_allowed('VIEW') OR NOT public.sf_expense_allowed(CASE act WHEN 'SAVE' THEN CASE WHEN expected=0 THEN 'CREATE' ELSE 'EDIT' END WHEN 'CORRECT' THEN 'EDIT' WHEN 'REFUND' THEN 'REVERSE' WHEN 'ATTACH' THEN 'ATTACH' ELSE act END) THEN RAISE EXCEPTION 'Expense action not permitted' USING ERRCODE='42501'; END IF;
 IF id IS NULL OR op IS NULL OR id='00000000-0000-0000-0000-000000000000'::uuid OR op='00000000-0000-0000-0000-000000000000'::uuid OR expected IS NULL OR expected<0 OR jsonb_typeof(body) IS DISTINCT FROM 'object' OR pg_column_size(body)>7200000 THEN RAISE EXCEPTION 'Invalid expense request' USING ERRCODE='23514'; END IF;
 original_input:=jsonb_build_object('expected',expected,'body',CASE WHEN act='ATTACH' THEN (body-'data')||jsonb_build_object('dataSha256',encode(sha256(decode(body->>'data','base64')),'hex')) ELSE body END);
 PERFORM pg_advisory_xact_lock(hashtextextended('expense-op:'||public.sf_tenant()::text||':'||op::text,0));
 SELECT * INTO prior FROM public.expense_events WHERE tenant_id=public.sf_tenant() AND operation_id=op;
 IF FOUND THEN
  IF prior.expense_id<>id OR prior.actor_id<>public.sf_actor() OR prior.action<>act OR prior.input IS DISTINCT FROM original_input OR NOT EXISTS(SELECT 1 FROM public.expenses x WHERE x.tenant_id=public.sf_tenant() AND x.expense_id=id AND x.store_id=public.sf_store() AND x.location_id=public.sf_location() AND x.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Expense retry identity conflicts' USING ERRCODE='23514'; END IF;
  RETURN prior.version;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('expense:'||public.sf_tenant()::text||':'||id::text,0));
 SELECT * INTO e FROM public.expenses WHERE tenant_id=public.sf_tenant() AND expense_id=id FOR UPDATE;
 IF FOUND THEN
  IF e.store_id<>public.sf_store() OR e.location_id<>public.sf_location() OR e.business_profile_code<>public.sf_visible_business_profile() THEN RAISE EXCEPTION 'Expense unavailable in selected shop' USING ERRCODE='42501'; END IF;
  IF e.version<>expected THEN RAISE EXCEPTION 'Expense changed; reload before continuing' USING ERRCODE='40001'; END IF;
 ELSE IF expected<>0 OR act<>'SAVE' THEN RAISE EXCEPTION 'Expense not found' USING ERRCODE='23514'; END IF; END IF;
 IF e.status='CANCELLED' THEN RAISE EXCEPTION 'Cancelled expense cannot change' USING ERRCODE='23514'; END IF;
 SELECT coalesce(sum(amount),0) INTO paid FROM public.expense_payments WHERE tenant_id=public.sf_tenant() AND expense_id=id;
 reason:=btrim(coalesce(body->>'reason',''));
 IF length(reason)>1000 OR (act IN('CORRECT','REFUND','REVERSE','CANCEL') AND length(reason)<3) THEN RAISE EXCEPTION 'Record a correction/refund/reversal/cancellation reason (3-1000 characters)' USING ERRCODE='23514'; END IF;
 n:=expected+1;
 IF act IN('SAVE','CORRECT') THEN
  IF (act='SAVE' AND e.status='POSTED') OR (act='CORRECT' AND e.status IS DISTINCT FROM 'POSTED') THEN RAISE EXCEPTION 'Use a correction for a posted expense' USING ERRCODE='23514'; END IF;
  value:=(body->>'amount')::numeric;day:=(body->>'incurredDate')::date;cat:=(body->>'categoryId')::uuid;payee:=(body->>'payeeId')::uuid;
  IF value IS NULL OR value<=0 OR value>9999999999.99 OR round(value,2)<>value OR value<paid OR day IS NULL OR day>today OR day<'2000-01-01' OR EXISTS(SELECT 1 FROM public.expense_payments WHERE tenant_id=public.sf_tenant() AND expense_id=id AND paid_date<day) OR length(btrim(coalesce(body->>'description',''))) NOT BETWEEN 1 AND 2000
   OR NOT EXISTS(SELECT 1 FROM public.expense_categories c WHERE c.tenant_id=public.sf_tenant() AND c.category_id=cat AND c.store_id=public.sf_store() AND c.business_profile_code=public.sf_visible_business_profile())
   OR NOT EXISTS(SELECT 1 FROM public.expense_payees p WHERE p.tenant_id=public.sf_tenant() AND p.payee_id=payee AND p.store_id=public.sf_store() AND p.business_profile_code=public.sf_visible_business_profile()) THEN RAISE EXCEPTION 'Invalid expense category, payee, date or amount' USING ERRCODE='23514'; END IF;
  IF expected=0 THEN INSERT INTO public.expenses VALUES(public.sf_tenant(),public.sf_store(),public.sf_location(),public.sf_visible_business_profile(),id,cat,payee,day,value,body->>'description','DRAFT',n,public.sf_actor(),now(),now());
  ELSE UPDATE public.expenses SET category_id=cat,payee_id=payee,incurred_date=day,amount=value,description=body->>'description',version=n,updated_at=now() WHERE tenant_id=e.tenant_id AND expense_id=id; END IF;
 ELSIF act IN('PAY','REFUND','REVERSE') THEN
  IF e.status<>'POSTED' THEN RAISE EXCEPTION 'Post the expense before payment' USING ERRCODE='23514'; END IF;
  day:=(body->>'paidDate')::date;value:=(body->>'amount')::numeric;method:=body->>'paymentMode';
  IF act='REVERSE' THEN
   SELECT * INTO original FROM public.expense_payments WHERE tenant_id=e.tenant_id AND expense_id=id AND payment_id=(body->>'paymentId')::uuid;
   IF NOT FOUND OR original.kind='REVERSAL' OR EXISTS(SELECT 1 FROM public.expense_payments WHERE tenant_id=e.tenant_id AND reverses_id=original.payment_id) THEN RAISE EXCEPTION 'Payment already reversed or unavailable' USING ERRCODE='23514'; END IF;
   value:=-original.amount;method:=original.payment_mode;
  ELSE
   IF value IS NULL OR value<=0 OR round(value,2)<>value THEN RAISE EXCEPTION 'Payment must be positive with at most two decimals' USING ERRCODE='23514'; END IF;
   IF act='REFUND' THEN value:=-value; END IF;
  END IF;
  IF day IS NULL OR day>today OR day<e.incurred_date OR (act='REVERSE' AND day<original.paid_date) OR method IS NULL OR method NOT IN('CASH','UPI','DEBIT_CARD','CREDIT_CARD','BANK_TRANSFER') OR paid+value<0 OR paid+value>e.amount OR length(coalesce(body->>'reference',''))>150 THEN RAISE EXCEPTION 'Invalid payment date/method or amount exceeds expense balance' USING ERRCODE='23514'; END IF;
  INSERT INTO public.expense_payments VALUES(e.tenant_id,op,id,e.location_id,e.business_profile_code,day,value,method,CASE act WHEN 'PAY' THEN 'PAYMENT' WHEN 'REFUND' THEN 'REFUND' ELSE 'REVERSAL' END,CASE WHEN act='REVERSE' THEN original.payment_id END,coalesce(body->>'reference',''),public.sf_actor(),now());
 ELSIF act='POST' THEN IF e.status<>'DRAFT' THEN RAISE EXCEPTION 'Expense already posted' USING ERRCODE='23514'; END IF;
 ELSIF act='CANCEL' THEN IF paid<>0 THEN RAISE EXCEPTION 'Reverse or refund payments before cancellation' USING ERRCODE='23514'; END IF;
 ELSIF act='ATTACH' THEN
  IF length(coalesce(body->>'name','')) NOT BETWEEN 1 AND 180 OR body->>'contentType' NOT IN('application/pdf','image/png','image/jpeg') OR octet_length(decode(body->>'data','base64')) NOT BETWEEN 1 AND 5242880 THEN RAISE EXCEPTION 'Invalid attachment' USING ERRCODE='23514'; END IF;
  IF NOT ((body->>'contentType'='application/pdf' AND substring(decode(body->>'data','base64') FROM 1 FOR 5)=decode('255044462d','hex'))
   OR (body->>'contentType'='image/png' AND substring(decode(body->>'data','base64') FROM 1 FOR 8)=decode('89504e470d0a1a0a','hex'))
   OR (body->>'contentType'='image/jpeg' AND substring(decode(body->>'data','base64') FROM 1 FOR 3)=decode('ffd8ff','hex'))) THEN RAISE EXCEPTION 'Attachment signature mismatch' USING ERRCODE='23514'; END IF;
  INSERT INTO public.expense_attachments VALUES(e.tenant_id,op,id,body->>'name',body->>'contentType',decode(body->>'data','base64'),public.sf_actor(),now());
 END IF;
 IF act NOT IN('SAVE','CORRECT') THEN UPDATE public.expenses SET status=CASE act WHEN 'POST' THEN 'POSTED' WHEN 'CANCEL' THEN 'CANCELLED' ELSE status END,version=n,updated_at=now() WHERE tenant_id=e.tenant_id AND expense_id=id; END IF;
 INSERT INTO public.expense_events VALUES(public.sf_tenant(),op,id,public.sf_actor(),act,original_input,
  (SELECT to_jsonb(x)||jsonb_build_object('paid',coalesce((SELECT sum(amount) FROM public.expense_payments WHERE tenant_id=x.tenant_id AND expense_id=x.expense_id),0)) FROM public.expenses x WHERE x.tenant_id=public.sf_tenant() AND x.expense_id=id),n,now());
 RETURN n;
END $$;
CREATE FUNCTION public.sf_expense_cash_movements(loc bigint,start_at timestamptz,end_at timestamptz,zone text) RETURNS TABLE(id bigint,amount numeric,occurred timestamptz)
 LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT p.movement_id,p.amount,p.paid_date::timestamp AT TIME ZONE zone FROM public.expense_payments p JOIN public.expenses e USING(tenant_id,expense_id)
 WHERE p.tenant_id=public.sf_tenant() AND p.location_id=loc AND loc=public.sf_location() AND e.store_id=public.sf_store()
 AND public.sf_can_location(loc) AND public.sf_shop_member(public.sf_store()) AND p.business_profile_code=public.sf_visible_business_profile()
 AND public.sf_transfer_permission('REPORTS','VIEW') AND (public.sf_owner() OR EXISTS(SELECT 1 FROM public.users u JOIN public.roles r USING(tenant_id,role_id) WHERE u.tenant_id=public.sf_tenant() AND u.user_id=public.sf_actor() AND u.is_active AND lower(btrim(r.role_name))='admin'))
 AND p.payment_mode='CASH' AND p.paid_date::timestamp AT TIME ZONE zone>=start_at AND p.paid_date::timestamp AT TIME ZONE zone<end_at
$$;
REVOKE ALL ON FUNCTION public.sf_expense_cash_movements(bigint,timestamptz,timestamptz,text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_expense_cash_movements(bigint,timestamptz,timestamptz,text) TO stockflow_runtime;
REVOKE ALL ON FUNCTION public.sf_expense_allowed(text),public.sf_expense_master(text,uuid,text),public.sf_expense_write(uuid,uuid,bigint,text,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_expense_allowed(text),public.sf_expense_master(text,uuid,text),public.sf_expense_write(uuid,uuid,bigint,text,jsonb) TO stockflow_runtime;
COMMIT;
