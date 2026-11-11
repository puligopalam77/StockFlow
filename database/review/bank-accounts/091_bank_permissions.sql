-- REVIEW DRAFT ONLY. Tested on disposable local fixtures; NOT applied remotely.
-- No user/role grants and no active navigation. See local test report for limits.
BEGIN;
DO $$ BEGIN
 IF current_setting('stockflow.bank_schema_review_approved',true) IS DISTINCT FROM 'yes'
 THEN RAISE EXCEPTION 'Review-only bank permissions: explicit migration approval required'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND rolsuper)
 THEN RAISE EXCEPTION 'A separately approved PostgreSQL superuser maintenance session is required'; END IF;
 IF to_regclass('public.bank_movement_allocations') IS NULL
 THEN RAISE EXCEPTION 'Reviewed bank schema must precede permission metadata'; END IF;
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
