# Database-owned modules and submodules

Migration `029_module_hierarchy.sql` keeps every existing permission `module_id` and adds a parent-child hierarchy to the existing `modules` table. PostgreSQL remains the source for module names, codes, active status, descriptions, ordering, routes and sidebar visibility.

## Manual application

1. Stop StockFlow and back up the application database.
2. Confirm migrations through `028_shop_operations.sql` were applied.
3. Execute `database/migrations/029_module_hierarchy.sql` as a PostgreSQL superuser.
4. Execute `database/queries/module_hierarchy_checks.sql` read-only. The second, third and fourth result sets should return zero rows.
5. Start the updated application and verify the sidebar and Users/Roles module assignment screens.

Do not renumber or delete an existing module because user, role and action permissions reference `module_id`. Set `is_active=false` to deactivate it. An inactive page module is omitted from the permission catalog and navigation. An inactive parent hides its submenu group. Restore it with `is_active=true` after confirming its parent and action rows are active.

`module_navigation_permissions` controls which permission action makes a menu item visible. It allows navigation-only submodules such as **New sale** to use `SALES:CREATE` without creating a second sales permission module. The **Configuration** entry is visible when the user has `VIEW` on any configured catalog module.

The application still enforces route and API permissions independently of menu visibility. Changing a route or navigation requirement does not grant access.
