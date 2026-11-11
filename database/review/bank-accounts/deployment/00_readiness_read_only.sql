-- REVIEW ONLY. No connection is made by this file's preparation.
-- Execute only after separate approval of read-only access to a named target.
-- Use the intended application login to verify authentication, then a separately
-- authorized read-only catalog reviewer if permissions differ. No payment/secret rows.
BEGIN READ ONLY;
SET LOCAL statement_timeout='15s';
SELECT current_database() AS database_name,host(inet_server_addr()) AS server_address,
 inet_server_port() AS server_port,current_user AS connected_role,session_user,
 current_setting('server_version') AS server_version,
 current_setting('cluster_name') AS cluster_name,pg_is_in_recovery() AS standby,
 has_database_privilege(current_user,current_database(),'CONNECT') AS can_connect,
 CASE WHEN has_function_privilege('pg_catalog.pg_control_system()','EXECUTE')
 THEN (SELECT system_identifier::text FROM pg_control_system()) END AS system_identifier;
SELECT ssl,version AS tls_version FROM pg_stat_ssl WHERE pid=pg_backend_pid();

-- Catalog clues only: presence does NOT prove application/migration history.
SELECT n.nspname,c.relname,c.relkind FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname NOT IN('pg_catalog','information_schema') AND c.relname ILIKE '%migrat%'
 ORDER BY 1,2;
SELECT name,to_regclass('public.'||name) IS NOT NULL AS present
 FROM unnest(ARRAY['tenants','users','roles','stores','store_locations','user_store_access',
 'user_location_access','modules','module_actions','user_action_permissions','role_action_permissions',
 'auth_protection_keys','sale_items','purchase_items','sales','payments','purchases','supplier_payments',
 'service_requests','service_payments','sale_returns','expenses','expense_payments',
 'cash_adjustments','cash_opening_expense_reviews','bank_accounts','bank_account_secrets',
 'bank_account_shops','bank_openings','bank_payment_links','bank_payment_events',
 'bank_movements','bank_movement_allocations','bank_account_events',
 'bank_release_control','bank_requests']) AS name;
SELECT signature,to_regprocedure(signature) IS NOT NULL AS present
 FROM unnest(ARRAY['public.sf_snapshot_retail_variant()','public.sf_cash_entry_reason_guard()',
 'public.sf_tenant()','public.sf_actor()','public.sf_owner()','public.sf_store()','public.sf_location()',
 'public.sf_shop_member(bigint)','public.sf_can_location(bigint)','public.sf_visible_business_profile()',
 'public.sf_transfer_permission(text,text)','public.sf_read_protection_keys()',
 'public.sf_write_protection_key(text,text)','public.sf_bank_review_immutable()',
 'public.sf_bank_allowed(text)','public.sf_bank_sources()',
 'public.sf_bank_write(text,uuid,uuid,jsonb)','public.sf_bank_read(uuid,date,integer,text)',
 'public.sf_bank_read(uuid,date,integer,text,bigint)']) AS signature;
SELECT p.oid::regprocedure::text AS signature,pg_get_userbyid(p.proowner) AS owner,
 p.prosecdef AS security_definer,p.proconfig,p.proacl,
 md5(pg_get_functiondef(p.oid)) AS definition_md5
 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
 WHERE n.nspname='public' AND p.proname IN('sf_snapshot_retail_variant','sf_tenant','sf_actor',
 'sf_owner','sf_store','sf_location','sf_shop_member','sf_can_location','sf_visible_business_profile',
 'sf_transfer_permission','sf_cash_entry_reason_guard','sf_bank_review_immutable',
 'sf_bank_allowed','sf_bank_sources','sf_bank_write','sf_bank_read') ORDER BY 1;
SELECT table_name,column_name,data_type,udt_name,is_nullable FROM information_schema.columns
 WHERE table_schema='public' AND table_name IN('payments','sales','supplier_payments','purchases',
 'service_payments','service_requests','sale_returns','expense_payments','expenses','sale_items','purchase_items')
 ORDER BY table_name,ordinal_position;
SELECT tgrelid::regclass::text AS table_name,tgname,tgenabled,pg_get_triggerdef(oid) AS definition
 FROM pg_trigger WHERE NOT tgisinternal AND (tgname='zz_retail_variant_snapshot'
 OR tgname LIKE 'bank_%' OR tgname='cash_entry_reason_guard') ORDER BY 1,2;
SELECT rolname,rolsuper,rolbypassrls,rolcanlogin,rolinherit,
 has_schema_privilege(rolname,'public','CREATE') AS can_create_in_public
 FROM pg_roles WHERE rolname IN(current_user,'stockflow_runtime');
SELECT pg_get_userbyid(roleid) AS role_name,pg_get_userbyid(member) AS member_name,admin_option
 FROM pg_auth_members WHERE roleid=(SELECT oid FROM pg_roles WHERE rolname='stockflow_runtime')
 OR member=(SELECT oid FROM pg_roles WHERE rolname='stockflow_runtime');
SELECT c.relname,c.relrowsecurity,c.relforcerowsecurity,pg_get_userbyid(c.relowner) AS owner,c.relacl
 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname='public' AND c.relname LIKE 'bank_%' ORDER BY c.relname;
SELECT tablename,policyname,roles,cmd,qual,with_check FROM pg_policies
 WHERE schemaname='public' AND tablename LIKE 'bank_%' ORDER BY tablename,policyname;
SELECT tablename,indexname,indexdef FROM pg_indexes
 WHERE schemaname='public' AND tablename LIKE 'bank_%' ORDER BY tablename,indexname;
SELECT conrelid::regclass::text AS table_name,conname,contype,convalidated,pg_get_constraintdef(oid) AS definition
 FROM pg_constraint WHERE connamespace='public'::regnamespace
 AND conrelid IN(SELECT oid FROM pg_class WHERE relnamespace='public'::regnamespace AND relname LIKE 'bank_%')
 ORDER BY 1,2;

-- Existing business identity/permissions only; never treat role name as Owner proof.
-- Optional reviewer-selected stockflow.review_tenant_id / review_owner_id limit rows.
-- If unset, return NO actor/grant rows; do not enumerate every tenant's permissions.
SELECT u.tenant_id,u.user_id,u.is_tenant_owner,u.is_active AS user_active,
 t.is_active AS tenant_active,r.role_id,r.role_name,r.is_active AS role_active
 FROM public.users u JOIN public.tenants t USING(tenant_id)
 JOIN public.roles r USING(tenant_id,role_id)
 WHERE u.tenant_id=nullif(current_setting('stockflow.review_tenant_id',true),'')::bigint
 AND u.user_id=nullif(current_setting('stockflow.review_owner_id',true),'')::integer;
SELECT m.module_code,m.route,m.is_active,m.show_in_navigation,a.action_code,a.is_active AS action_active,a.is_sensitive
 FROM public.modules m LEFT JOIN public.module_actions a USING(module_id)
 WHERE m.module_code='BANK_ACCOUNTS' ORDER BY a.action_code;
SELECT p.tenant_id,p.user_id,a.action_code,p.is_allowed
 FROM public.user_action_permissions p JOIN public.module_actions a USING(module_action_id)
 JOIN public.modules m USING(module_id)
 WHERE m.module_code='BANK_ACCOUNTS'
 AND p.tenant_id=nullif(current_setting('stockflow.review_tenant_id',true),'')::bigint
 AND p.user_id=nullif(current_setting('stockflow.review_owner_id',true),'')::integer
 ORDER BY a.action_code;
COMMIT;
