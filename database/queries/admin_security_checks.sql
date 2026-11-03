-- Read-only review after migration 025. No secrets, password hashes or recovery codes.
SELECT u.tenant_id,u.user_id,u.username,r.role_name,u.is_active,u.must_change_password,
       u.two_factor_enabled,(u.require_two_factor OR r.requires_two_factor OR COALESCE(s.require_two_factor,false)) AS two_factor_required,
       u.last_login_at,u.security_version
FROM users u JOIN roles r USING(role_id) LEFT JOIN tenant_security_settings s ON s.tenant_id=u.tenant_id
ORDER BY u.tenant_id,u.user_id;

SELECT p.tenant_id,p.user_id,m.module_code,a.action_code
FROM user_action_permissions p JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id)
WHERE p.is_allowed ORDER BY p.tenant_id,p.user_id,m.sort_order,a.action_code;

-- Missing assignments for non-owner accounts require administrative review.
SELECT tenant_id,user_id,username FROM users u WHERE is_active AND NOT is_tenant_owner
AND NOT EXISTS(SELECT 1 FROM user_store_access a WHERE a.user_id=u.user_id AND a.is_active)
AND NOT EXISTS(SELECT 1 FROM user_location_access l WHERE l.user_id=u.user_id);

-- Confirm converted grants have VIEW dependencies.
SELECT p.user_id,m.module_code,a.action_code FROM user_action_permissions p
JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id)
WHERE p.is_allowed AND a.action_code<>'VIEW' AND NOT EXISTS(
SELECT 1 FROM user_action_permissions v JOIN module_actions va USING(module_action_id)
WHERE v.user_id=p.user_id AND v.is_allowed AND va.module_id=a.module_id AND va.action_code='VIEW');

SELECT tenant_id,lower(btrim(role_name)),count(*) FROM roles
GROUP BY tenant_id,lower(btrim(role_name)) HAVING count(*)>1;
