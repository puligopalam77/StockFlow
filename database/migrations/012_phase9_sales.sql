-- Execute manually AFTER migrations 007-011 in the application database.
-- Adds retry protection and invoice snapshots; does not post stock or payments.
BEGIN;
ALTER TABLE sales ADD COLUMN IF NOT EXISTS request_id UUID;
ALTER TABLE sales ADD COLUMN IF NOT EXISTS request_payload JSONB;
ALTER TABLE sales ADD COLUMN IF NOT EXISTS customer_name_snapshot VARCHAR(150);
ALTER TABLE sale_items ADD COLUMN IF NOT EXISTS product_name_snapshot VARCHAR(150);
ALTER TABLE sale_items ADD COLUMN IF NOT EXISTS serial_number_snapshot VARCHAR(150);
CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_request_id ON sales(request_id) WHERE request_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_sales_customer ON sales(customer_id, sale_date DESC);
CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id);
CREATE INDEX IF NOT EXISTS idx_payments_sale ON payments(sale_id);
CREATE INDEX IF NOT EXISTS idx_warranties_sale_item ON warranties(sale_item_id);
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_sale_items_values' AND conrelid = 'sale_items'::regclass) THEN
        ALTER TABLE sale_items ADD CONSTRAINT ck_sale_items_values CHECK (
            quantity > 0 AND selling_price >= 0 AND discount_amount >= 0
            AND discount_amount <= quantity::NUMERIC * selling_price
            AND tax_percentage BETWEEN 0 AND 100 AND line_total >= 0
            AND (product_unit_id IS NULL OR quantity = 1));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_sales_values' AND conrelid = 'sales'::regclass) THEN
        ALTER TABLE sales ADD CONSTRAINT ck_sales_values CHECK (
            subtotal >= 0 AND tax_amount >= 0 AND discount_amount >= 0 AND total_amount >= 0
            AND total_amount = subtotal + tax_amount - discount_amount);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_payments_positive' AND conrelid = 'payments'::regclass) THEN
        ALTER TABLE payments ADD CONSTRAINT ck_payments_positive CHECK (amount > 0);
    END IF;
END $$;
COMMIT;
