-- READ-ONLY preflight: these queries must return no rows before migration 017.
SELECT lower(username) AS duplicate_username,count(*) FROM users GROUP BY lower(username) HAVING count(*)>1;
SELECT lower(email) AS duplicate_email,count(*) FROM users WHERE email IS NOT NULL AND btrim(email)<>'' GROUP BY lower(email) HAVING count(*)>1;

-- Run these only AFTER database/migrations/017_administration_security.sql.
-- Account overview: deliberately excludes password hashes.
SELECT u.user_id,u.username,u.full_name,r.role_name,u.is_active,u.must_change_password,
       u.failed_login_count,u.locked_until,u.last_login_at,u.password_changed_at,u.security_version
FROM users u JOIN roles r USING(role_id) ORDER BY u.user_id;

-- Effective permissions (per-user rows, not role-template fallback).
SELECT u.username,m.module_code,p.can_view,p.can_create,p.can_edit,p.can_delete,p.can_approve,p.can_export
FROM user_module_permissions p JOIN users u USING(user_id) JOIN modules m USING(module_id)
ORDER BY u.username,m.sort_order;

-- Confirm at least one active administrator retains account/permission editing.
SELECT u.user_id,u.username FROM users u JOIN roles r USING(role_id)
WHERE u.is_active AND r.is_active AND r.role_name='Admin'
AND (SELECT count(*) FROM user_module_permissions p JOIN modules m USING(module_id)
     WHERE p.user_id=u.user_id AND m.module_code IN('USER_MANAGEMENT','SECURITY') AND p.can_view AND p.can_edit)=2;

-- Recent administration changes. No password/hash fields are audited.
SELECT a.audit_log_id,u.username,a.action_name,a.record_id,a.old_values,a.new_values,a.created_at
FROM audit_logs a LEFT JOIN users u USING(user_id)
WHERE a.module_name='Administration' ORDER BY a.created_at DESC,a.audit_log_id DESC LIMIT 100;

-- Make routine user, password and permission changes through /Admin/Users so
-- validation, session invalidation, last-administrator protection and auditing apply.
