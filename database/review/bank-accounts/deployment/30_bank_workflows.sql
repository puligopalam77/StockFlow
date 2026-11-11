-- DEPLOYMENT REVIEW CANDIDATE. NOT EXECUTED OR APPROVED FOR EXECUTION.
-- Outside automatic migration discovery. See REVIEW.txt; do not run local 093 here.
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='60s';
SET LOCAL search_path=pg_catalog,public,pg_temp;
DO $$ DECLARE required text; actual text; BEGIN
 IF current_setting('stockflow.bank_deployment_approved',true) IS DISTINCT FROM 'bank-accounts-jan2027-review-v1'
 OR current_setting('stockflow.bank_schema_contract_reviewed',true) IS DISTINCT FROM 'yes'
 THEN RAISE EXCEPTION 'Separate target-specific deployment and schema review approval required'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper)
 THEN RAISE EXCEPTION 'Approved maintenance superuser session required'; END IF;
 FOREACH required IN ARRAY ARRAY['database','server_address','server_port','system_identifier','maintenance_role'] LOOP
  actual:=CASE required WHEN 'database' THEN current_database()
   WHEN 'server_address' THEN host(inet_server_addr()) WHEN 'server_port' THEN inet_server_port()::text
   WHEN 'system_identifier' THEN (SELECT system_identifier::text FROM pg_control_system())
   WHEN 'maintenance_role' THEN current_user END;
  IF coalesce(current_setting('stockflow.bank_expected_'||required,true),'')=''
  OR current_setting('stockflow.bank_expected_'||required,true) IS DISTINCT FROM actual
  THEN RAISE EXCEPTION 'Deployment target identity mismatch: %',required; END IF;
 END LOOP;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='stockflow_runtime' AND NOT rolsuper AND NOT rolbypassrls)
 OR has_schema_privilege('stockflow_runtime','public','CREATE')
 THEN RAISE EXCEPTION 'Review runtime role and public schema CREATE restrictions'; END IF;
 IF to_regprocedure('public.sf_snapshot_retail_variant()') IS NULL
 OR to_regprocedure('public.sf_cash_entry_reason_guard()') IS NULL
 THEN RAISE EXCEPTION 'Verify genuine 049 contract and prerequisites through 089'; END IF;
 -- Existence is necessary, not proof of compatible semantics. External reviewed
 -- contract/type/hash/privilege evidence is required before the approval above.
 FOREACH required IN ARRAY ARRAY['sf_tenant()','sf_actor()','sf_store()','sf_location()',
  'sf_owner()','sf_shop_member(bigint)','sf_can_location(bigint)',
  'sf_visible_business_profile()','sf_transfer_permission(text,text)'] LOOP
  IF to_regprocedure('public.'||required) IS NULL THEN RAISE EXCEPTION 'Required helper missing: %',required; END IF;
 END LOOP;
END $$;
DO $$ DECLARE tab text; BEGIN
 IF to_regclass('public.bank_release_control') IS NOT NULL OR to_regclass('public.bank_requests') IS NOT NULL
 OR NOT EXISTS(SELECT 1 FROM public.modules WHERE module_code='BANK_ACCOUNTS' AND NOT is_active AND NOT show_in_navigation)
 THEN RAISE EXCEPTION 'New inactive foundation/metadata required; existing workflows need incremental review'; END IF;
 FOREACH tab IN ARRAY ARRAY['payments','sales','supplier_payments','purchases',
  'service_payments','service_requests','sale_returns','expense_payments','expenses'] LOOP
  IF to_regclass('public.'||tab) IS NULL THEN RAISE EXCEPTION 'Source table missing: %',tab; END IF;
 END LOOP;
END $$;
CREATE TABLE public.bank_release_control(singleton boolean PRIMARY KEY CHECK(singleton),configuration_enabled boolean NOT NULL DEFAULT false,posting_enabled boolean NOT NULL DEFAULT false,max_active_accounts integer NOT NULL DEFAULT 1 CHECK(max_active_accounts>0),max_assigned_shops integer NOT NULL DEFAULT 3 CHECK(max_assigned_shops>0));
INSERT INTO bank_release_control(singleton) VALUES(true);
CREATE TABLE public.bank_requests(tenant_id bigint NOT NULL,request_id uuid NOT NULL,account_id uuid NOT NULL,actor_id integer NOT NULL,action text NOT NULL,body jsonb NOT NULL,result jsonb NOT NULL,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,request_id));
ALTER TABLE bank_release_control ENABLE ROW LEVEL SECURITY;
ALTER TABLE bank_release_control FORCE ROW LEVEL SECURITY;
ALTER TABLE bank_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE bank_requests FORCE ROW LEVEL SECURITY;
REVOKE ALL ON bank_release_control,bank_requests FROM PUBLIC,stockflow_runtime;
CREATE TRIGGER bank_request_immutable BEFORE UPDATE OR DELETE ON bank_requests FOR EACH ROW EXECUTE FUNCTION sf_bank_review_immutable();
CREATE FUNCTION public.sf_bank_allowed(act text) RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT true
 AND EXISTS(SELECT 1 FROM bank_release_control WHERE configuration_enabled AND (act NOT IN('INITIALIZE','VERIFY','REVERSE') OR posting_enabled))
 AND sf_transfer_permission('BANK_ACCOUNTS','VIEW') AND sf_transfer_permission('BANK_ACCOUNTS',act)
 AND EXISTS(SELECT 1 FROM users u JOIN roles r USING(tenant_id,role_id) JOIN tenants t USING(tenant_id)
 WHERE u.tenant_id=sf_tenant() AND u.user_id=sf_actor() AND u.is_active AND r.is_active AND t.is_active
 AND (u.is_tenant_owner OR lower(btrim(r.role_name))='admin'))
 AND (sf_owner() OR sf_store()>0 AND sf_shop_member(sf_store()) AND sf_location()>0 AND sf_can_location(sf_location()))
$$;

