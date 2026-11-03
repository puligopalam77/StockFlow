-- READ ONLY. Run manually in the StockFlow application database after migrations 001-025.
-- Run as a database administrator to review every store despite row security.
-- This checks schema/data; it does not apply migrations or change assignments.
BEGIN TRANSACTION READ ONLY;

SELECT current_database() AS database_name, current_setting('server_version') AS postgres_version;

-- Every required object should be present. This verifies the Phase 1 baseline,
-- not a historical record that each migration file was executed.
SELECT name, to_regclass('public.' || name) IS NOT NULL AS present
FROM (VALUES ('tenants'),('stores'),('store_locations'),('user_store_access'),
 ('user_location_access'),('product_variants'),('variant_barcodes'),
 ('purchase_receipts'),('purchase_receipt_items'),('stock_transfers'),
 ('stock_transfer_operations'),('module_actions'),('user_action_permissions'),
 ('role_action_permissions'),('tenant_security_settings'),('vw_location_current_stock')) AS required(name);

SELECT table_name,column_name,EXISTS(
 SELECT 1 FROM information_schema.columns c WHERE c.table_schema='public'
 AND c.table_name=required.table_name AND c.column_name=required.column_name
) AS present
FROM (VALUES ('users','is_tenant_owner'),('users','security_version'),
 ('user_store_access','is_default'),('store_locations','location_type'),
 ('sales','tenant_id'),('sales','store_id'),('sales','location_id'),
 ('inventory_transactions','location_id')) AS required(table_name,column_name);

-- Stop here if required objects/columns are missing; apply only the missing
-- migrations in their documented order before executing the remaining checks.

-- Every retail employee should have a default assigned active store (or just
-- one active store) containing an active SALES_FLOOR location.
WITH assignments AS (
 SELECT u.tenant_id,u.user_id,u.username,
   count(s.store_id) AS assigned_stores,
   count(s.store_id) FILTER(WHERE a.is_default) AS default_stores,
   COALESCE(min(s.store_id) FILTER(WHERE a.is_default),
            CASE WHEN count(s.store_id)=1 THEN min(s.store_id) END) AS selected_store
 FROM users u JOIN roles r ON r.role_id=u.role_id AND r.tenant_id=u.tenant_id
 LEFT JOIN user_store_access a ON a.user_id=u.user_id AND a.tenant_id=u.tenant_id AND a.is_active
 LEFT JOIN stores s ON s.store_id=a.store_id AND s.tenant_id=u.tenant_id AND s.is_active
 WHERE u.is_active AND r.is_active AND NOT u.is_tenant_owner AND lower(btrim(r.role_name))<>'admin'
 GROUP BY u.tenant_id,u.user_id,u.username
)
SELECT a.*,chosen.location_id AS automatic_location_id,
 CASE WHEN a.assigned_stores>0 AND chosen.location_id IS NULL THEN 'ACTION REQUIRED: set default store and active sales floor'
      WHEN a.assigned_stores=0 AND chosen.location_id IS NULL THEN 'ACTION REQUIRED: assign an active store or warehouse'
      WHEN a.assigned_stores=0 THEN 'OK: warehouse staff'
      ELSE 'OK: retail staff' END AS result
FROM assignments a LEFT JOIN LATERAL (
 SELECT l.location_id FROM store_locations l
 WHERE l.tenant_id=a.tenant_id AND l.is_active AND (
   (a.assigned_stores>0 AND l.store_id=a.selected_store AND l.location_type='SALES_FLOOR') OR
   (a.assigned_stores=0 AND l.store_id IS NULL AND l.location_type='WAREHOUSE' AND EXISTS(
      SELECT 1 FROM user_location_access w WHERE w.tenant_id=a.tenant_id AND w.user_id=a.user_id AND w.location_id=l.location_id)))
 ORDER BY l.location_id LIMIT 1
) chosen ON true ORDER BY a.tenant_id,a.user_id;

-- Multiple sales floors are supported: the lowest active location ID is used.
SELECT s.tenant_id,s.store_id,s.store_name,
 count(l.location_id) AS active_sales_floors,min(l.location_id) AS automatic_sales_location
FROM stores s LEFT JOIN store_locations l ON l.tenant_id=s.tenant_id AND l.store_id=s.store_id
 AND l.is_active AND l.location_type='SALES_FLOOR'
WHERE s.is_active GROUP BY s.tenant_id,s.store_id,s.store_name ORDER BY s.tenant_id,s.store_id;

SELECT rolname,rolsuper,rolbypassrls FROM pg_roles WHERE rolname='stockflow_runtime';
SELECT relname,relrowsecurity,relforcerowsecurity FROM pg_class
WHERE relnamespace='public'::regnamespace AND relname IN('sales','inventory_transactions','purchase_receipts','product_units');

ROLLBACK;
