-- OPTIONAL, READ ONLY. Run as administrator after baseline through 029.
-- Reviews all businesses. No accounts, roles, permissions or stock are changed.
BEGIN TRANSACTION READ ONLY;

-- Informational: active modules/actions offered by the current application baseline.
SELECT m.module_code,m.module_name,a.action_code,a.action_name,a.is_sensitive
FROM modules m JOIN module_actions a USING(module_id) WHERE m.is_active AND a.is_active
ORDER BY m.sort_order,m.module_id,a.module_action_id;

-- Integrity checks: zero rows expected. Review historical exceptions before piloting.
SELECT module_code,module_name FROM modules
WHERE is_active AND module_code IN('TRANSFER_DISPATCH','TRANSFER_RECEIVE','TRANSFER_CANCEL');

-- Latest workflow actions must exist and be active; they are not granted by this query.
WITH required(module_code,action_code) AS (VALUES
 ('STOCK_INFLOW','VIEW'),('STOCK_INFLOW','RECEIVE'),('SALES','VIEW'),('SALES','CREATE'),
 ('SCAN','VIEW'),('SCAN','SCAN'),('BARCODES','VIEW'),('BARCODES','GENERATE'),('BARCODES','PRINT'),('BARCODES','REPRINT'),
 ('PRODUCT_VARIANTS','VIEW'),('PRODUCT_VARIANTS','CREATE'),('INVENTORY','VIEW'),
 ('STOCK_ADJUSTMENT','VIEW'),('STOCK_ADJUSTMENT','ADJUST'),('REPORTS','VIEW'),
 ('STOCK_TRANSFER','VIEW'),('STOCK_TRANSFER','CREATE'),('STOCK_TRANSFER','APPROVE'),
 ('STOCK_TRANSFER','DISPATCH'),('STOCK_TRANSFER','RECEIVE'),('STOCK_TRANSFER','CANCEL'),
 ('USER_MANAGEMENT','MANAGE_PERMISSIONS'),('ROLE_MANAGEMENT','MANAGE_PERMISSIONS'),('STORES','MANAGE_ACCESS'))
SELECT r.* FROM required r LEFT JOIN modules m ON m.module_code=r.module_code AND m.is_active
LEFT JOIN module_actions a ON a.module_id=m.module_id AND a.action_code=r.action_code AND a.is_active
WHERE a.module_action_id IS NULL;

SELECT p.tenant_id,p.user_id,m.module_code,a.action_code FROM user_action_permissions p
JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id)
WHERE p.is_allowed AND a.action_code<>'VIEW' AND a.is_active AND m.is_active
 AND NOT EXISTS(SELECT 1 FROM user_action_permissions v JOIN module_actions va USING(module_action_id)
  WHERE v.tenant_id=p.tenant_id AND v.user_id=p.user_id AND v.is_allowed AND va.module_id=m.module_id AND va.action_code='VIEW' AND va.is_active);

SELECT p.tenant_id,p.role_id,m.module_code,a.action_code FROM role_action_permissions p
JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id)
WHERE p.is_allowed AND a.action_code<>'VIEW' AND a.is_active AND m.is_active
 AND NOT EXISTS(SELECT 1 FROM role_action_permissions v JOIN module_actions va USING(module_action_id)
  WHERE v.tenant_id=p.tenant_id AND v.role_id=p.role_id AND v.is_allowed AND va.module_id=m.module_id AND va.action_code='VIEW' AND va.is_active);

-- Staff need an active assigned store with a sales floor, or warehouse-only access.
-- Admin/owner review scope is validated separately by application checks.
SELECT u.tenant_id,u.user_id,u.username FROM users u JOIN roles r USING(tenant_id,role_id)
WHERE u.is_active AND r.is_active AND NOT u.is_tenant_owner
 AND NOT EXISTS(SELECT 1 FROM user_store_access a JOIN stores s USING(tenant_id,store_id)
  JOIN store_locations l ON l.tenant_id=s.tenant_id AND l.store_id=s.store_id
  WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.is_active AND s.is_active AND l.is_active)
 AND NOT EXISTS(SELECT 1 FROM user_location_access a JOIN store_locations l USING(tenant_id,location_id)
  WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND l.is_active AND l.location_type<>'TRANSIT');

SELECT u.tenant_id,u.user_id,u.username,r.role_name FROM users u JOIN roles r USING(tenant_id,role_id)
WHERE u.is_active AND r.is_active AND NOT u.is_tenant_owner AND lower(btrim(r.role_name))<>'admin'
 AND NOT EXISTS(SELECT 1 FROM user_store_access a JOIN stores s USING(tenant_id,store_id)
  JOIN store_locations l ON l.tenant_id=s.tenant_id AND l.store_id=s.store_id
  WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.is_active AND s.is_active
   AND l.is_active AND l.location_type='SALES_FLOOR'
   AND (a.is_default OR (SELECT count(*) FROM user_store_access x JOIN stores sx USING(tenant_id,store_id)
    WHERE x.tenant_id=u.tenant_id AND x.user_id=u.user_id AND x.is_active AND sx.is_active)=1))
 AND NOT (
  NOT EXISTS(SELECT 1 FROM user_store_access a JOIN stores s USING(tenant_id,store_id)
   JOIN store_locations sl ON sl.tenant_id=s.tenant_id AND sl.store_id=s.store_id
   WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.is_active AND s.is_active AND sl.is_active)
  AND EXISTS(SELECT 1 FROM user_location_access a JOIN store_locations l USING(tenant_id,location_id)
   WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND l.is_active AND l.store_id IS NULL AND l.location_type='WAREHOUSE'));

-- Informational: account readiness (no credentials/authenticator secrets are selected).
SELECT u.tenant_id,u.user_id,u.username,r.role_name,u.is_active,u.must_change_password,u.security_version,
 u.two_factor_enabled,u.require_two_factor,r.requires_two_factor,
 (SELECT count(*) FROM user_action_permissions p WHERE p.tenant_id=u.tenant_id AND p.user_id=u.user_id AND p.is_allowed) AS assigned_actions,
 (SELECT s.store_name FROM user_store_access a JOIN stores s USING(tenant_id,store_id)
  WHERE a.tenant_id=u.tenant_id AND a.user_id=u.user_id AND a.is_active AND a.is_default) AS default_store
FROM users u JOIN roles r USING(tenant_id,role_id) ORDER BY u.tenant_id,u.user_id;
ROLLBACK;
