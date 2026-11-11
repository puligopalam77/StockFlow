-- Disposable local database only. Owner inserts are synthetic schema fixtures,
-- NOT a bank posting API or a production verification workflow.
CREATE TABLE bank_test_results(test_order bigint GENERATED ALWAYS AS IDENTITY,label text NOT NULL);
CREATE TABLE bank_test_gaps(label text NOT NULL);
CREATE FUNCTION bank_test_assert(ok boolean,label text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN IF ok IS DISTINCT FROM true THEN RAISE EXCEPTION 'FAIL: %',label; END IF;
 INSERT INTO bank_test_results(label) VALUES(label); END $$;
CREATE FUNCTION bank_test_expect(wanted text,statement text,label text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE got text; BEGIN
 BEGIN EXECUTE statement;
 EXCEPTION WHEN OTHERS THEN GET STACKED DIAGNOSTICS got=RETURNED_SQLSTATE;
  IF got<>wanted THEN RAISE EXCEPTION 'FAIL %: expected %, got %',label,wanted,got; END IF;
  INSERT INTO bank_test_results(label) VALUES(label); RETURN;
 END; RAISE EXCEPTION 'FAIL %: expected error %, statement succeeded',label,wanted;
END $$;
CREATE FUNCTION bank_test_probe(statement text,label text) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
 BEGIN EXECUTE statement; RAISE EXCEPTION 'Undo accepted synthetic probe' USING ERRCODE='P9999';
 EXCEPTION WHEN SQLSTATE 'P9999' THEN INSERT INTO bank_test_gaps VALUES(label);
 END;
END $$;
GRANT INSERT ON bank_test_results TO stockflow_runtime;
GRANT USAGE ON SEQUENCE bank_test_results_test_order_seq TO stockflow_runtime;

SELECT bank_test_assert((SELECT count(*)=9 AND bool_and(relrowsecurity AND relforcerowsecurity)
 FROM pg_class WHERE relnamespace='public'::regnamespace AND relname IN
 ('bank_accounts','bank_account_secrets','bank_account_shops','bank_openings','bank_payment_links','bank_payment_events','bank_movements','bank_movement_allocations','bank_account_events')),
 'all nine bank tables enable and FORCE RLS');
SELECT bank_test_assert(NOT EXISTS(SELECT 1 FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid WHERE c.relname LIKE 'bank_%'),
 'locked foundation has no permissive bank RLS policies');
SELECT bank_test_assert((SELECT NOT is_active AND NOT show_in_navigation FROM modules WHERE module_code='BANK_ACCOUNTS'),
 'bank module is inactive and absent from active navigation');
SELECT bank_test_assert((SELECT count(*)=12 AND bool_and(is_sensitive) FILTER(WHERE action_code<>'VIEW')
 FROM module_actions a JOIN modules m USING(module_id) WHERE module_code='BANK_ACCOUNTS'),
 'twelve explicit bank actions exist; sensitive operations are marked');
SELECT bank_test_assert(NOT EXISTS(SELECT 1 FROM user_action_permissions p JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id) WHERE module_code='BANK_ACCOUNTS')
 AND NOT EXISTS(SELECT 1 FROM role_action_permissions p JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id) WHERE module_code='BANK_ACCOUNTS'),
 'owner/admin/cashier receive no automatic bank action grants');
SELECT bank_test_assert((SELECT count(*)=1 FROM pg_proc WHERE pronamespace='public'::regnamespace AND proname LIKE 'sf_bank_%'),
 'only immutable trigger function exists; no bank writers or balance APIs activated');
SELECT bank_test_assert((SELECT count(*)=0 FROM bank_payment_links),
 'existing UPI payment was not automatically attributed or backfilled');
SELECT bank_test_assert(NOT EXISTS(SELECT 1 FROM bank_test_source_snapshot s JOIN (
 SELECT 'sales' source,md5(coalesce(jsonb_agg(to_jsonb(s) ORDER BY sale_id)::text,'[]')) digest FROM sales s
 UNION ALL SELECT 'payments',md5(coalesce(jsonb_agg(to_jsonb(p) ORDER BY payment_id)::text,'[]')) FROM payments p
 UNION ALL SELECT 'cash_adjustments',md5(coalesce(jsonb_agg(to_jsonb(c) ORDER BY movement_number)::text,'[]')) FROM cash_adjustments c
 UNION ALL SELECT 'user_action_permissions',md5(coalesce(jsonb_agg(to_jsonb(p) ORDER BY user_id,module_action_id)::text,'[]')) FROM user_action_permissions p
 ) current_sources USING(source) WHERE s.digest<>current_sources.digest),
 'existing synthetic sale/payment/cash history and user action grants unchanged by drafts');

