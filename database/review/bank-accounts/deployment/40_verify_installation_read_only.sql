-- REVIEW ONLY: after approved installation, BEFORE any activation.
-- Requires the completed candidate schema. No writes, sources, secrets or real postings.
BEGIN READ ONLY;
SET LOCAL statement_timeout='15s';
SELECT current_database(),host(inet_server_addr()),inet_server_port(),current_user;
SELECT singleton,configuration_enabled,posting_enabled,max_active_accounts,max_assigned_shops
 FROM public.bank_release_control;
-- Expect false/false and 1/3 after fresh installation.
SELECT m.module_code,m.is_active,m.show_in_navigation,a.action_code,a.is_active AS action_active,a.is_sensitive
 FROM public.modules m JOIN public.module_actions a USING(module_id)
 WHERE m.module_code='BANK_ACCOUNTS' ORDER BY a.action_code;
-- Expect twelve existing keys, inactive module, and zero user/role grants.
SELECT 'user' AS grant_kind,count(*) AS grants FROM public.user_action_permissions p
 JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
 WHERE m.module_code='BANK_ACCOUNTS'
 UNION ALL SELECT 'role',count(*) FROM public.role_action_permissions p
 JOIN public.module_actions a USING(module_action_id) JOIN public.modules m USING(module_id)
 WHERE m.module_code='BANK_ACCOUNTS';
SELECT c.relname,c.relrowsecurity,c.relforcerowsecurity,
 has_table_privilege('stockflow_runtime',c.oid,'SELECT') AS runtime_select,
 has_table_privilege('stockflow_runtime',c.oid,'INSERT,UPDATE,DELETE,TRUNCATE') AS runtime_dml
 FROM pg_class c WHERE c.relnamespace='public'::regnamespace AND c.relkind='r' AND c.relname LIKE 'bank_%'
 ORDER BY c.relname;
-- Expect all eleven tables forced RLS and no direct runtime data access.
SELECT p.oid::regprocedure::text AS signature,p.prosecdef,p.proconfig,
 has_function_privilege('stockflow_runtime',p.oid,'EXECUTE') AS runtime_execute,
 EXISTS(SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
  WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE') AS public_execute
 FROM pg_proc p WHERE p.pronamespace='public'::regnamespace AND p.proname LIKE 'sf_bank_%'
 ORDER BY 1;
-- Only write and both reader signatures should be runtime-callable; no PUBLIC.
SELECT indexname,indexdef FROM pg_indexes WHERE schemaname='public' AND tablename LIKE 'bank_%' ORDER BY indexname;
SELECT conrelid::regclass,conname,convalidated FROM pg_constraint
 WHERE connamespace='public'::regnamespace AND NOT convalidated
 AND conrelid IN(SELECT oid FROM pg_class WHERE relnamespace='public'::regnamespace AND relname LIKE 'bank_%');
-- Expect zero unvalidated bank constraints; compare indexes to reviewed 090 source.
COMMIT;
