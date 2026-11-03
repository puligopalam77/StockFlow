-- MANUAL ONLY: stop StockFlow, back up, apply once after 024, then restart updated app.
-- Existing fixed-column permissions are retained as migration history, not authorization.
BEGIN;
DROP INDEX IF EXISTS ux_users_email_ci;
-- Resolve any legacy case-insensitive role duplicates before applying.
CREATE UNIQUE INDEX ux_roles_tenant_name_ci ON roles(tenant_id,lower(btrim(role_name)));
ALTER TABLE roles ADD COLUMN requires_two_factor boolean NOT NULL DEFAULT false;
UPDATE roles SET requires_two_factor=true WHERE lower(role_name) IN('admin','store manager');
ALTER TABLE users ADD COLUMN require_two_factor boolean NOT NULL DEFAULT false,
 ADD COLUMN two_factor_enabled boolean NOT NULL DEFAULT false,
 ADD COLUMN two_factor_secret_encrypted text,
 ADD COLUMN two_factor_pending_encrypted text,
 ADD COLUMN two_factor_pending_until timestamptz,
 ADD COLUMN two_factor_enabled_at timestamptz,
 ADD COLUMN two_factor_last_step bigint,
 ADD COLUMN two_factor_failures integer NOT NULL DEFAULT 0,
 ADD COLUMN two_factor_locked_until timestamptz;
CREATE TABLE tenant_security_settings(
 tenant_id bigint PRIMARY KEY DEFAULT sf_tenant() REFERENCES tenants(tenant_id),
 minimum_password_length integer NOT NULL DEFAULT 12 CHECK(minimum_password_length BETWEEN 12 AND 128),
 require_two_factor boolean NOT NULL DEFAULT false,
 max_login_attempts integer NOT NULL DEFAULT 5 CHECK(max_login_attempts BETWEEN 3 AND 10),
 lockout_minutes integer NOT NULL DEFAULT 15 CHECK(lockout_minutes BETWEEN 5 AND 60));
INSERT INTO tenant_security_settings(tenant_id) SELECT tenant_id FROM tenants;
CREATE TABLE two_factor_recovery_codes(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(), user_id integer NOT NULL,
 code_hash varchar(64) NOT NULL,used_at timestamptz,
 PRIMARY KEY(user_id,code_hash), FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id));
CREATE TABLE module_actions(
 module_action_id bigserial PRIMARY KEY,module_id integer NOT NULL REFERENCES modules(module_id),
 action_code varchar(50) NOT NULL,action_name varchar(100) NOT NULL,
 is_sensitive boolean NOT NULL DEFAULT false,is_active boolean NOT NULL DEFAULT true,UNIQUE(module_id,action_code));
CREATE TABLE user_action_permissions(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),user_id integer NOT NULL,module_action_id bigint NOT NULL REFERENCES module_actions(module_action_id),
 is_allowed boolean NOT NULL DEFAULT false,assigned_by integer,assigned_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(user_id,module_action_id),FOREIGN KEY(tenant_id,user_id) REFERENCES users(tenant_id,user_id),FOREIGN KEY(tenant_id,assigned_by) REFERENCES users(tenant_id,user_id));
CREATE TABLE role_action_permissions(
 tenant_id bigint NOT NULL DEFAULT sf_tenant(),role_id integer NOT NULL,module_action_id bigint NOT NULL REFERENCES module_actions(module_action_id),
 is_allowed boolean NOT NULL DEFAULT false,assigned_by integer,assigned_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(role_id,module_action_id),FOREIGN KEY(role_id) REFERENCES roles(role_id),FOREIGN KEY(tenant_id,assigned_by) REFERENCES users(tenant_id,user_id));