INSERT INTO bank_accounts(tenant_id,account_id,bank_name,display_name,holder_name,account_type,account_last_four,ifsc,created_by)
 VALUES(1,md5('account-a')::uuid,'Synthetic Bank','Synthetic Shared Account','Synthetic Business','CURRENT','0001','TEST0000001',1),
 (1,md5('account-b')::uuid,'Synthetic Bank','Synthetic Second Account','Synthetic Business','CURRENT','0002','TEST0000001',1),
 (2,md5('account-other')::uuid,'Synthetic Bank','Synthetic Other Account','Synthetic Other Business','CURRENT','0003','TEST0000001',4);
INSERT INTO bank_account_secrets VALUES(1,md5('account-a')::uuid,'SYNTHETIC-CIPHERTEXT-NOT-A-REAL-ACCOUNT',NULL,decode(repeat('ab',32),'hex'),1,1);
SELECT bank_test_expect('23505',$q$INSERT INTO bank_account_secrets VALUES(1,md5('account-b')::uuid,'SYNTHETIC',NULL,decode(repeat('ab',32),'hex'),1,1)$q$,
 'duplicate account identity fingerprint rejected within tenant');
SELECT bank_test_expect('23514',$q$INSERT INTO bank_accounts SELECT 1,md5('invalid-ifsc')::uuid,'Synthetic Bank','Invalid IFSC','Synthetic','CURRENT','IN','INR','0004','INVALID',true,1,1,now()$q$,
 'invalid IFSC rejected by schema');
INSERT INTO bank_account_shops(tenant_id,account_id,store_id,is_default_receiving)
 VALUES(1,md5('account-a')::uuid,1,true),(1,md5('account-a')::uuid,2,true),(1,md5('account-b')::uuid,1,false);
SELECT bank_test_assert((SELECT count(*)=1 FROM bank_accounts WHERE account_id=md5('account-a')::uuid)
 AND (SELECT count(*)=2 FROM bank_account_shops WHERE account_id=md5('account-a')::uuid),
 'one authoritative account shared by two shops; multiple accounts supported in one shop');
SELECT bank_test_expect('23505',$q$UPDATE bank_account_shops SET is_default_receiving=true WHERE account_id=md5('account-b')::uuid$q$,
 'second receiving default in same shop rejected');
SELECT bank_test_expect('23514',$q$UPDATE bank_account_shops SET is_active=false WHERE account_id=md5('account-a')::uuid AND store_id=1$q$,
 'inactive assignment cannot retain receiving default');
SELECT bank_test_expect('23503',$q$INSERT INTO bank_account_shops VALUES(1,md5('account-other')::uuid,1,true,false,1)$q$,
 'cross-tenant account assignment rejected by composite foreign key');
SELECT bank_test_expect('23503',$q$INSERT INTO bank_account_shops VALUES(1,md5('account-b')::uuid,3,true,false,1)$q$,
 'cross-tenant shop assignment rejected by composite foreign key');

INSERT INTO bank_openings(tenant_id,account_id,opening_id,amount,tracking_started_at,evidence_description,bank_reference,request_id,request_fingerprint,initialized_by)
 VALUES(1,md5('account-a')::uuid,md5('opening-a')::uuid,100000,'2026-11-10 00:00+05:30','Synthetic statement opening evidence','SYNTHETIC-OPEN-A',md5('opening-request-a')::uuid,decode(repeat('01',32),'hex'),1),
 (1,md5('account-b')::uuid,md5('opening-b')::uuid,0,'2026-11-10 00:00+05:30','Synthetic zero opening evidence','SYNTHETIC-OPEN-B',md5('opening-request-b')::uuid,decode(repeat('02',32),'hex'),1);
