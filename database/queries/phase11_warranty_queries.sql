-- OPTIONAL read-only checks. Run AFTER migrations/014_phase11_warranty.sql.
-- 1. Confirm Phase 11 objects.
SELECT to_regclass('warranty_claims') AS claims,
       to_regclass('warranty_events') AS history,
       to_regclass('warranty_claim_number_seq') AS claim_sequence;

-- 2. Warranty lookup: replace BAT001 with your serial number.
SELECT w.*, pu.serial_number, s.invoice_number,
       CASE WHEN s.status = 'CANCELLED' THEN 'CANCELLED'
            WHEN w.warranty_status IN ('ACTIVE','CLAIMED') AND w.warranty_end_date < CURRENT_DATE THEN 'EXPIRED'
            ELSE w.warranty_status END AS effective_status,
       (s.status = 'COMPLETED' AND w.warranty_status IN ('ACTIVE','CLAIMED')
        AND CURRENT_DATE BETWEEN w.warranty_start_date AND w.warranty_end_date) AS is_under_warranty
FROM warranties w
JOIN sale_items si ON si.sale_item_id = w.sale_item_id
JOIN sales s ON s.sale_id = si.sale_id
LEFT JOIN product_units pu ON pu.product_unit_id = w.product_unit_id
WHERE LOWER(BTRIM(pu.serial_number)) = LOWER('BAT001')
ORDER BY w.warranty_id DESC;

-- 3. Every warranty for an invoice: replace INV-2026-000001.
SELECT w.*, pu.serial_number
FROM warranties w JOIN sale_items si ON si.sale_item_id = w.sale_item_id
JOIN sales s ON s.sale_id = si.sale_id
LEFT JOIN product_units pu ON pu.product_unit_id = w.product_unit_id
WHERE LOWER(s.invoice_number) = LOWER('INV-2026-000001')
ORDER BY w.warranty_id;

-- 4. Expiring within 30 days.
SELECT w.warranty_id, p.product_name, pu.serial_number, s.invoice_number,
       c.customer_name, c.mobile_number, w.warranty_end_date,
       w.warranty_end_date - CURRENT_DATE AS remaining_days
FROM warranties w JOIN sale_items si ON si.sale_item_id = w.sale_item_id
JOIN sales s ON s.sale_id = si.sale_id JOIN products p ON p.product_id = si.product_id
LEFT JOIN product_units pu ON pu.product_unit_id = w.product_unit_id
LEFT JOIN customers c ON c.customer_id = s.customer_id
WHERE s.status = 'COMPLETED' AND w.warranty_status IN ('ACTIVE','CLAIMED')
  AND w.warranty_end_date BETWEEN CURRENT_DATE AND CURRENT_DATE + 30
ORDER BY w.warranty_end_date;

-- 5. Claims and replacement movement audit.
SELECT wc.*, old_unit.serial_number AS original_serial, new_unit.serial_number AS replacement_serial,
       it.inventory_transaction_id, it.quantity
FROM warranty_claims wc JOIN warranties w ON w.warranty_id = wc.warranty_id
LEFT JOIN product_units old_unit ON old_unit.product_unit_id = w.product_unit_id
LEFT JOIN product_units new_unit ON new_unit.product_unit_id = wc.replacement_product_unit_id
LEFT JOIN inventory_transactions it ON it.reference_type = 'WARRANTY_CLAIM'
    AND it.reference_id = wc.warranty_claim_id AND it.transaction_type = 'WARRANTY_REPLACEMENT'
ORDER BY wc.warranty_claim_id DESC;

-- 6. Serialized stock reconciliation (should return zero rows).
SELECT p.product_id, p.product_name, s.current_stock,
       COUNT(pu.product_unit_id) FILTER (WHERE pu.unit_status = 'IN_STOCK') AS available_units
FROM products p JOIN vw_current_stock s ON s.product_id = p.product_id
LEFT JOIN product_units pu ON pu.product_id = p.product_id
WHERE p.is_serial_tracked
GROUP BY p.product_id, p.product_name, s.current_stock
HAVING s.current_stock <> COUNT(pu.product_unit_id) FILTER (WHERE pu.unit_status = 'IN_STOCK');

-- 7. Older sold items missing registration. Use the sale_item_id in Warranty lookup.
SELECT si.sale_item_id, s.invoice_number, si.product_unit_id,
       COALESCE(si.warranty_months_snapshot,p.warranty_months) AS warranty_months
FROM sale_items si JOIN sales s ON s.sale_id=si.sale_id JOIN products p ON p.product_id=si.product_id
WHERE s.status='COMPLETED' AND COALESCE(si.warranty_months_snapshot,p.warranty_months)>0
AND NOT EXISTS (SELECT 1 FROM warranties w WHERE w.sale_item_id=si.sale_item_id AND w.parent_warranty_id IS NULL);
