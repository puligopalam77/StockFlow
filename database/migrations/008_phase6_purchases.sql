-- Run manually in StockFlow_DB after 007_phase5_master_data.sql.
-- Existing purchase/item history is preserved. No stock is posted by this migration.
BEGIN;
ALTER TABLE purchases ALTER COLUMN status SET DEFAULT 'DRAFT';
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS serial_numbers TEXT[] NOT NULL DEFAULT '{}';
-- NULL preserves the distinction between historical items and new items with a tracking snapshot.
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS is_serial_tracked BOOLEAN;
ALTER TABLE product_units ADD COLUMN IF NOT EXISTS purchase_item_id BIGINT;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM product_units WHERE serial_number IS NOT NULL
        GROUP BY LOWER(BTRIM(serial_number)) HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Duplicate serial numbers exist ignoring case/spaces. Resolve them before applying Phase 6.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_product_units_purchase_item' AND conrelid = 'product_units'::regclass) THEN
        ALTER TABLE product_units ADD CONSTRAINT fk_product_units_purchase_item
            FOREIGN KEY (purchase_item_id) REFERENCES purchase_items(purchase_item_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_product_units_purchase' AND conrelid = 'product_units'::regclass) THEN
        ALTER TABLE product_units ADD CONSTRAINT fk_product_units_purchase
            FOREIGN KEY (purchase_id) REFERENCES purchases(purchase_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_items_values' AND conrelid = 'purchase_items'::regclass) THEN
        ALTER TABLE purchase_items ADD CONSTRAINT ck_purchase_items_values CHECK (
            quantity > 0 AND purchase_price >= 0 AND tax_percentage BETWEEN 0 AND 100 AND line_total >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchases_values' AND conrelid = 'purchases'::regclass) THEN
        ALTER TABLE purchases ADD CONSTRAINT ck_purchases_values CHECK (
            subtotal >= 0 AND tax_amount >= 0 AND discount_amount >= 0 AND total_amount >= 0
            AND status IN ('DRAFT', 'COMPLETED', 'CANCELLED'));
    END IF;
END $$;

-- A distinct name is intentional: the baseline already has a case-sensitive unique serial index.
CREATE UNIQUE INDEX IF NOT EXISTS ux_product_units_serial_ci ON product_units (LOWER(BTRIM(serial_number))) WHERE serial_number IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_purchase_items_purchase ON purchase_items(purchase_id);
CREATE INDEX IF NOT EXISTS idx_product_units_purchase_item ON product_units(purchase_item_id);
CREATE INDEX IF NOT EXISTS idx_inventory_reference ON inventory_transactions(reference_type, reference_id);
COMMIT;

-- Purchase numbers use the existing purchases.purchase_id BIGSERIAL sequence via nextval.
-- There is no MAX(id)+1 race and no separate sequence to reset.