-- Existing payment rows remain authoritative. The bank module neither creates
-- replacement payments nor updates customer/supplier balances, stock or GST.
CREATE FUNCTION public.sf_bank_sources() RETURNS TABLE(kind text,key text,store_id bigint,location_id bigint,profile text,amount numeric,mode text,day date,occurred timestamptz,reference text,module text)
 LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 WITH sources AS (
 SELECT 'SALE_PAYMENT'::text kind,p.payment_id::text key,l.store_id,p.location_id,
 coalesce(to_jsonb(s)->>'business_profile_code','ELECTRICAL') profile,p.amount,p.payment_mode::text mode,p.payment_date::date AS day,
 p.payment_date AT TIME ZONE 'Asia/Kolkata' occurred,s.invoice_number::text reference,'SALES'::text module
 FROM payments p JOIN sales s ON s.tenant_id=p.tenant_id AND s.sale_id=p.sale_id JOIN store_locations l ON l.tenant_id=p.tenant_id AND l.location_id=p.location_id
 WHERE p.tenant_id=sf_tenant() AND s.status='COMPLETED' AND p.payment_mode NOT IN('CASH','CREDIT') AND p.amount>0
 UNION ALL SELECT 'SUPPLIER_PAYMENT',p.supplier_payment_id::text,l.store_id,p.location_id,coalesce(to_jsonb(s)->>'business_profile_code','ELECTRICAL'),-p.amount,p.payment_mode,
 (p.payment_date AT TIME ZONE 'Asia/Kolkata')::date,p.payment_date,'SUP-'||p.supplier_payment_id,'PURCHASES'
 FROM supplier_payments p JOIN purchases s ON s.tenant_id=p.tenant_id AND s.purchase_id=p.purchase_id JOIN store_locations l ON l.tenant_id=p.tenant_id AND l.location_id=p.location_id
 WHERE p.tenant_id=sf_tenant() AND p.payment_mode NOT IN('CASH','CREDIT_CARD') AND p.amount>0
 UNION ALL SELECT 'SERVICE_PAYMENT',p.service_payment_id::text,l.store_id,p.location_id,coalesce(to_jsonb(s)->>'business_profile_code','ELECTRICAL'),p.amount,p.payment_mode,
 (p.payment_date AT TIME ZONE 'Asia/Kolkata')::date,p.payment_date,'SRV-'||p.service_payment_id,'SERVICE'
 FROM service_payments p JOIN service_requests s ON s.tenant_id=p.tenant_id AND s.service_request_id=p.service_request_id JOIN store_locations l ON l.tenant_id=p.tenant_id AND l.location_id=p.location_id
 WHERE p.tenant_id=sf_tenant() AND p.payment_mode NOT IN('CASH','CREDIT') AND p.amount>0
 UNION ALL SELECT 'SALE_REFUND',p.sale_return_id::text,l.store_id,p.location_id,coalesce(to_jsonb(s)->>'business_profile_code','ELECTRICAL'),-p.refund_amount,p.refund_mode,
 (p.created_at AT TIME ZONE 'Asia/Kolkata')::date,p.created_at,p.return_number,'SALES'
 FROM sale_returns p JOIN sales s ON s.tenant_id=p.tenant_id AND s.sale_id=p.sale_id JOIN store_locations l ON l.tenant_id=p.tenant_id AND l.location_id=p.location_id
 WHERE p.tenant_id=sf_tenant() AND p.refund_mode IN('UPI','CARD','BANK') AND p.refund_amount>0
 UNION ALL SELECT 'EXPENSE_PAYMENT',p.payment_id::text,l.store_id,p.location_id,p.business_profile_code,p.amount*-1,p.payment_mode,p.paid_date,NULL::timestamptz,p.reference,'EXPENSES'
 FROM expense_payments p JOIN expenses e ON e.tenant_id=p.tenant_id AND e.expense_id=p.expense_id JOIN store_locations l ON l.tenant_id=p.tenant_id AND l.location_id=p.location_id
 WHERE p.tenant_id=sf_tenant() AND e.status='POSTED' AND p.payment_mode NOT IN('CASH','CREDIT_CARD') AND p.kind IN('PAYMENT','REFUND')
 AND NOT EXISTS(SELECT 1 FROM expense_payments r WHERE r.tenant_id=p.tenant_id AND r.reverses_id=p.payment_id)
 ) SELECT s.* FROM sources s WHERE sf_bank_allowed('VIEW') AND sf_transfer_permission(s.module,'VIEW')
 AND s.profile=sf_visible_business_profile() AND sf_shop_member(s.store_id) AND sf_can_location(s.location_id)
 AND (sf_owner() OR s.store_id=sf_store() AND s.location_id=sf_location())
$$;
CREATE OR REPLACE FUNCTION public.sf_bank_write(op text,acct uuid,retry uuid,input jsonb) RETURNS jsonb LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=sf_tenant();actor integer:=sf_actor();a bank_accounts;old bank_requests;
 required text;request_body jsonb:=input-'protectedNumber'-'protectedUpi';answer jsonb;
 opening bank_openings;movement bank_movements;link bank_payment_links;src record;item jsonb;sid bigint;
 amount numeric;net numeric:=0;remaining numeric;date_value date;instant timestamptz;
 id uuid;chosen_link uuid;ref text;reason text;direction integer;fingerprint bytea;stores bigint[];before_config jsonb;account_limit integer;shop_limit integer;
