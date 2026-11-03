-- Read-only: duplicate identifiers and cross-field collisions needing manual review.
WITH codes AS (
 SELECT 'PRODUCT_BARCODE' AS kind, product_id::BIGINT AS id, LOWER(BTRIM(barcode)) AS code FROM products WHERE barcode IS NOT NULL
 UNION ALL SELECT 'SERIAL_NUMBER', product_unit_id, LOWER(BTRIM(serial_number)) FROM product_units WHERE serial_number IS NOT NULL
 UNION ALL SELECT 'UNIT_BARCODE', product_unit_id, LOWER(BTRIM(barcode)) FROM product_units WHERE barcode IS NOT NULL
)
SELECT code, STRING_AGG(kind || ':' || id, ', ') AS matching_records
FROM codes GROUP BY code HAVING COUNT(*) > 1 ORDER BY code;

-- Inspect registered physical units without changing stock.
SELECT pu.product_unit_id, p.product_name, p.barcode AS product_barcode,
       pu.serial_number, pu.barcode AS unit_barcode, pu.unit_status
FROM product_units pu JOIN products p ON p.product_id = pu.product_id
ORDER BY pu.product_unit_id DESC LIMIT 100;
