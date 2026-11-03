-- OPTIONAL, READ ONLY. Run as database administrator after migration 027.
-- No stock, permissions, transactions or tenant settings are changed.
BEGIN TRANSACTION READ ONLY;
SELECT current_database(),to_regprocedure('public.sf_transfer_unit_labels(bigint)') AS unit_label_lookup,
       EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
         AND table_name='inventory_transactions' AND column_name='serial_number_snapshot') AS movement_serial_snapshot;

-- Operational overview: outstanding quantities are expected for dispatched/partial transfers.
SELECT h.tenant_id,h.transfer_id,h.transfer_number,h.status,h.from_location_id,h.to_location_id,
       sum(i.requested_quantity) AS requested,sum(i.dispatched_quantity) AS dispatched,
       sum(i.received_quantity) AS received,sum(i.damaged_quantity) AS damaged,sum(i.lost_quantity) AS lost,
       sum(i.dispatched_quantity-i.received_quantity-i.damaged_quantity-i.lost_quantity) AS outstanding_in_transit
FROM stock_transfers h JOIN stock_transfer_items i USING(tenant_id,transfer_id)
GROUP BY h.tenant_id,h.transfer_id,h.transfer_number,h.status,h.from_location_id,h.to_location_id
ORDER BY h.tenant_id,h.transfer_id DESC;

-- Integrity checks below should return zero rows; investigate historical exceptions.
SELECT tenant_id,location_id,variant_id,current_stock
FROM vw_location_current_stock WHERE current_stock<0;

SELECT tenant_id,inventory_transaction_id,transfer_id,transaction_type,reference_type,reference_id
FROM inventory_transactions WHERE (transfer_id IS NOT NULL OR reference_type='TRANSFER')
 AND (transaction_type NOT IN('TRANSFER_IN','TRANSFER_OUT','TRANSFER_LOSS','DAMAGED')
      OR reference_type IS DISTINCT FROM 'TRANSFER' OR transfer_id IS DISTINCT FROM reference_id);

SELECT h.tenant_id,h.transfer_id,h.status,i.transfer_item_id,i.requested_quantity,i.dispatched_quantity,
       i.received_quantity,i.damaged_quantity,i.lost_quantity
FROM stock_transfers h JOIN stock_transfer_items i USING(tenant_id,transfer_id)
WHERE i.received_quantity+i.damaged_quantity+i.lost_quantity>i.dispatched_quantity
 OR i.dispatched_quantity>i.requested_quantity
 OR h.status='RECEIVED' AND i.received_quantity+i.damaged_quantity+i.lost_quantity<>i.dispatched_quantity;

SELECT tu.tenant_id,tu.transfer_item_id,tu.product_unit_id,u.location_id,u.unit_status,h.transit_location_id
FROM stock_transfer_units tu JOIN stock_transfer_items i USING(tenant_id,transfer_item_id)
JOIN stock_transfers h USING(tenant_id,transfer_id)
JOIN product_units u USING(tenant_id,product_unit_id)
WHERE tu.resolution='IN_TRANSIT'
 AND (u.unit_status IS DISTINCT FROM 'IN_TRANSIT' OR u.location_id IS DISTINCT FROM h.transit_location_id);

SELECT tenant_id,inventory_transaction_id,product_unit_id,location_id
FROM inventory_transactions WHERE product_unit_id IS NOT NULL AND serial_number_snapshot IS NULL;

-- Compare company transit ledger with unresolved dispatch quantities, per tenant/variant.
WITH outstanding AS (
 SELECT h.tenant_id,h.transit_location_id AS location_id,i.variant_id,
        sum(i.dispatched_quantity-i.received_quantity-i.damaged_quantity-i.lost_quantity) AS quantity
 FROM stock_transfers h JOIN stock_transfer_items i USING(tenant_id,transfer_id)
 GROUP BY h.tenant_id,h.transit_location_id,i.variant_id
), actual AS (
 SELECT s.tenant_id,s.location_id,s.variant_id,s.current_stock
 FROM vw_location_current_stock s JOIN store_locations l USING(tenant_id,location_id)
 WHERE l.location_type='TRANSIT'
)
SELECT coalesce(o.tenant_id,a.tenant_id) AS tenant_id,coalesce(o.location_id,a.location_id) AS location_id,
       coalesce(o.variant_id,a.variant_id) AS variant_id,coalesce(o.quantity,0) AS outstanding,
       coalesce(a.current_stock,0) AS transit_ledger
FROM outstanding o FULL JOIN actual a USING(tenant_id,location_id,variant_id)
WHERE coalesce(o.quantity,0)<>coalesce(a.current_stock,0);

-- Review effective transfer and adjustment grants; this query changes no permissions.
SELECT p.tenant_id,p.user_id,u.username,m.module_code,a.action_code,p.is_allowed
FROM user_action_permissions p JOIN users u USING(tenant_id,user_id)
JOIN module_actions a USING(module_action_id) JOIN modules m USING(module_id)
WHERE m.module_code IN('STOCK_TRANSFER','STOCK_ADJUSTMENT') AND m.is_active AND a.is_active
ORDER BY p.tenant_id,p.user_id,m.module_code,a.action_code;
ROLLBACK;