-- Enforce tenant-consistent roles as well as users.
CREATE UNIQUE INDEX ux_role_tenant_identity ON roles(tenant_id,role_id);
ALTER TABLE role_action_permissions ADD FOREIGN KEY(tenant_id,role_id) REFERENCES roles(tenant_id,role_id);
INSERT INTO modules(module_code,module_name,route,sort_order) VALUES
 ('PRODUCT_VARIANTS','Product variants',NULL,15),('BARCODES','Barcode management',NULL,16),
 ('STOCK_ADJUSTMENT','Stock adjustment',NULL,24),('ROLE_MANAGEMENT','Roles and permissions','/Admin/Roles',81);
UPDATE modules SET module_name='Security settings',route='/Admin/Security' WHERE module_code='SECURITY';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='DASHBOARD';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SCAN';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'SCAN',replace(initcap(lower('SCAN')),'_',' '),'SCAN' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SCAN';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SEARCH';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCT_VARIANTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCT_VARIANTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCT_VARIANTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PRODUCT_VARIANTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BARCODES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'GENERATE',replace(initcap(lower('GENERATE')),'_',' '),'GENERATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BARCODES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'PRINT',replace(initcap(lower('PRINT')),'_',' '),'PRINT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BARCODES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'REPRINT',replace(initcap(lower('REPRINT')),'_',' '),'REPRINT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BARCODES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CATEGORIES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CATEGORIES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CATEGORIES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CATEGORIES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BRANDS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BRANDS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BRANDS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='BRANDS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SUPPLIERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SUPPLIERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SUPPLIERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SUPPLIERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SUPPLIERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CUSTOMERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CUSTOMERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CUSTOMERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CUSTOMERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='CUSTOMERS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PURCHASES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PURCHASES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PURCHASES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'APPROVE',replace(initcap(lower('APPROVE')),'_',' '),'APPROVE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PURCHASES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PURCHASES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_INFLOW';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'RECEIVE',replace(initcap(lower('RECEIVE')),'_',' '),'RECEIVE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_INFLOW';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_INFLOW';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_OUTFLOW';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_OUTFLOW';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='INVENTORY';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='INVENTORY';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_ADJUSTMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'ADJUST',replace(initcap(lower('ADJUST')),'_',' '),'ADJUST' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_ADJUSTMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'APPROVE',replace(initcap(lower('APPROVE')),'_',' '),'APPROVE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DISPATCH',replace(initcap(lower('DISPATCH')),'_',' '),'DISPATCH' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'RECEIVE',replace(initcap(lower('RECEIVE')),'_',' '),'RECEIVE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CANCEL',replace(initcap(lower('CANCEL')),'_',' '),'CANCEL' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STOCK_TRANSFER';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SALES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SALES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SALES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='INVOICES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'PRINT',replace(initcap(lower('PRINT')),'_',' '),'PRINT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='INVOICES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='INVOICES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_LOOKUP';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_LOOKUP';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_LOOKUP';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_CLAIMS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_CLAIMS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'UPDATE',replace(initcap(lower('UPDATE')),'_',' '),'UPDATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_CLAIMS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'APPROVE',replace(initcap(lower('APPROVE')),'_',' '),'APPROVE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_CLAIMS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'RESOLVE',replace(initcap(lower('RESOLVE')),'_',' '),'RESOLVE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='WARRANTY_CLAIMS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='EXPIRING_WARRANTIES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='EXPIRING_WARRANTIES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SERVICE';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SERVICE';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'UPDATE',replace(initcap(lower('UPDATE')),'_',' '),'UPDATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SERVICE';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'ASSIGN',replace(initcap(lower('ASSIGN')),'_',' '),'ASSIGN' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SERVICE';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DELIVER',replace(initcap(lower('DELIVER')),'_',' '),'DELIVER' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SERVICE';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='PENDING_SERVICES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='REPORTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='REPORTS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='COMPANY_SETTINGS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='COMPANY_SETTINGS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STORES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STORES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'MANAGE_ACCESS',replace(initcap(lower('MANAGE_ACCESS')),'_',' '),'MANAGE_ACCESS' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='STORES';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'CREATE',replace(initcap(lower('CREATE')),'_',' '),'CREATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EDIT',replace(initcap(lower('EDIT')),'_',' '),'EDIT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'DEACTIVATE',replace(initcap(lower('DEACTIVATE')),'_',' '),'DEACTIVATE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'RESET_PASSWORD',replace(initcap(lower('RESET_PASSWORD')),'_',' '),'RESET_PASSWORD' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'RESET_AUTHENTICATOR',replace(initcap(lower('RESET_AUTHENTICATOR')),'_',' '),'RESET_AUTHENTICATOR' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'MANAGE_PERMISSIONS',replace(initcap(lower('MANAGE_PERMISSIONS')),'_',' '),'MANAGE_PERMISSIONS' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='USER_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='ROLE_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'MANAGE_PERMISSIONS',replace(initcap(lower('MANAGE_PERMISSIONS')),'_',' '),'MANAGE_PERMISSIONS' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='ROLE_MANAGEMENT';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SECURITY';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'MANAGE',replace(initcap(lower('MANAGE')),'_',' '),'MANAGE' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='SECURITY';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'VIEW',replace(initcap(lower('VIEW')),'_',' '),'VIEW' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='AUDIT_LOGS';
INSERT INTO module_actions(module_id,action_code,action_name,is_sensitive) SELECT module_id,'EXPORT',replace(initcap(lower('EXPORT')),'_',' '),'EXPORT' IN('DEACTIVATE','APPROVE','DISPATCH','RECEIVE','CANCEL','ADJUST','RESET_PASSWORD','RESET_AUTHENTICATOR','MANAGE_PERMISSIONS','MANAGE','MANAGE_ACCESS') FROM modules WHERE module_code='AUDIT_LOGS';

