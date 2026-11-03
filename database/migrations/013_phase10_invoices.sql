-- Execute manually in the StockFlow application database AFTER migration 012.
-- No stock, prices, payments or existing invoice numbers are changed.
BEGIN;
CREATE TABLE IF NOT EXISTS company_settings (
    company_settings_id INT PRIMARY KEY DEFAULT 1 CHECK (company_settings_id = 1),
    company_name VARCHAR(150) NOT NULL CHECK (LENGTH(BTRIM(company_name)) > 0),
    address TEXT,
    mobile_number VARCHAR(20),
    email VARCHAR(150),
    gst_number VARCHAR(30),
    invoice_footer TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP
);
-- Replace the default through Admin > Company / invoice settings before issuing invoices.
-- No fictitious address, phone or GST registration is seeded.
INSERT INTO company_settings (company_settings_id, company_name, invoice_footer)
VALUES (1, 'StockFlow', 'Thank you for your business.') ON CONFLICT (company_settings_id) DO NOTHING;

ALTER TABLE sales ADD COLUMN IF NOT EXISTS invoice_snapshot_version INT NOT NULL DEFAULT 0;
ALTER TABLE sales ADD COLUMN IF NOT EXISTS customer_mobile_snapshot VARCHAR(20);
ALTER TABLE sales ADD COLUMN IF NOT EXISTS customer_address_snapshot TEXT;
ALTER TABLE sales ADD COLUMN IF NOT EXISTS customer_gst_snapshot VARCHAR(30);
ALTER TABLE sales ADD COLUMN IF NOT EXISTS company_snapshot JSONB;
ALTER TABLE sale_items ADD COLUMN IF NOT EXISTS brand_name_snapshot VARCHAR(100);
ALTER TABLE sale_items ADD COLUMN IF NOT EXISTS model_snapshot VARCHAR(100);
ALTER TABLE sale_items ADD COLUMN IF NOT EXISTS warranty_months_snapshot INT;
-- Version 0 means these extra details were not captured on older sales.
-- Historical addresses/company details cannot be reconstructed; no backfill is invented.
CREATE INDEX IF NOT EXISTS idx_sales_sale_date ON sales(sale_date DESC, sale_id DESC);
CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id);
CREATE INDEX IF NOT EXISTS idx_payments_sale ON payments(sale_id);
CREATE INDEX IF NOT EXISTS idx_warranties_sale_item ON warranties(sale_item_id);
COMMIT;
