-- Optional read-only queries. Run AFTER migration 015. Edit sample identifiers below.

-- 1. Confirm required objects.
SELECT to_regclass('service_requests') AS tickets,to_regclass('service_history') AS history,
       to_regclass('service_parts') AS parts,to_regclass('service_operations') AS retry_receipts,
       to_regclass('service_request_number_seq') AS service_sequence;

-- 2. Pending tickets, including ready for collection.
SELECT sr.service_request_id,sr.service_number,c.customer_name,c.mobile_number,p.product_name,
       pu.serial_number,sr.status,u.full_name AS technician,sr.received_date,sr.expected_delivery_date
FROM service_requests sr JOIN customers c ON c.customer_id=sr.customer_id
JOIN products p ON p.product_id=sr.product_id
LEFT JOIN product_units pu ON pu.product_unit_id=sr.product_unit_id
LEFT JOIN users u ON u.user_id=sr.assigned_to
WHERE sr.status NOT IN ('DELIVERED','CANCELLED')
ORDER BY sr.received_date DESC,sr.service_request_id DESC;

-- 3. Completed services with separate parts/labour totals.
SELECT sr.service_request_id,sr.service_number,c.customer_name,sr.delivered_date,
       sr.service_charge,COALESCE(SUM(sp.total_amount),0) AS parts_total,
       sr.service_charge+COALESCE(SUM(sp.total_amount),0) AS total_charge
FROM service_requests sr JOIN customers c ON c.customer_id=sr.customer_id
LEFT JOIN service_parts sp ON sp.service_request_id=sr.service_request_id
WHERE sr.status='DELIVERED'
GROUP BY sr.service_request_id,c.customer_name
ORDER BY sr.delivered_date DESC;

-- 4. History for one service number: replace SRV-2026-000001.
SELECT sh.changed_at,sh.action_name,sh.old_status,sh.new_status,sh.remarks,
       u.full_name AS changed_by,sh.details
FROM service_history sh JOIN service_requests sr ON sr.service_request_id=sh.service_request_id
JOIN users u ON u.user_id=sh.changed_by
WHERE sr.service_number='SRV-2026-000001'
ORDER BY sh.service_history_id;

-- 5. Spare-part stock audit; each part row should have one matching outbound movement.
SELECT sr.service_number,sp.service_part_id,sp.product_name_snapshot,pu.serial_number,
       sp.quantity,sp.unit_price,sp.total_amount,it.inventory_transaction_id,it.quantity AS issued_quantity
FROM service_parts sp JOIN service_requests sr ON sr.service_request_id=sp.service_request_id
LEFT JOIN product_units pu ON pu.product_unit_id=sp.product_unit_id
LEFT JOIN inventory_transactions it ON it.reference_type='SERVICE_PART'
    AND it.reference_id=sp.service_part_id AND it.transaction_type='SERVICE_OUT'
ORDER BY sp.service_part_id DESC;

-- 6. Open service/unit mismatches (normally no rows).
SELECT sr.service_request_id,sr.service_number,sr.status,pu.serial_number,pu.unit_status
FROM service_requests sr JOIN product_units pu ON pu.product_unit_id=sr.product_unit_id
WHERE sr.status NOT IN ('DELIVERED','CANCELLED') AND pu.unit_status<>'IN_SERVICE';

-- 7. Serial stock reconciliation (normally no rows).
SELECT p.product_id,p.product_name,s.current_stock,
       COUNT(pu.product_unit_id) FILTER (WHERE pu.unit_status='IN_STOCK') AS available_units
FROM products p JOIN vw_current_stock s ON s.product_id=p.product_id
LEFT JOIN product_units pu ON pu.product_id=p.product_id
WHERE p.is_serial_tracked
GROUP BY p.product_id,p.product_name,s.current_stock
HAVING s.current_stock<>COUNT(pu.product_unit_id) FILTER (WHERE pu.unit_status='IN_STOCK');

-- 8. Active technician choices (no credentials returned).
SELECT u.user_id,u.full_name,r.role_name FROM users u JOIN roles r ON r.role_id=u.role_id
WHERE u.is_active AND r.is_active AND r.role_name IN ('Admin','Service')
ORDER BY u.full_name;