SELECT bank_test_assert((SELECT amount=0 FROM bank_openings WHERE account_id=md5('account-b')::uuid),'zero opening is valid and distinct from missing initialization');
SELECT bank_test_expect('23505',$q$INSERT INTO bank_openings SELECT tenant_id,account_id,gen_random_uuid(),amount,tracking_started_at,evidence_description,bank_reference,gen_random_uuid(),request_fingerprint,initialized_by,initialized_at FROM bank_openings WHERE account_id=md5('account-a')::uuid$q$,
 'second opening on shared account rejected');

INSERT INTO bank_payment_links(tenant_id,link_id,account_id,store_id,location_id,business_profile_code,source_kind,source_key,source_direction,source_amount,payment_mode,source_date,recorded_by,request_id,request_fingerprint)
 SELECT 1,md5(x.label)::uuid,md5('account-a')::uuid,1,1,'ELECTRICAL',x.kind,x.key,x.direction,x.amount,x.mode,'2026-11-10',1,md5(x.label||'-request')::uuid,decode(repeat('03',32),'hex')
 FROM (VALUES('receipt','SALE_PAYMENT','1','IN',5000,'UPI'),('supplier','SUPPLIER_PAYMENT','999991','OUT',20000,'BANK'),
 ('expense','EXPENSE_PAYMENT',md5('synthetic-expense')::uuid::text,'OUT',3000,'BANK_TRANSFER'),('pending','SALE_PAYMENT','999992','IN',10000,'UPI')) x(label,kind,key,direction,amount,mode);
INSERT INTO bank_payment_events VALUES(1,md5('pending-event')::uuid,md5('pending')::uuid,'REPORTED_UNVERIFIED','Synthetic unverified customer payment',md5('pending-event-request')::uuid,decode(repeat('04',32),'hex'),1,now());
SELECT bank_test_expect('23505',$q$INSERT INTO bank_payment_links SELECT tenant_id,gen_random_uuid(),account_id,store_id,location_id,business_profile_code,source_kind,source_key,source_direction,source_amount,payment_mode,source_date,source_occurred_at,recorded_by,recorded_at,gen_random_uuid(),request_fingerprint FROM bank_payment_links WHERE link_id=md5('receipt')::uuid$q$,
 'duplicate original payment source attribution rejected');
INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,request_id,request_fingerprint)
 SELECT 1,md5(x.label||'-movement')::uuid,md5('account-a')::uuid,'SETTLEMENT',x.amount,'2026-11-10',x.reference,x.reference,'Synthetic bank statement verification evidence',1,md5(x.label||'-verify-request')::uuid,decode(repeat('05',32),'hex')
 FROM (VALUES('receipt',5000,'SYNTHETIC-UTR-IN'),('supplier',-20000,'SYNTHETIC-UTR-SUP'),('expense',-3000,'SYNTHETIC-UTR-EXP')) x(label,amount,reference);
INSERT INTO bank_movement_allocations SELECT 1,account_id,movement_id,CASE signed_amount WHEN 5000 THEN md5('receipt')::uuid WHEN -20000 THEN md5('supplier')::uuid ELSE md5('expense')::uuid END,signed_amount FROM bank_movements;
SELECT bank_test_assert((SELECT 100000+sum(signed_amount)=82000 FROM bank_movements WHERE account_id=md5('account-a')::uuid),
 'synthetic ledger example: 100000 opening +5000 receipt -20000 supplier -3000 expense =82000');
SELECT bank_test_assert((SELECT sum(signed_amount) FILTER(WHERE signed_amount>0)=5000 AND -sum(signed_amount) FILTER(WHERE signed_amount<0)=23000 FROM bank_movements),
 'daily money in/out exclude opening and 10000 unverified UPI');
INSERT INTO bank_payment_events VALUES(1,md5('failed-event')::uuid,md5('pending')::uuid,'REPORTED_FAILED','Synthetic failed payment evidence',md5('failed-event-request')::uuid,decode(repeat('04',32),'hex'),1,now());
SELECT bank_test_assert((SELECT 100000+sum(signed_amount)=82000 FROM bank_movements),
 'reported failure event does not create or inflate a posted bank movement');
SELECT bank_test_assert((SELECT 100000+sum(signed_amount)=82000 AND count(*) FILTER(WHERE effective_date='2026-11-11')=0 FROM bank_movements WHERE effective_date<='2026-11-11'),
 'next-day owner calculation carries 82000 with zero daily transactions');
