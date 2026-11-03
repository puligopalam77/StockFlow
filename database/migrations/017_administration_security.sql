-- Run manually after migration 016. Existing passwords and transaction data are preserved.
BEGIN;
ALTER TABLE users ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false,
 ADD COLUMN IF NOT EXISTS failed_login_count integer NOT NULL DEFAULT 0,
 ADD COLUMN IF NOT EXISTS locked_until timestamp,
 ADD COLUMN IF NOT EXISTS last_login_at timestamp,
 ADD COLUMN IF NOT EXISTS password_changed_at timestamp,
 ADD COLUMN IF NOT EXISTS created_by integer REFERENCES users(user_id),
 ADD COLUMN IF NOT EXISTS updated_by integer REFERENCES users(user_id),
 ADD COLUMN IF NOT EXISTS security_version bigint NOT NULL DEFAULT 1;
-- Resolve any case-insensitive duplicate usernames/emails before running this script.
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_username_ci ON users(lower(username));
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email_ci ON users(lower(email)) WHERE email IS NOT NULL AND btrim(email) <> '';
CREATE TABLE IF NOT EXISTS modules (
 module_id serial PRIMARY KEY, module_code varchar(50) UNIQUE NOT NULL,
 module_name varchar(100) NOT NULL, route varchar(150), sort_order integer NOT NULL DEFAULT 0,
 is_active boolean NOT NULL DEFAULT true
);
CREATE TABLE IF NOT EXISTS role_module_permissions (
 role_id integer REFERENCES roles(role_id), module_id integer REFERENCES modules(module_id),
 can_view boolean NOT NULL DEFAULT false, can_create boolean NOT NULL DEFAULT false,
 can_edit boolean NOT NULL DEFAULT false, can_delete boolean NOT NULL DEFAULT false,
 can_approve boolean NOT NULL DEFAULT false, can_export boolean NOT NULL DEFAULT false,
 PRIMARY KEY(role_id,module_id),
 CHECK(can_view OR NOT(can_create OR can_edit OR can_delete OR can_approve OR can_export))
);
CREATE TABLE IF NOT EXISTS user_module_permissions (
 user_id integer REFERENCES users(user_id), module_id integer REFERENCES modules(module_id),
 can_view boolean NOT NULL DEFAULT false, can_create boolean NOT NULL DEFAULT false,
 can_edit boolean NOT NULL DEFAULT false, can_delete boolean NOT NULL DEFAULT false,
 can_approve boolean NOT NULL DEFAULT false, can_export boolean NOT NULL DEFAULT false,
 PRIMARY KEY(user_id,module_id),
 CHECK(can_view OR NOT(can_create OR can_edit OR can_delete OR can_approve OR can_export))
);
INSERT INTO modules(module_code,module_name,route,sort_order) VALUES
 ('DASHBOARD','Dashboard','/',1),('SCAN','Scan product / serial','/Scan',2),
 ('PRODUCTS','Products','/Products',10),('CATEGORIES','Categories','/Categories',11),('BRANDS','Brands','/Brands',12),
 ('SUPPLIERS','Suppliers','/Suppliers',13),('CUSTOMERS','Customers','/Customers',14),
 ('PURCHASES','Purchase orders','/Purchases',20),('STOCK_INFLOW','Stock inflow','/StockInflow',21),
 ('STOCK_OUTFLOW','Stock outflow','/StockOutflow',22),('INVENTORY','Inventory','/Inventory',23),
 ('SALES','Sales / invoices','/Sales',30),('INVOICES','Invoices',NULL,31),
 ('WARRANTY_LOOKUP','Warranty lookup','/Warranty/Lookup',40),('WARRANTY_CLAIMS','Warranty claims','/Warranty/Claims',41),
 ('EXPIRING_WARRANTIES','Expiring warranties','/Warranty/Expiring',42),
 ('SERVICE','Service history','/Service',50),('PENDING_SERVICES','Pending services','/Service/Pending',51),
 ('REPORTS','Reports','/Reports',60),('SEARCH','Search','/Search',61),
 ('COMPANY_SETTINGS','Company settings','/Settings/Company',70),
 ('USER_MANAGEMENT','User management','/Admin/Users',80),('SECURITY','Roles and permissions','/Admin/Roles',81),
 ('AUDIT_LOGS','Audit logs','/Admin/AuditLogs',82)
ON CONFLICT(module_code) DO NOTHING;
INSERT INTO role_module_permissions(role_id,module_id,can_view,can_create,can_edit,can_delete,can_approve,can_export)
SELECT r.role_id,m.module_id,
 r.role_name='Admin' OR m.module_code NOT IN ('SUPPLIERS','PURCHASES','COMPANY_SETTINGS','USER_MANAGEMENT','SECURITY','AUDIT_LOGS') AND (r.role_name='Sales' OR r.role_name='Service' AND m.module_code<>'SALES'),
 r.role_name='Admin' OR r.role_name='Sales' AND m.module_code IN('CUSTOMERS','SALES') OR r.role_name='Service' AND m.module_code IN('SERVICE','WARRANTY_LOOKUP','WARRANTY_CLAIMS'),
 r.role_name='Admin' OR r.role_name='Sales' AND m.module_code='CUSTOMERS' OR r.role_name='Service' AND m.module_code IN('SERVICE','WARRANTY_LOOKUP','WARRANTY_CLAIMS'),
 r.role_name='Admin' AND m.module_code IN('PRODUCTS','CATEGORIES','BRANDS','CUSTOMERS','SUPPLIERS','USER_MANAGEMENT'),
 r.role_name='Admin' OR r.role_name='Service' AND m.module_code IN('SERVICE','WARRANTY_CLAIMS'),
 r.role_name='Admin' OR m.module_code IN('INVOICES','REPORTS') AND r.role_name IN('Sales','Service')
FROM roles r CROSS JOIN modules m WHERE r.role_name IN('Admin','Sales','Service')
ON CONFLICT DO NOTHING;
-- Seed only users with no saved permissions; rerunning never changes customized users.
INSERT INTO user_module_permissions
SELECT u.user_id,p.module_id,p.can_view,p.can_create,p.can_edit,p.can_delete,p.can_approve,p.can_export
FROM users u JOIN role_module_permissions p ON p.role_id=u.role_id
WHERE NOT EXISTS(SELECT 1 FROM user_module_permissions x WHERE x.user_id=u.user_id)
ON CONFLICT DO NOTHING;
CREATE INDEX IF NOT EXISTS ix_audit_security_date ON audit_logs(created_at DESC,audit_log_id DESC);
COMMIT;