BEGIN
 required:=CASE op WHEN 'create' THEN 'CREATE' WHEN 'edit' THEN 'EDIT' WHEN 'assign' THEN 'ASSIGN' WHEN 'default' THEN 'SET_DEFAULT' WHEN 'status' THEN 'DEACTIVATE' WHEN 'open' THEN 'INITIALIZE' WHEN 'link' THEN 'EDIT' WHEN 'verify' THEN 'VERIFY' WHEN 'reverse' THEN 'REVERSE' END;
 IF required IS NULL OR sf_bank_allowed(required) IS DISTINCT FROM true THEN RAISE EXCEPTION 'Bank operation not permitted' USING ERRCODE='42501'; END IF;
 IF acct IS NULL OR acct='00000000-0000-0000-0000-000000000000' OR retry IS NULL OR retry='00000000-0000-0000-0000-000000000000'
 OR jsonb_typeof(input) IS DISTINCT FROM 'object' THEN RAISE EXCEPTION 'Valid account, request and input required' USING ERRCODE='23514'; END IF;
 IF op IN('create','edit','assign','default','status','open','link') AND NOT sf_owner() THEN RAISE EXCEPTION 'Only the business owner may configure shared bank accounts or opening balances' USING ERRCODE='42501'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('bank-request:'||tid||':'||retry,0));
 SELECT * INTO old FROM bank_requests WHERE tenant_id=tid AND request_id=retry;
 IF FOUND THEN
  IF old.account_id<>acct OR old.actor_id<>actor OR old.action<>op OR old.body IS DISTINCT FROM request_body THEN RAISE EXCEPTION 'Bank request identity conflicts' USING ERRCODE='23514'; END IF;
  RETURN old.result;
 END IF;
 SELECT max_active_accounts,max_assigned_shops INTO account_limit,shop_limit FROM bank_release_control WHERE singleton FOR SHARE;
 IF account_limit IS NULL OR shop_limit IS NULL THEN RAISE EXCEPTION 'Bank policy unavailable' USING ERRCODE='42501'; END IF;
 IF op IN('create','edit','assign','default','status') THEN PERFORM pg_advisory_xact_lock(hashtextextended('bank-config:'||tid,0)); END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('bank-account:'||tid||':'||acct,0));
 SELECT * INTO a FROM bank_accounts WHERE tenant_id=tid AND account_id=acct FOR UPDATE;
 before_config:=CASE WHEN a.account_id IS NULL THEN NULL ELSE jsonb_build_object('bankName',a.bank_name,'displayName',a.display_name,'holderName',a.holder_name,'type',a.account_type,'ifsc',a.ifsc,'lastFour',a.account_last_four,'active',a.is_active,'shops',(SELECT coalesce(jsonb_agg(jsonb_build_object('storeId',s.store_id,'active',s.is_active,'default',s.is_default_receiving) ORDER BY s.store_id),'[]') FROM bank_account_shops s WHERE s.tenant_id=tid AND s.account_id=acct)) END;
 IF op='create' OR op='status' AND (input->>'active')::boolean AND NOT a.is_active THEN
  IF (SELECT count(*) FROM bank_accounts WHERE tenant_id=tid AND is_active AND account_id<>acct)>=account_limit
  THEN RAISE EXCEPTION 'Active bank account limit reached for this business' USING ERRCODE='23514'; END IF;
  IF op='status' AND (SELECT count(*) FROM bank_account_shops WHERE tenant_id=tid AND account_id=acct AND is_active)>shop_limit
  THEN RAISE EXCEPTION 'Reduce shop assignments to the configured limit before reactivation' USING ERRCODE='23514'; END IF;
 END IF;
 IF op='create' THEN
  IF a.account_id IS NOT NULL OR length(coalesce(input->>'protectedNumber',''))=0 OR input->>'fingerprint' !~ '^[a-f0-9]{64}$'
  THEN RAISE EXCEPTION 'A new protected account identity is required' USING ERRCODE='23514'; END IF;
  INSERT INTO bank_accounts(tenant_id,account_id,bank_name,display_name,holder_name,account_type,account_last_four,ifsc,created_by)
  VALUES(tid,acct,btrim(input->>'bankName'),btrim(input->>'displayName'),btrim(input->>'holderName'),input->>'accountType',input->>'lastFour',input->>'ifsc',actor);
  INSERT INTO bank_account_secrets VALUES(tid,acct,input->>'protectedNumber',input->>'protectedUpi',decode(input->>'fingerprint','hex'),1,1);
 ELSE
  IF a.account_id IS NULL THEN RAISE EXCEPTION 'Bank account unavailable' USING ERRCODE='23514'; END IF;
  IF NOT sf_owner() AND NOT EXISTS(SELECT 1 FROM bank_account_shops WHERE tenant_id=tid AND account_id=acct AND store_id=sf_store() AND is_active) THEN RAISE EXCEPTION 'Bank account not assigned to active shop' USING ERRCODE='42501'; END IF;
  IF op IN('edit','assign','default','status') AND (input->>'expectedVersion')::bigint IS DISTINCT FROM a.version THEN RAISE EXCEPTION 'Bank configuration changed; reload before editing' USING ERRCODE='23514'; END IF;
 END IF;
 IF op IN('create','assign') THEN
  IF jsonb_typeof(input->'stores') IS DISTINCT FROM 'array' OR jsonb_array_length(input->'stores') NOT BETWEEN 1 AND shop_limit THEN RAISE EXCEPTION 'Select authorized shops' USING ERRCODE='23514'; END IF;
  SELECT array_agg(value::bigint) INTO stores FROM jsonb_array_elements_text(input->'stores');
  IF cardinality(stores)<>(SELECT count(DISTINCT x) FROM unnest(stores) x) THEN RAISE EXCEPTION 'Shop assignments must be unique' USING ERRCODE='23514'; END IF;
  FOREACH sid IN ARRAY stores LOOP
   IF sf_shop_member(sid) IS DISTINCT FROM true OR NOT EXISTS(SELECT 1 FROM public.stores s WHERE s.tenant_id=tid AND s.store_id=sid AND s.is_active) THEN RAISE EXCEPTION 'Shop assignment denied' USING ERRCODE='42501'; END IF;
  END LOOP;
  UPDATE bank_account_shops SET is_active=false,is_default_receiving=false,version=version+1 WHERE tenant_id=tid AND account_id=acct AND NOT store_id=ANY(stores);
  FOREACH sid IN ARRAY stores LOOP
   INSERT INTO bank_account_shops(tenant_id,account_id,store_id) VALUES(tid,acct,sid)
   ON CONFLICT(tenant_id,account_id,store_id) DO UPDATE SET is_active=true,version=bank_account_shops.version+1;
  END LOOP;
 ELSIF op='edit' THEN
  IF left(input->>'ifsc',4)<>left(a.ifsc,4) OR input ? 'fingerprint' AND decode(input->>'fingerprint','hex') IS DISTINCT FROM (SELECT identity_fingerprint FROM bank_account_secrets WHERE tenant_id=tid AND account_id=acct)
  THEN RAISE EXCEPTION 'Account identity is immutable; create a separate account for a different bank/account number' USING ERRCODE='23514'; END IF;
  UPDATE bank_accounts SET bank_name=btrim(input->>'bankName'),display_name=btrim(input->>'displayName'),holder_name=btrim(input->>'holderName'),account_type=input->>'accountType',ifsc=input->>'ifsc' WHERE tenant_id=tid AND account_id=acct;
  IF input ? 'protectedUpi' THEN UPDATE bank_account_secrets SET protected_upi_id=input->>'protectedUpi' WHERE tenant_id=tid AND account_id=acct; END IF;
 ELSIF op='default' THEN
  sid:=(input->>'storeId')::bigint;
  IF NOT a.is_active OR sf_shop_member(sid) IS DISTINCT FROM true OR NOT EXISTS(SELECT 1 FROM bank_account_shops WHERE tenant_id=tid AND account_id=acct AND store_id=sid AND is_active) THEN RAISE EXCEPTION 'An active assigned account is required' USING ERRCODE='23514'; END IF;
  UPDATE bank_account_shops SET is_default_receiving=false,version=version+1 WHERE tenant_id=tid AND store_id=sid AND is_default_receiving;
  UPDATE bank_account_shops SET is_default_receiving=true,version=version+1 WHERE tenant_id=tid AND account_id=acct AND store_id=sid;
 ELSIF op='status' THEN
  IF jsonb_typeof(input->'active') IS DISTINCT FROM 'boolean' THEN RAISE EXCEPTION 'Choose account status' USING ERRCODE='23514'; END IF;
  UPDATE bank_accounts SET is_active=(input->>'active')::boolean WHERE tenant_id=tid AND account_id=acct;
  IF (input->>'active')::boolean=false THEN UPDATE bank_account_shops SET is_default_receiving=false,version=version+1 WHERE tenant_id=tid AND account_id=acct; END IF;
 END IF;
 IF op IN('create','edit','assign','default','status') THEN
  IF op<>'create' THEN UPDATE bank_accounts SET version=version+1 WHERE tenant_id=tid AND account_id=acct; END IF;
  INSERT INTO bank_account_events(tenant_id,event_id,account_id,action,store_id,version,redacted_changes,reason,request_id,request_fingerprint,actor_id)
  SELECT tid,gen_random_uuid(),acct,CASE op WHEN 'create' THEN 'CREATE' WHEN 'edit' THEN 'EDIT' WHEN 'assign' THEN 'ASSIGN_SHOP' WHEN 'default' THEN 'SET_DEFAULT' WHEN 'status' THEN CASE WHEN is_active THEN 'REACTIVATE' ELSE 'DEACTIVATE' END END,
  CASE WHEN op='default' THEN sid ELSE NULL END,version,jsonb_build_object('before',before_config,'after',jsonb_build_object('bankName',bank_name,'displayName',display_name,'holderName',holder_name,'type',account_type,'ifsc',ifsc,'lastFour',account_last_four,'active',is_active,'shops',(SELECT coalesce(jsonb_agg(jsonb_build_object('storeId',s.store_id,'active',s.is_active,'default',s.is_default_receiving) ORDER BY s.store_id),'[]') FROM bank_account_shops s WHERE s.tenant_id=tid AND s.account_id=acct)),'upiChanged',input ? 'protectedUpi'),
  coalesce(nullif(btrim(input->>'reason'),''),'Authorized bank configuration change'),retry,sha256(convert_to(request_body::text,'UTF8')),actor FROM bank_accounts WHERE tenant_id=tid AND account_id=acct;
  answer:=jsonb_build_object('accountId',acct);
 ELSE
  IF NOT a.is_active THEN RAISE EXCEPTION 'Reactivate this account before linking or posting' USING ERRCODE='23514'; END IF;
  SELECT * INTO opening FROM bank_openings WHERE tenant_id=tid AND account_id=acct;
  IF op='open' THEN
   amount:=(input->>'amount')::numeric;instant:=(input->>'trackingStartedAt')::timestamptz;
   IF amount IS NULL OR amount<0 OR amount>9999999999.99 OR round(amount,2)<>amount OR instant IS NULL OR instant>clock_timestamp() OR instant<'2000-01-01'::timestamptz THEN RAISE EXCEPTION 'Enter a valid opening amount and tracking boundary' USING ERRCODE='23514'; END IF;
   INSERT INTO bank_openings VALUES(tid,acct,gen_random_uuid(),amount,instant,btrim(input->>'evidence'),btrim(input->>'reference'),retry,sha256(convert_to(request_body::text,'UTF8')),actor,clock_timestamp());
   answer:=jsonb_build_object('accountId',acct);
  ELSIF op='link' THEN
   ref:=CASE WHEN input->>'kind'='EXPENSE_PAYMENT' THEN (input->>'key')::uuid::text ELSE (input->>'key')::bigint::text END;
   SELECT * INTO src FROM sf_bank_sources() s WHERE s.kind=input->>'kind' AND s.key=ref;
   IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM bank_account_shops WHERE tenant_id=tid AND account_id=acct AND store_id=src.store_id AND is_active) THEN RAISE EXCEPTION 'An eligible recorded payment and assigned account are required' USING ERRCODE='23514'; END IF;
   PERFORM pg_advisory_xact_lock(hashtextextended('bank-source:'||tid||':'||src.kind||':'||src.key,0));
   id:=gen_random_uuid();
   INSERT INTO bank_payment_links VALUES(tid,id,acct,src.store_id,src.location_id,src.profile,src.kind,src.key,CASE WHEN src.amount>0 THEN 'IN' ELSE 'OUT' END,abs(src.amount),src.mode,src.day,src.occurred,actor,clock_timestamp(),retry,sha256(convert_to(request_body::text,'UTF8')));
   INSERT INTO bank_payment_events VALUES(tid,gen_random_uuid(),id,'REPORTED_UNVERIFIED','Explicit account attribution; bank settlement not verified',retry,sha256(convert_to(request_body::text,'UTF8')),actor,clock_timestamp());
   answer:=jsonb_build_object('linkId',id,'status','Unverified');
  ELSIF op IN('verify','reverse') THEN
   IF opening.account_id IS NULL THEN RAISE EXCEPTION 'Initialize this account opening first' USING ERRCODE='23514'; END IF;
   date_value:=(input->>'effectiveDate')::date;reason:=btrim(input->>'evidence');
   IF date_value IS NULL OR date_value>(clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date OR date_value<(opening.tracking_started_at AT TIME ZONE 'Asia/Kolkata')::date OR length(coalesce(reason,'')) NOT BETWEEN 10 AND 1000 THEN RAISE EXCEPTION 'Enter bank-effective date and meaningful evidence' USING ERRCODE='23514'; END IF;
   id:=gen_random_uuid();
   IF op='verify' THEN
    IF jsonb_typeof(input->'allocations') IS DISTINCT FROM 'array' OR jsonb_array_length(input->'allocations') NOT BETWEEN 1 AND 50 THEN RAISE EXCEPTION 'Select 1–50 recorded payment allocations' USING ERRCODE='23514'; END IF;
    IF jsonb_array_length(input->'allocations')<>(SELECT count(DISTINCT value->>'linkId') FROM jsonb_array_elements(input->'allocations')) THEN RAISE EXCEPTION 'Duplicate allocation' USING ERRCODE='23514'; END IF;
    ref:=upper(btrim(input->>'reference'));
    IF length(coalesce(ref,'')) NOT BETWEEN 3 AND 150 THEN RAISE EXCEPTION 'Bank reference/UTR required' USING ERRCODE='23514'; END IF;
    amount:=(input->>'amount')::numeric;
    IF amount IS NULL OR amount<=0 OR amount>9999999999.99 OR round(amount,2)<>amount OR input->>'direction' IS NULL OR input->>'direction' NOT IN('IN','OUT') THEN RAISE EXCEPTION 'Enter the verified amount and direction' USING ERRCODE='23514'; END IF;
    amount:=amount*CASE WHEN input->>'direction'='IN' THEN 1 ELSE -1 END;
    IF (input->>'afterOpening')::boolean IS DISTINCT FROM true THEN RAISE EXCEPTION 'Confirm evidence is after the opening boundary and not included in opening' USING ERRCODE='23514'; END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(input->'allocations') ORDER BY value->>'linkId' LOOP
     chosen_link:=(item->>'linkId')::uuid;
     SELECT * INTO link FROM bank_payment_links WHERE tenant_id=tid AND account_id=acct AND bank_payment_links.link_id=chosen_link FOR UPDATE;
     IF link.link_id IS NULL THEN RAISE EXCEPTION 'Allocation source unavailable' USING ERRCODE='23514'; END IF;
     SELECT * INTO src FROM sf_bank_sources() s WHERE s.kind=link.source_kind AND s.key=link.source_key;
     IF NOT FOUND OR src.store_id<>link.store_id OR src.location_id<>link.location_id OR src.profile<>link.business_profile_code OR abs(src.amount)<>link.source_amount OR (src.amount>0)<>(link.source_direction='IN')
     OR NOT EXISTS(SELECT 1 FROM bank_account_shops WHERE tenant_id=tid AND account_id=acct AND store_id=link.store_id AND is_active) THEN RAISE EXCEPTION 'Recorded source changed or is not authorized' USING ERRCODE='23514'; END IF;
     IF NOT sf_owner() AND jsonb_array_length(input->'allocations')>1 THEN RAISE EXCEPTION 'Batch verification is owner-only' USING ERRCODE='42501'; END IF;
     remaining:=link.source_amount-coalesce((SELECT sum(b.signed_amount)*CASE WHEN link.source_direction='IN' THEN 1 ELSE -1 END FROM bank_movement_allocations b WHERE b.tenant_id=tid AND b.link_id=link.link_id),0);
     IF (item->>'amount')::numeric IS NULL OR (item->>'amount')::numeric<=0 OR round((item->>'amount')::numeric,2)<>(item->>'amount')::numeric OR (item->>'amount')::numeric>remaining THEN RAISE EXCEPTION 'Allocation exceeds unsettled source amount or currency precision' USING ERRCODE='23514'; END IF;
     net:=net+(item->>'amount')::numeric*CASE WHEN link.source_direction='IN' THEN 1 ELSE -1 END;
    END LOOP;
    IF net<>amount THEN RAISE EXCEPTION 'Verified bank amount must equal signed allocations; never invent fees' USING ERRCODE='23514'; END IF;
    INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,request_id,request_fingerprint)
    VALUES(tid,id,acct,'SETTLEMENT',amount,date_value,ref,ref,reason,actor,retry,sha256(convert_to(request_body::text,'UTF8')));
    FOR item IN SELECT value FROM jsonb_array_elements(input->'allocations') LOOP
     SELECT * INTO link FROM bank_payment_links WHERE tenant_id=tid AND bank_payment_links.link_id=(item->>'linkId')::uuid;
     INSERT INTO bank_movement_allocations VALUES(tid,acct,id,link.link_id,(item->>'amount')::numeric*CASE WHEN link.source_direction='IN' THEN 1 ELSE -1 END);
     IF date_value=(opening.tracking_started_at AT TIME ZONE 'Asia/Kolkata')::date THEN
      INSERT INTO bank_payment_events VALUES(tid,gen_random_uuid(),link.link_id,'BOUNDARY_AFTER_OPENING',reason,gen_random_uuid(),sha256(convert_to(request_body::text,'UTF8')),actor,clock_timestamp());
     END IF;
    END LOOP;
   ELSE
    SELECT * INTO movement FROM bank_movements WHERE tenant_id=tid AND account_id=acct AND movement_id=(input->>'movementId')::uuid;
    IF movement.movement_id IS NULL OR movement.entry_kind<>'SETTLEMENT' OR date_value<movement.effective_date OR EXISTS(SELECT 1 FROM bank_movements WHERE tenant_id=tid AND reverses_id=movement.movement_id) THEN RAISE EXCEPTION 'Original verification unavailable or already reversed' USING ERRCODE='23514'; END IF;
    IF NOT sf_owner() AND EXISTS(SELECT 1 FROM bank_movement_allocations b JOIN bank_payment_links l ON l.tenant_id=b.tenant_id AND l.link_id=b.link_id WHERE b.tenant_id=tid AND b.movement_id=movement.movement_id AND (l.store_id<>sf_store() OR l.location_id<>sf_location() OR l.business_profile_code<>sf_visible_business_profile())) THEN RAISE EXCEPTION 'Cannot reverse a shared or foreign-shop bank event' USING ERRCODE='42501'; END IF;
     IF EXISTS(SELECT 1 FROM bank_movement_allocations b JOIN bank_payment_links l ON l.tenant_id=b.tenant_id AND l.link_id=b.link_id WHERE b.tenant_id=tid AND b.movement_id=movement.movement_id AND (sf_shop_member(l.store_id) IS DISTINCT FROM true OR sf_can_location(l.location_id) IS DISTINCT FROM true OR sf_transfer_permission(CASE l.source_kind WHEN 'SUPPLIER_PAYMENT' THEN 'PURCHASES' WHEN 'EXPENSE_PAYMENT' THEN 'EXPENSES' WHEN 'SERVICE_PAYMENT' THEN 'SERVICE' ELSE 'SALES' END,'VIEW') IS DISTINCT FROM true)) THEN RAISE EXCEPTION 'Original source access is required for reversal' USING ERRCODE='42501'; END IF;
    INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,reverses_id,request_id,request_fingerprint)
    VALUES(tid,id,acct,'VERIFICATION_REVERSAL',-movement.signed_amount,date_value,movement.bank_reference,movement.normalized_bank_reference,reason,actor,movement.movement_id,retry,sha256(convert_to(request_body::text,'UTF8')));
    INSERT INTO bank_movement_allocations SELECT tenant_id,account_id,id,bank_movement_allocations.link_id,-signed_amount FROM bank_movement_allocations WHERE tenant_id=tid AND movement_id=movement.movement_id;
   END IF;
   answer:=jsonb_build_object('movementId',id,'status','Manually Verified');
  END IF;
 END IF;
 INSERT INTO bank_requests VALUES(tid,retry,acct,actor,op,request_body,answer,clock_timestamp());
 RETURN answer;
