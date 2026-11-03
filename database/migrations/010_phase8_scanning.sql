-- Execute manually in StockFlow_DB after the Phase 5/6 migrations.
-- Does not change inventory or rewrite existing identifiers. Safe to rerun.
BEGIN;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM products WHERE barcode IS NOT NULL GROUP BY LOWER(BTRIM(barcode)) HAVING COUNT(*) > 1)
    OR EXISTS (SELECT 1 FROM product_units WHERE serial_number IS NOT NULL GROUP BY LOWER(BTRIM(serial_number)) HAVING COUNT(*) > 1)
    OR EXISTS (SELECT 1 FROM product_units WHERE barcode IS NOT NULL GROUP BY LOWER(BTRIM(barcode)) HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate identifiers exist (ignoring case/spaces). Review phase8_scanning_queries.sql before retrying.';
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_products_barcode_ci ON products(LOWER(BTRIM(barcode))) WHERE barcode IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_units_serial_ci ON product_units(LOWER(BTRIM(serial_number))) WHERE serial_number IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_units_barcode_ci ON product_units(LOWER(BTRIM(barcode))) WHERE barcode IS NOT NULL;
COMMIT;