-- Migrate only actions corresponding to previously granted operations.
-- New narrow actions are split from their former broader grant. No role fallback.
DO $$ DECLARE kind text; key_name text; BEGIN
 FOREACH kind IN ARRAY ARRAY['user','role'] LOOP
 key_name:=kind||'_id';
 EXECUTE format($q$
 INSERT INTO %I(tenant_id,%I,module_action_id,is_allowed)
 SELECT p.tenant_id,p.%I,a.module_action_id,true
 FROM module_actions a JOIN modules m ON m.module_id=a.module_id
 JOIN modules old ON old.module_code=CASE
   WHEN m.module_code IN('PRODUCT_VARIANTS','BARCODES') THEN 'PRODUCTS'
   WHEN m.module_code='ROLE_MANAGEMENT' THEN 'SECURITY'
   WHEN m.module_code='STOCK_ADJUSTMENT' THEN 'INVENTORY'
   WHEN m.module_code='STOCK_INFLOW' AND a.action_code='RECEIVE' THEN 'PURCHASES'
   WHEN m.module_code='STOCK_TRANSFER' AND a.action_code IN('DISPATCH','RECEIVE','CANCEL') THEN 'TRANSFER_'||a.action_code
   ELSE m.module_code END
 JOIN %I p ON p.module_id=old.module_id
 WHERE p.can_view AND CASE a.action_code
   WHEN 'VIEW' THEN true WHEN 'SCAN' THEN true WHEN 'PRINT' THEN true WHEN 'REPRINT' THEN true
   WHEN 'CREATE' THEN p.can_create WHEN 'EDIT' THEN p.can_edit WHEN 'UPDATE' THEN p.can_edit
   WHEN 'GENERATE' THEN p.can_edit WHEN 'ASSIGN' THEN p.can_edit
   WHEN 'DEACTIVATE' THEN p.can_delete WHEN 'EXPORT' THEN p.can_export
   WHEN 'RESET_PASSWORD' THEN p.can_edit WHEN 'RESET_AUTHENTICATOR' THEN p.can_edit
   WHEN 'MANAGE' THEN p.can_edit WHEN 'MANAGE_ACCESS' THEN p.can_edit
   WHEN 'MANAGE_PERMISSIONS' THEN p.can_edit
   WHEN 'RESOLVE' THEN p.can_approve
   ELSE p.can_approve END
 $q$,kind||'_action_permissions',key_name,key_name,kind||'_module_permissions');
 END LOOP;
