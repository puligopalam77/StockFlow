-- OPTIONAL MANUAL TEMPLATE after migrations 020 and 021. Run as PostgreSQL superuser.
-- Edit every REPLACE_ value. Generate the hash with:
-- dotnet run --project src/StockFlow.Web -- --hash-password
-- This creates a NEW business; do not run to add a store to an existing business.
BEGIN;
DO $$
DECLARE
 code text:='REPLACE_BUSINESS_CODE';
 business text:='REPLACE_BUSINESS_NAME';
 owner_name text:='REPLACE_OWNER_NAME';
 login_name text:='REPLACE_USERNAME';
 password_hash_value text:='REPLACE_ASPNET_PASSWORD_HASH';
 tid bigint; sid bigint; rid int; uid int;
BEGIN
 IF NOT (SELECT rolsuper FROM pg_roles WHERE rolname=current_user) THEN RAISE EXCEPTION 'Run as PostgreSQL superuser'; END IF;
 IF code LIKE 'REPLACE_%' OR business LIKE 'REPLACE_%' OR owner_name LIKE 'REPLACE_%' OR login_name LIKE 'REPLACE_%' OR password_hash_value LIKE 'REPLACE_%' THEN
 RAISE EXCEPTION 'Edit the provisioning placeholders before running'; END IF;
 INSERT INTO tenants(tenant_code,business_name) VALUES(code,business) RETURNING tenant_id INTO tid;
 INSERT INTO stores(tenant_id,store_code,store_name,is_head_office) VALUES(tid,'MAIN','Main store',true) RETURNING store_id INTO sid;
 INSERT INTO store_locations(tenant_id,store_id,location_name,location_type) VALUES(tid,sid,'Main stock','SALES_FLOOR');
 INSERT INTO roles(tenant_id,role_name,description,requires_two_factor) VALUES(tid,'Admin','Business owner and administrators',true) RETURNING role_id INTO rid;
 INSERT INTO users(tenant_id,role_id,full_name,username,password_hash,is_tenant_owner,must_change_password)
 VALUES(tid,rid,owner_name,login_name,password_hash_value,true,true) RETURNING user_id INTO uid;
 INSERT INTO user_store_access(tenant_id,user_id,store_id,is_default) VALUES(tid,uid,sid,true);
 INSERT INTO company_settings(tenant_id,company_settings_id,company_name) VALUES(tid,1,business);
 INSERT INTO role_module_permissions(tenant_id,role_id,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export)
 SELECT tid,rid,module_id,true,true,true,true,true,true FROM modules WHERE is_active;
 INSERT INTO user_module_permissions(tenant_id,user_id,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export)
 SELECT tid,uid,module_id,true,true,true,true,true,true FROM modules WHERE is_active;
 INSERT INTO tenant_security_settings(tenant_id) VALUES(tid);
 INSERT INTO role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
 SELECT tid,rid,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE a.is_active AND m.is_active;
 INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
 SELECT tid,uid,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE a.is_active AND m.is_active;
 -- Employees receive explicit per-user permissions through User Management.
 INSERT INTO roles(tenant_id,role_name,description) VALUES(tid,'Sales','Sales employees'),(tid,'Service','Service employees');
 RAISE NOTICE 'Created tenant %, store %, owner user %',tid,sid,uid;
END $$;
COMMIT;

