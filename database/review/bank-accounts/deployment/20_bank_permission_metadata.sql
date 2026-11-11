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
DO $$ BEGIN
 IF to_regclass('public.bank_movement_allocations') IS NULL
 OR EXISTS(SELECT 1 FROM public.modules WHERE module_code='BANK_ACCOUNTS')
 THEN RAISE EXCEPTION 'Foundation required and permission metadata must be absent'; END IF;
END $$;
INSERT INTO public.modules(module_code,module_name,route,sort_order,show_in_navigation,navigation_icon,is_active)
 VALUES('BANK_ACCOUNTS','Bank Accounts','/Settings/BankAccounts',65,false,'bank',false);
INSERT INTO public.module_actions(module_id,action_code,action_name,is_sensitive)
 SELECT module_id,a.code,a.label,a.sensitive FROM public.modules CROSS JOIN (VALUES
 ('VIEW','View authorized bank account activity',false),
 ('CREATE','Create bank account',true),('EDIT','Edit bank account configuration',true),
 ('ASSIGN','Assign authorized shops',true),('SET_DEFAULT','Set shop receiving default',true),
 ('INITIALIZE','Initialize account opening once',true),
 ('VERIFY','Manually verify bank movement against bank evidence',true),
 ('REVERSE','Reverse an erroneous bank verification',true),
 ('VIEW_BALANCE','View complete account recorded balance',true),
 ('VIEW_DETAILS','Reveal full account details',true),
 ('DEACTIVATE','Deactivate bank account',true),('EXPORT','Export authorized bank activity',true)
 ) a(code,label,sensitive) WHERE module_code='BANK_ACCOUNTS';
-- Deliberately no INSERT into user_action_permissions / role_action_permissions.
-- No owner/admin implicit VERIFY grant, no COMPANY_SETTINGS/REPORTS compatibility.
-- Future activation must add catalog/route/profile mappings and reviewed grants.
COMMIT;
