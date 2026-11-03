-- Read-only Phase 7 queries for pgAdmin. Run in StockFlow_DB after migration 009.

-- 1. Current inventory, including active products with zero stock.
SELECT p.product_id, p.product_name, c.category_name, b.brand_name,
       p.model, p.sku, p.barcode, p.is_serial_tracked, p.minimum_stock_level,
       s.current_stock, s.current_stock <= p.minimum_stock_level AS is_low_stock
FROM products p
JOIN categories c ON c.category_id = p.category_id
LEFT JOIN brands b ON b.brand_id = p.brand_id
JOIN vw_current_stock s ON s.product_id = p.product_id
WHERE p.is_active
ORDER BY p.product_name;

-- 2. Low stock.
SELECT p.product_id, p.product_name, s.current_stock, p.minimum_stock_level,
       GREATEST(p.minimum_stock_level - s.current_stock, 0) AS shortage
FROM products p JOIN vw_current_stock s ON s.product_id = p.product_id
WHERE p.is_active AND s.current_stock <= p.minimum_stock_level
ORDER BY s.current_stock, p.product_name;

-- 3. Movement history. Replace NULLs in filters as needed, e.g. 1, DATE '2026-09-01'.
WITH filters AS (SELECT NULL::INT AS product_id, NULL::DATE AS from_date, NULL::DATE AS to_date)
SELECT it.inventory_transaction_id, it.created_at, p.product_name, pu.serial_number,
       it.transaction_type, it.quantity, it.reference_type, it.reference_id, it.remarks,
       u.full_name AS created_by_name
FROM inventory_transactions it
JOIN products p ON p.product_id = it.product_id
LEFT JOIN product_units pu ON pu.product_unit_id = it.product_unit_id
JOIN users u ON u.user_id = it.created_by CROSS JOIN filters f
WHERE (f.product_id IS NULL OR it.product_id = f.product_id)
  AND (f.from_date IS NULL OR it.created_at >= f.from_date)
  AND (f.to_date IS NULL OR it.created_at < f.to_date + INTERVAL '1 day')
ORDER BY it.created_at DESC, it.inventory_transaction_id DESC;

-- 4. Available serialized units. Add AND pu.product_id = 1 to select a product.
SELECT pu.product_unit_id, p.product_name, pu.serial_number, pu.barcode, pu.unit_status, pu.purchase_id
FROM product_units pu JOIN products p ON p.product_id = pu.product_id
WHERE pu.unit_status = 'IN_STOCK' AND p.is_active
ORDER BY p.product_name, pu.serial_number;

-- 5. Serial lookup: replace BAT001 with the scanned value.
SELECT pu.*, p.product_name FROM product_units pu
JOIN products p ON p.product_id = pu.product_id
WHERE LOWER(BTRIM(pu.serial_number)) = LOWER('BAT001');

-- 6. Product search: replace battery with the search text.
SELECT p.product_id, p.product_name, p.sku, p.barcode, s.current_stock
FROM products p JOIN vw_current_stock s ON s.product_id = p.product_id
WHERE p.is_active AND (p.product_name ILIKE '%battery%' OR p.sku ILIKE '%battery%' OR p.barcode ILIKE '%battery%')
ORDER BY p.product_name;

-- 7. Serialized stock reconciliation; only discrepancies are returned.
SELECT p.product_id, p.product_name, s.current_stock,
       COUNT(pu.product_unit_id) FILTER (WHERE pu.unit_status = 'IN_STOCK') AS available_units
FROM products p JOIN vw_current_stock s ON s.product_id = p.product_id
LEFT JOIN product_units pu ON pu.product_id = p.product_id
WHERE p.is_serial_tracked
GROUP BY p.product_id, p.product_name, s.current_stock
HAVING s.current_stock <> COUNT(pu.product_unit_id) FILTER (WHERE pu.unit_status = 'IN_STOCK');

-- 8. Review movement types before migration 009; this query does not require the view.
SELECT transaction_type, COUNT(*) AS movements FROM inventory_transactions GROUP BY transaction_type;
