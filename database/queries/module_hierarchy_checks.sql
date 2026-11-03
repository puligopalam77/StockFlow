-- OPTIONAL, READ ONLY. Run after migration 029. Zero-row sections indicate a valid hierarchy.
BEGIN TRANSACTION READ ONLY;

-- Complete active module/submodule catalog used by the application.
SELECT m.module_id,m.module_name,m.module_code,m.is_active,p.module_name AS parent_module,
 m.route,m.show_in_navigation,m.sort_order
FROM modules m LEFT JOIN modules p ON p.module_id=m.parent_module_id
ORDER BY COALESCE(p.sort_order,m.sort_order),p.module_id NULLS FIRST,m.sort_order,m.module_id;

-- Every visible page must have at least one permission requirement. Parent menu groups have no route.
SELECT m.module_id,m.module_code,m.module_name,m.route
FROM modules m
WHERE m.is_active AND m.show_in_navigation AND m.route IS NOT NULL
 AND NOT EXISTS(SELECT 1 FROM module_navigation_permissions r WHERE r.navigation_module_id=m.module_id);

-- Requirements must point to an active module/action pair.
SELECT n.module_code AS navigation_code,p.module_code AS permission_code,r.action_code
FROM module_navigation_permissions r JOIN modules n ON n.module_id=r.navigation_module_id
JOIN modules p ON p.module_id=r.permission_module_id
LEFT JOIN module_actions a ON a.module_id=p.module_id AND a.action_code=r.action_code AND a.is_active
WHERE NOT n.is_active OR NOT p.is_active OR a.module_action_id IS NULL;

-- Active submodules must have active parents and cannot parent themselves.
SELECT c.module_id,c.module_code,c.parent_module_id,p.module_code AS parent_code
FROM modules c LEFT JOIN modules p ON p.module_id=c.parent_module_id
WHERE c.is_active AND c.parent_module_id IS NOT NULL AND (p.module_id IS NULL OR NOT p.is_active OR p.module_id=c.module_id);

ROLLBACK;