SELECT bank_test_assert((SELECT array_agg(balance ORDER BY posting_order)=ARRAY[105000,85000,82000]::numeric[] FROM
 (SELECT posting_order,100000+sum(signed_amount) OVER(ORDER BY effective_date,posting_order,movement_id) balance FROM bank_movements) b),
 'same-date deterministic owner running balances calculated before newest-first display');
SELECT bank_test_expect('23505',$q$INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,request_id,request_fingerprint) VALUES(1,gen_random_uuid(),md5('account-a')::uuid,'SETTLEMENT',5000,'2026-11-10','SYNTHETIC-UTR-IN','SYNTHETIC-UTR-IN','Synthetic duplicate bank reference evidence',1,gen_random_uuid(),decode(repeat('06',32),'hex'))$q$,
 'same account/bank reference cannot post twice under a new request');
SELECT bank_test_expect('23505',$q$INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,request_id,request_fingerprint) VALUES(1,gen_random_uuid(),md5('account-a')::uuid,'SETTLEMENT',5000,'2026-11-10','NEW-SYNTHETIC-UTR','NEW-SYNTHETIC-UTR','Synthetic duplicate request evidence',1,md5('receipt-verify-request')::uuid,decode(repeat('06',32),'hex'))$q$,
 'same bank posting request cannot insert a second movement');
SELECT bank_test_expect('23503',$q$INSERT INTO bank_movement_allocations VALUES(1,md5('account-b')::uuid,md5('receipt-movement')::uuid,md5('pending')::uuid,5000)$q$,
 'cross-account settlement allocation rejected');
INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,reverses_id,request_id,request_fingerprint)
 VALUES(1,md5('expense-reversal')::uuid,md5('account-a')::uuid,'VERIFICATION_REVERSAL',3000,'2026-11-11','SYNTHETIC-UTR-EXP','SYNTHETIC-UTR-EXP','Synthetic mistaken verification correction',1,md5('expense-movement')::uuid,md5('expense-reversal-request')::uuid,decode(repeat('07',32),'hex'));
INSERT INTO bank_movement_allocations VALUES(1,md5('account-a')::uuid,md5('expense-reversal')::uuid,md5('expense')::uuid,3000);
SELECT bank_test_assert((SELECT 100000+sum(signed_amount)=85000 FROM bank_movements),
 'valid full opposite synthetic reversal carries balance to 85000 while preserving original');
SELECT bank_test_expect('23505',$q$INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,reverses_id,request_id,request_fingerprint) VALUES(1,gen_random_uuid(),md5('account-a')::uuid,'VERIFICATION_REVERSAL',3000,'2026-11-11','SYNTHETIC-UTR-EXP','SYNTHETIC-UTR-EXP','Synthetic repeated verification correction',1,md5('expense-movement')::uuid,gen_random_uuid(),decode(repeat('07',32),'hex'))$q$,
 'second reversal of same original rejected');
SELECT bank_test_expect('42501','UPDATE bank_openings SET amount=0','opening amount cannot be edited');
SELECT bank_test_expect('42501','DELETE FROM bank_movements','posted financial history cannot be deleted');
SELECT bank_test_expect('42501','UPDATE bank_movement_allocations SET signed_amount=1','posted allocations cannot be modified');

-- Raw owner probes confirm already-documented activation blockers. Every accepted
-- probe is rolled back by its own subtransaction; no invalid fixture persists.
SELECT bank_test_probe($q$INSERT INTO bank_payment_links SELECT tenant_id,gen_random_uuid(),account_id,2,1,business_profile_code,'SALE_PAYMENT','999999','IN',1,'UPI',source_date,NULL,recorded_by,recorded_at,gen_random_uuid(),request_fingerprint FROM bank_payment_links LIMIT 1$q$,
 'source existence and store/location consistency require an authoritative writer');
SELECT bank_test_probe($q$INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,reverses_id,request_id,request_fingerprint) VALUES(1,gen_random_uuid(),md5('account-a')::uuid,'VERIFICATION_REVERSAL',1,'2026-11-11','SYNTHETIC-WRONG-REVERSAL','SYNTHETIC-WRONG-REVERSAL','Synthetic wrong amount correction probe',1,md5('receipt-movement')::uuid,gen_random_uuid(),decode(repeat('08',32),'hex'))$q$,
 'full-opposite reversal amount and direction require cross-row validation');