END $$;
-- Previously assigning user permissions also required SECURITY/EDIT.
DELETE FROM user_action_permissions p USING module_actions a,modules m
WHERE p.module_action_id=a.module_action_id AND a.module_id=m.module_id
 AND m.module_code='USER_MANAGEMENT' AND a.action_code='MANAGE_PERMISSIONS'
 AND NOT EXISTS(SELECT 1 FROM user_module_permissions old JOIN modules s USING(module_id)
 WHERE old.user_id=p.user_id AND s.module_code='SECURITY' AND old.can_view AND old.can_edit);
-- Dependent actions must always include VIEW even if former modules differed.
INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed)
SELECT DISTINCT p.tenant_id,p.user_id,v.module_action_id,true FROM user_action_permissions p
JOIN module_actions a USING(module_action_id) JOIN module_actions v ON v.module_id=a.module_id AND v.action_code='VIEW'
ON CONFLICT DO NOTHING;
INSERT INTO role_action_permissions(tenant_id,role_id,module_action_id,is_allowed)
SELECT DISTINCT p.tenant_id,p.role_id,v.module_action_id,true FROM role_action_permissions p
JOIN module_actions a USING(module_action_id) JOIN module_actions v ON v.module_id=a.module_id AND v.action_code='VIEW'
ON CONFLICT DO NOTHING;
UPDATE modules SET is_active=false WHERE module_code IN('TRANSFER_DISPATCH','TRANSFER_RECEIVE','TRANSFER_CANCEL');
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['tenant_security_settings','two_factor_recovery_codes','user_action_permissions','role_action_permissions'] LOOP
 EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',tab);
 EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',tab);
 EXECUTE format('CREATE POLICY tenant_isolation ON %I USING(tenant_id=sf_tenant()) WITH CHECK(tenant_id=sf_tenant())',tab);
 EXECUTE format('GRANT SELECT,INSERT,UPDATE,DELETE ON %I TO stockflow_runtime',tab);
 END LOOP;
END $$;
GRANT SELECT ON module_actions TO stockflow_runtime;
CREATE FUNCTION sf_action_view_dependency() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE target integer; missing boolean;
BEGIN
 target:=COALESCE((to_jsonb(NEW)->>TG_ARGV[0])::integer,(to_jsonb(OLD)->>TG_ARGV[0])::integer);
 EXECUTE format('SELECT EXISTS(SELECT 1 FROM %I p JOIN module_actions a USING(module_action_id)
 WHERE p.%I=$1 AND p.is_allowed AND a.action_code<>''VIEW'' AND NOT EXISTS(
 SELECT 1 FROM %I v JOIN module_actions va USING(module_action_id)
 WHERE v.%I=p.%I AND v.is_allowed AND va.module_id=a.module_id AND va.action_code=''VIEW''))',
 TG_TABLE_NAME,TG_ARGV[0],TG_TABLE_NAME,TG_ARGV[0],TG_ARGV[0]) INTO missing USING target;
 IF missing THEN RAISE EXCEPTION 'View permission is required for module actions' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER user_action_view_dependency AFTER INSERT OR UPDATE OR DELETE ON user_action_permissions
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sf_action_view_dependency('user_id');
CREATE CONSTRAINT TRIGGER role_action_view_dependency AFTER INSERT OR UPDATE OR DELETE ON role_action_permissions
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION sf_action_view_dependency('role_id');
-- New policy enforcement and action migration invalidate all existing sessions.
UPDATE users SET security_version=security_version+1;
COMMIT;