EXCEPTION WHEN invalid_text_representation OR datetime_field_overflow OR invalid_datetime_format OR numeric_value_out_of_range OR not_null_violation THEN RAISE EXCEPTION 'Invalid or missing bank field' USING ERRCODE='23514';
END $$;

CREATE FUNCTION public.sf_bank_read(acct uuid,selected date,page integer,term text,shop bigint)
 RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
DECLARE tid bigint:=sf_tenant();is_owner boolean:=sf_owner();full_view boolean:=sf_owner() AND sf_bank_allowed('VIEW_BALANCE');
 result jsonb;account_json jsonb;rows_json jsonb;pending_json jsonb;candidates jsonb;opening bank_openings;
 balance numeric;money_in numeric;money_out numeric;total bigint;account_limit integer;shop_limit integer;active_count integer;
BEGIN
 IF sf_bank_allowed('VIEW') IS DISTINCT FROM true THEN RAISE EXCEPTION 'Bank access not permitted' USING ERRCODE='42501'; END IF;
 IF selected IS NULL OR selected>(clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date OR selected<'2000-01-01'::date
 OR page IS NULL OR page NOT BETWEEN 1 AND 100000 OR length(coalesce(term,''))>150 OR shop<=0
 THEN RAISE EXCEPTION 'Choose a valid bank date, shop, search and page' USING ERRCODE='23514'; END IF;
 IF shop IS NOT NULL AND (NOT is_owner OR acct IS NULL OR NOT EXISTS(
  SELECT 1 FROM bank_account_shops a JOIN stores s USING(tenant_id,store_id)
  WHERE a.tenant_id=tid AND a.account_id=acct AND a.store_id=shop))
 THEN RAISE EXCEPTION 'Shop filter is not authorized for this account' USING ERRCODE='42501'; END IF;
 SELECT max_active_accounts,max_assigned_shops INTO account_limit,shop_limit FROM bank_release_control WHERE singleton;
 SELECT count(*) INTO active_count FROM bank_accounts WHERE tenant_id=tid AND is_active;
 result:=jsonb_build_object('today',(clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date,'date',selected,
 'ownerOverview',full_view,'showRunningBalance',full_view AND shop IS NULL,'selectedShopId',shop,
 'canCreate',is_owner AND sf_bank_allowed('CREATE') AND active_count<account_limit,
 'canEdit',is_owner AND sf_bank_allowed('EDIT'),'canLink',is_owner AND sf_bank_allowed('EDIT'),
 'canAssign',is_owner AND sf_bank_allowed('ASSIGN'),'canDefault',is_owner AND sf_bank_allowed('SET_DEFAULT'),
 'canStatus',is_owner AND sf_bank_allowed('DEACTIVATE'),'canOpen',is_owner AND sf_bank_allowed('INITIALIZE'),
 'canVerify',sf_bank_allowed('VERIFY'),'canReverse',sf_bank_allowed('REVERSE'));
 IF is_owner THEN result:=result||jsonb_build_object('policy',jsonb_build_object('maxActiveAccounts',account_limit,'maxAssignedShops',shop_limit),
  'shops',(SELECT coalesce(jsonb_agg(jsonb_build_object('id',store_id,'name',store_name) ORDER BY store_id),'[]')
   FROM stores WHERE tenant_id=tid AND is_active AND sf_shop_member(store_id))); END IF;
 SELECT coalesce(jsonb_agg(j ORDER BY j->>'displayName'),'[]') INTO rows_json FROM (
  SELECT jsonb_build_object('id',a.account_id,'bankName',a.bank_name,'displayName',a.display_name,'holderName',a.holder_name,
   'ifsc',a.ifsc,'type',a.account_type,'maskedNumber','•••• '||a.account_last_four,'active',a.is_active,'version',a.version,
   'defaultHere',EXISTS(SELECT 1 FROM bank_account_shops s WHERE s.tenant_id=tid AND s.account_id=a.account_id AND s.store_id=sf_store() AND s.is_active AND s.is_default_receiving),
   'initialized',o.account_id IS NOT NULL)
  ||CASE WHEN is_owner THEN jsonb_build_object('stores',(SELECT coalesce(jsonb_agg(s.store_id ORDER BY s.store_id),'[]')
    FROM bank_account_shops s WHERE s.tenant_id=tid AND s.account_id=a.account_id AND s.is_active)) ELSE '{}' END
  ||CASE WHEN full_view THEN jsonb_build_object('recordedBalance',CASE WHEN o.account_id IS NOT NULL AND
   (o.tracking_started_at AT TIME ZONE 'Asia/Kolkata')::date<=selected THEN o.amount+coalesce((SELECT sum(signed_amount)
   FROM bank_movements b WHERE b.tenant_id=tid AND b.account_id=a.account_id AND b.effective_date<=selected),0) END) ELSE '{}' END j
  FROM bank_accounts a LEFT JOIN bank_openings o ON o.tenant_id=a.tenant_id AND o.account_id=a.account_id
  WHERE a.tenant_id=tid AND (is_owner OR a.is_active AND EXISTS(SELECT 1 FROM bank_account_shops s
    WHERE s.tenant_id=tid AND s.account_id=a.account_id AND s.store_id=sf_store() AND s.is_active))
 ) accounts;
 IF acct IS NULL THEN RETURN result||jsonb_build_object('accounts',rows_json); END IF;
 SELECT value INTO account_json FROM jsonb_array_elements(rows_json) WHERE (value->>'id')::uuid=acct;
 IF account_json IS NULL THEN RAISE EXCEPTION 'Account unavailable in this shop' USING ERRCODE='42501'; END IF;
 IF is_owner THEN result:=result||jsonb_build_object('filterShops',(SELECT coalesce(jsonb_agg(jsonb_build_object(
  'id',s.store_id,'name',s.store_name,'assigned',a.is_active) ORDER BY s.store_id),'[]')
  FROM bank_account_shops a JOIN stores s USING(tenant_id,store_id) WHERE a.tenant_id=tid AND a.account_id=acct),
  'canReactivate',active_count<account_limit AND (SELECT count(*) FROM bank_account_shops WHERE tenant_id=tid AND account_id=acct AND is_active)<=shop_limit); END IF;
 SELECT * INTO opening FROM bank_openings WHERE tenant_id=tid AND account_id=acct;
 IF full_view AND opening.account_id IS NOT NULL AND (opening.tracking_started_at AT TIME ZONE 'Asia/Kolkata')::date<=selected THEN
  balance:=opening.amount+coalesce((SELECT sum(signed_amount) FROM bank_movements WHERE tenant_id=tid AND account_id=acct AND effective_date<=selected),0);
 END IF;
 WITH entries AS (
  -- Consolidated view counts the statement movement ONCE, even across several shops/sources.
  SELECT m.movement_id id,m.effective_date AS day,m.posting_order,m.bank_reference reference,m.entry_kind kind,m.signed_amount amount,
   m.verification_label status,m.verified_at,m.verified_by,NULL::bigint shop_id,NULL::text shop_name,
   (SELECT coalesce(jsonb_agg(jsonb_build_object('id',x.store_id,'name',x.store_name,'amount',x.amount) ORDER BY x.store_id),'[]') FROM (
    SELECT l.store_id,s.store_name,sum(b.signed_amount) amount FROM bank_movement_allocations b
    JOIN bank_payment_links l ON l.tenant_id=b.tenant_id AND l.link_id=b.link_id JOIN stores s ON s.tenant_id=l.tenant_id AND s.store_id=l.store_id
    WHERE b.tenant_id=tid AND b.movement_id=m.movement_id GROUP BY l.store_id,s.store_name) x) shops,
   m.entry_kind='SETTLEMENT' AND NOT EXISTS(SELECT 1 FROM bank_movements r WHERE r.tenant_id=tid AND r.reverses_id=m.movement_id) reversible
  FROM bank_movements m WHERE m.tenant_id=tid AND m.account_id=acct AND is_owner AND shop IS NULL
  UNION ALL
  -- Shop views aggregate only that shop's allocations; never return the full batch amount.
  SELECT m.movement_id,m.effective_date,m.posting_order,m.bank_reference,m.entry_kind,sum(b.signed_amount),
   m.verification_label,m.verified_at,m.verified_by,l.store_id,s.store_name,
   jsonb_build_array(jsonb_build_object('id',l.store_id,'name',s.store_name,'amount',sum(b.signed_amount))),
   NOT is_owner AND m.entry_kind='SETTLEMENT'
    AND NOT EXISTS(SELECT 1 FROM bank_movements r WHERE r.tenant_id=tid AND r.reverses_id=m.movement_id)
    AND NOT EXISTS(SELECT 1 FROM bank_movement_allocations z JOIN bank_payment_links p ON p.tenant_id=z.tenant_id AND p.link_id=z.link_id
      WHERE z.tenant_id=tid AND z.movement_id=m.movement_id AND (p.store_id<>sf_store() OR p.location_id<>sf_location() OR p.business_profile_code<>sf_visible_business_profile()))
  FROM bank_movement_allocations b JOIN bank_movements m ON m.tenant_id=b.tenant_id AND m.movement_id=b.movement_id
  JOIN bank_payment_links l ON l.tenant_id=b.tenant_id AND l.link_id=b.link_id JOIN stores s ON s.tenant_id=l.tenant_id AND s.store_id=l.store_id
  WHERE b.tenant_id=tid AND b.account_id=acct AND (is_owner AND shop IS NOT NULL AND l.store_id=shop
   OR NOT is_owner AND l.store_id=sf_store() AND l.location_id=sf_location() AND l.business_profile_code=sf_visible_business_profile())
  GROUP BY m.movement_id,m.effective_date,m.posting_order,m.bank_reference,m.entry_kind,m.verification_label,m.verified_at,m.verified_by,l.store_id,s.store_name
 ), running AS (
  SELECT *,opening.amount+sum(amount) OVER(ORDER BY day,posting_order,id ROWS UNBOUNDED PRECEDING) running_balance FROM entries
 ), filtered AS (
  SELECT * FROM running WHERE day=selected AND (coalesce(term,'')='' OR position(lower(term) in lower(reference||' '||kind))>0)
 ) SELECT coalesce(sum(amount) FILTER(WHERE amount>0 AND day=selected),0),-coalesce(sum(amount) FILTER(WHERE amount<0 AND day=selected),0),
  (SELECT count(*) FROM filtered),(SELECT coalesce(jsonb_agg(j ORDER BY posting_order DESC),'[]') FROM (
   SELECT posting_order,jsonb_build_object('id',id,'date',day,'reference',reference,'type',kind,'amount',amount,'status',status,
    'verifiedAt',verified_at,'verifiedBy',verified_by,'shopId',shop_id,'shopName',shop_name,'shops',shops,'reversible',reversible)
    ||CASE WHEN full_view AND shop IS NULL THEN jsonb_build_object('runningBalance',running_balance) ELSE '{}' END j
   FROM filtered ORDER BY day DESC,posting_order DESC,id DESC LIMIT 20 OFFSET (page-1)*20) paged)
 INTO money_in,money_out,total,rows_json FROM entries;
 SELECT coalesce(jsonb_agg(jsonb_build_object('id',l.link_id,'kind',l.source_kind,'key',l.source_key,'direction',l.source_direction,
  'amount',l.source_amount,'remaining',l.source_amount-coalesce((SELECT sum(b.signed_amount)*CASE WHEN l.source_direction='IN' THEN 1 ELSE -1 END
  FROM bank_movement_allocations b WHERE b.tenant_id=tid AND b.link_id=l.link_id),0),'date',l.source_date,'shopId',l.store_id) ORDER BY l.recorded_at DESC),'[]') INTO pending_json
 FROM bank_payment_links l WHERE l.tenant_id=tid AND l.account_id=acct AND (is_owner AND (shop IS NULL OR l.store_id=shop)
  OR NOT is_owner AND l.store_id=sf_store() AND l.location_id=sf_location() AND l.business_profile_code=sf_visible_business_profile());
 SELECT coalesce(jsonb_agg(to_jsonb(c)),'[]') INTO candidates FROM (SELECT s.* FROM sf_bank_sources() s
  WHERE EXISTS(SELECT 1 FROM bank_account_shops a WHERE a.tenant_id=tid AND a.account_id=acct AND a.store_id=s.store_id AND a.is_active)
  AND (shop IS NULL OR s.store_id=shop) AND NOT EXISTS(SELECT 1 FROM bank_payment_links l WHERE l.tenant_id=tid AND l.source_kind=s.kind AND l.source_key=s.key)
  ORDER BY s.day DESC,s.kind,s.key LIMIT 50) c;
 result:=result||jsonb_build_object('account',account_json,'moneyIn',money_in,'moneyOut',money_out,'netMovement',money_in-money_out,
  'total',total,'page',page,'transactions',rows_json,'sources',pending_json,'candidates',candidates);
 IF full_view THEN result:=result||jsonb_build_object('recordedBalance',balance,'opening',CASE WHEN opening.account_id IS NOT NULL THEN
  jsonb_build_object('amount',opening.amount,'trackingStartedAt',opening.tracking_started_at,'reference',opening.bank_reference,'initializedAt',opening.initialized_at) END); END IF;
 RETURN result;
END $$;
-- Preserve the old read signature as a restricted compatibility entry, not a bypass.
CREATE OR REPLACE FUNCTION public.sf_bank_read(acct uuid,selected date,page integer,term text)
 RETURNS jsonb LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 SELECT public.sf_bank_read(acct,selected,page,term,NULL::bigint)
$$;
REVOKE ALL ON FUNCTION public.sf_bank_read(uuid,date,integer,text,bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.sf_bank_read(uuid,date,integer,text,bigint) TO stockflow_runtime;
REVOKE ALL ON FUNCTION sf_bank_allowed(text),sf_bank_sources(),sf_bank_write(text,uuid,uuid,jsonb),
 sf_bank_read(uuid,date,integer,text),sf_bank_read(uuid,date,integer,text,bigint) FROM PUBLIC,stockflow_runtime;
GRANT EXECUTE ON FUNCTION sf_bank_write(text,uuid,uuid,jsonb),sf_bank_read(uuid,date,integer,text),
 sf_bank_read(uuid,date,integer,text,bigint) TO stockflow_runtime;
-- No activation, user/role grants, source writes, seed payments or historical import.
COMMIT;