SELECT bank_test_probe($q$INSERT INTO bank_movements(tenant_id,movement_id,account_id,entry_kind,signed_amount,effective_date,bank_reference,normalized_bank_reference,evidence_description,verified_by,request_id,request_fingerprint) VALUES(1,gen_random_uuid(),md5('account-a')::uuid,'SETTLEMENT',1,'2026-11-09','SYNTHETIC-BEFORE-OPENING','SYNTHETIC-BEFORE-OPENING','Synthetic pre-opening unallocated evidence',1,gen_random_uuid(),decode(repeat('08',32),'hex'))$q$,
 'opening boundary and complete allocation totals require authoritative validation');
SELECT bank_test_probe($q$UPDATE bank_accounts SET is_active=false WHERE account_id=md5('account-a')::uuid$q$,
 'account deactivation/default cleanup and atomic configuration audit require writer');
SELECT bank_test_assert((SELECT count(*)=4 FROM bank_test_gaps),'four declared financial activation blockers reproduced and isolated probes rolled back');

-- Test runtime denial under owner/admin/cashier/other-tenant identities. Runtime
-- cannot even select full account amounts; no UI hiding is relied upon.
DO $$ DECLARE actor integer; tab text; BEGIN
 FOREACH actor IN ARRAY ARRAY[1,2,3,4] LOOP
  PERFORM set_config('app.tenant_id',CASE WHEN actor=4 THEN '2' ELSE '1' END,false);
  PERFORM set_config('app.user_id',actor::text,false);
  SET LOCAL ROLE stockflow_runtime;
  PERFORM bank_test_assert(NOT sf_transfer_permission('BANK_ACCOUNTS','VERIFY'),'explicit bank VERIFY denied for actor '||actor);
  FOREACH tab IN ARRAY ARRAY['bank_accounts','bank_account_secrets','bank_account_shops','bank_openings','bank_payment_links','bank_payment_events','bank_movements','bank_movement_allocations','bank_account_events'] LOOP
   PERFORM bank_test_expect('42501','SELECT count(*) FROM public.'||tab,'runtime SELECT denied on '||tab||' for actor '||actor);
   PERFORM bank_test_expect('42501','INSERT INTO public.'||tab||' DEFAULT VALUES','runtime INSERT denied on '||tab||' for actor '||actor);
  END LOOP;
  RESET ROLE;
 END LOOP;
END $$;

-- Even a hypothetical accidental SELECT grant cannot bypass deny-all RLS.
-- Test-only grant and all transaction writes are rolled back before finishing.
BEGIN;
GRANT SELECT ON bank_accounts,bank_account_secrets,bank_account_shops,bank_openings,bank_payment_links,bank_payment_events,bank_movements,bank_movement_allocations,bank_account_events TO stockflow_runtime;
SET LOCAL ROLE stockflow_runtime;
DO $$ DECLARE tab text; total bigint; BEGIN
 FOREACH tab IN ARRAY ARRAY['bank_accounts','bank_account_secrets','bank_account_shops','bank_openings','bank_payment_links','bank_payment_events','bank_movements','bank_movement_allocations','bank_account_events'] LOOP
  EXECUTE 'SELECT count(*) FROM public.'||tab INTO total;
  IF total<>0 THEN RAISE EXCEPTION 'Deny-all RLS leaked %',tab; END IF;
 END LOOP;
END $$;
ROLLBACK;
SELECT bank_test_assert(NOT has_table_privilege('stockflow_runtime','public.bank_movements','SELECT'),
 'hypothetical read grant rolled back; FORCE RLS denied all rows even while granted');
SELECT bank_test_assert((SELECT amount=100000 FROM bank_openings WHERE account_id=md5('account-a')::uuid)
 AND (SELECT signed_amount=-3000 FROM bank_movements WHERE movement_id=md5('expense-movement')::uuid),
 'original opening and movement remain unchanged after all rejected operations');
SELECT 'KNOWN BLOCKER: '||label FROM bank_test_gaps ORDER BY label;
