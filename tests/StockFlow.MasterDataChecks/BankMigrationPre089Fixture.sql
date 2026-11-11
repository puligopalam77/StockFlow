-- Representative source-contract extension ONLY, NOT a replacement migration.
-- The full repository chain is blocked by empty checked-in migration 049.
-- No production schemas or rows are imported. CashInHandFixture.sql precedes this.
CREATE TABLE tenants(tenant_id bigint PRIMARY KEY,tenant_code text UNIQUE,business_name text,is_active boolean NOT NULL DEFAULT true);
INSERT INTO tenants VALUES(1,'SYNTHETIC-ONE','Synthetic business',true),(2,'SYNTHETIC-TWO','Synthetic other business',true);
ALTER TABLE users ADD COLUMN username text,ADD COLUMN full_name text,ADD COLUMN password_hash text,
 ADD COLUMN is_tenant_owner boolean NOT NULL DEFAULT false;
ALTER TABLE roles ADD COLUMN is_active boolean NOT NULL DEFAULT true;
ALTER TABLE stores ADD COLUMN store_code text,ADD COLUMN store_name text,ADD COLUMN is_active boolean NOT NULL DEFAULT true;
ALTER TABLE store_locations ADD COLUMN location_name text,ADD COLUMN location_type text NOT NULL DEFAULT 'SALES_FLOOR';
ALTER TABLE sales ADD COLUMN store_id bigint,ADD COLUMN subtotal numeric(12,2),ADD COLUMN total_amount numeric(12,2),ADD COLUMN status text,ADD COLUMN created_by integer;
ALTER TABLE payments ADD COLUMN reference_number text,ADD COLUMN created_by integer;
CREATE TABLE modules(module_id serial PRIMARY KEY,module_code varchar(50) UNIQUE NOT NULL,module_name varchar(100) NOT NULL,
 route varchar(150),sort_order integer,show_in_navigation boolean DEFAULT true,navigation_icon varchar(50),is_active boolean DEFAULT true);
CREATE TABLE module_actions(module_action_id bigserial PRIMARY KEY,module_id integer NOT NULL REFERENCES modules,
 action_code varchar(50),action_name varchar(150),is_sensitive boolean NOT NULL,is_active boolean DEFAULT true,UNIQUE(module_id,action_code));
CREATE TABLE user_action_permissions(tenant_id bigint,user_id integer,module_action_id bigint REFERENCES module_actions,is_allowed boolean NOT NULL,
 PRIMARY KEY(user_id,module_action_id),FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id));
CREATE TABLE role_action_permissions(tenant_id bigint,role_id integer,module_action_id bigint REFERENCES module_actions,is_allowed boolean NOT NULL,
 PRIMARY KEY(tenant_id,role_id,module_action_id));
CREATE TABLE user_store_access(tenant_id bigint,user_id integer,store_id bigint,is_active boolean NOT NULL,
 PRIMARY KEY(tenant_id,user_id,store_id));
GRANT SELECT ON users,roles,tenants,modules,module_actions,user_action_permissions,role_action_permissions TO stockflow_runtime;
-- Real explicit-grant semantics from migration 027 (no owner/admin shortcut).
CREATE OR REPLACE FUNCTION sf_transfer_permission(m text,a text) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public,pg_temp AS $$
 WITH wanted AS (
  SELECT CASE WHEN m IN('TRANSFER_DISPATCH','TRANSFER_RECEIVE','TRANSFER_CANCEL') THEN 'STOCK_TRANSFER' ELSE m END module,
         CASE m WHEN 'TRANSFER_DISPATCH' THEN 'DISPATCH' WHEN 'TRANSFER_RECEIVE' THEN 'RECEIVE' WHEN 'TRANSFER_CANCEL' THEN 'CANCEL' ELSE a END act
 )
 SELECT EXISTS(SELECT 1 FROM public.users u JOIN public.roles r ON r.role_id=u.role_id AND r.tenant_id=u.tenant_id
 JOIN public.tenants t ON t.tenant_id=u.tenant_id CROSS JOIN wanted w
 JOIN public.modules m ON m.module_code=w.module AND m.is_active
 JOIN public.module_actions a ON a.module_id=m.module_id AND a.action_code=w.act AND a.is_active
 JOIN public.user_action_permissions p ON p.module_action_id=a.module_action_id AND p.user_id=u.user_id AND p.tenant_id=u.tenant_id AND p.is_allowed
 WHERE u.user_id=public.sf_actor() AND u.tenant_id=public.sf_tenant() AND u.is_active AND r.is_active AND t.is_active
 AND EXISTS(SELECT 1 FROM public.module_actions v JOIN public.user_action_permissions vp ON vp.module_action_id=v.module_action_id AND vp.user_id=u.user_id AND vp.tenant_id=u.tenant_id AND vp.is_allowed
 WHERE v.module_id=m.module_id AND v.action_code='VIEW' AND v.is_active)) $$;
